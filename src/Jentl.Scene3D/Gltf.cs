using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Jentl.Assets;
using Jentl.Geometry;
using Jentl.Raster;

namespace Jentl.Scene3D;

/// <summary>
/// A model read from a glTF 2.0 file: its node tree (one scene), meshes, materials, textures and animations. Make
/// copies to put in a scene with <see cref="Instantiate"/>; they share the meshes, so a hundred trees cost one.
/// </summary>
public sealed class GltfModel
{
    private readonly Node[] _nodes;
    private readonly int[] _roots;
    private readonly int[][] _children;
    private readonly (int[] Joints, Matrix4x4[] InverseBind)[] _skins;
    private readonly int?[] _nodeSkins;

    internal GltfModel(Node[] nodes, int[][] children, int[] roots, IReadOnlyList<Material> materials, IReadOnlyList<AnimationClip> animations, IReadOnlyList<string> warnings, (int[] Joints, Matrix4x4[] InverseBind)[] skins, int?[] nodeSkins)
    {
        (_nodes, _children, _roots) = (nodes, children, roots);
        (_skins, _nodeSkins) = (skins, nodeSkins);
        Materials = materials;
        Animations = animations;
        Warnings = warnings;
    }

    public IReadOnlyList<Material> Materials { get; }

    /// <summary>The animations, targeting nodes by their index in the file (as <see cref="ModelInstance.Nodes"/> are).</summary>
    public IReadOnlyList<AnimationClip> Animations { get; }

    /// <summary>What was in the file but left out: morph targets, extra scenes and the like.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>The animation by this name, or null.</summary>
    public AnimationClip? Animation(string name) => Animations.FirstOrDefault(clip => clip.Name == name);

    /// <summary>A fresh copy of the model's node tree under one root named <paramref name="name"/>.</summary>
    public ModelInstance Instantiate(string name = "model")
    {
        var nodes = new Node[_nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
        {
            var source = _nodes[i];
            nodes[i] = new Node(source.Name) { Position = source.Position, Rotation = source.Rotation, Scale = source.Scale };
            nodes[i].Parts.AddRange(source.Parts);
        }

        for (var i = 0; i < nodes.Length; i++)
        {
            foreach (var child in _children[i])
            {
                nodes[i].Add(nodes[child]);
            }
        }

        var root = new Node(name);
        foreach (var index in _roots)
        {
            root.Add(nodes[index]);
        }

        // Each copy's skins have that copy's joints.
        for (var i = 0; i < nodes.Length; i++)
        {
            if (_nodeSkins[i] is { } skin)
            {
                nodes[i].Skin = new Skin([.. _skins[skin].Joints.Select(j => nodes[j])], _skins[skin].InverseBind);
            }
        }

        return new ModelInstance(root, nodes);
    }
}

/// <summary>One copy of a <see cref="GltfModel"/>: the root to put in a scene, and its nodes by their index in the file.</summary>
public sealed class ModelInstance(Node root, IReadOnlyList<Node> nodes)
{
    public Node Root { get; } = root;

    public IReadOnlyList<Node> Nodes { get; } = nodes;

    /// <summary>A player for this copy's nodes.</summary>
    public AnimationPlayer CreatePlayer() => new(Nodes);

    /// <summary>Bends every skinned mesh to its joints' pose: call after the animation has moved them, before drawing.</summary>
    public void UpdateSkins()
    {
        foreach (var node in Nodes)
        {
            node.Skin?.Deform(node);
        }
    }
}

/// <summary>
/// Reads glTF 2.0, the common format for 3D models (Blender, most tools and stores export it): <c>.gltf</c> with its
/// buffers and images in files beside it or inside it, or the single-file <c>.glb</c>. Triangles with positions,
/// normals and texture coordinates; base colour, texture, emission, transparency, double sides and a shine guessed
/// from roughness; the node tree; animations of position, rotation and scale; and skins (bent on the CPU by
/// <see cref="ModelInstance.UpdateSkins"/>). Morph targets, cameras,
/// lights and progressive JPEG textures are left out, each with a <see cref="GltfModel.Warnings">warning</see>.
/// </summary>
public static class Gltf
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinaryChunk = 0x004E4942;

    /// <summary>Reads a model file, finding the files it refers to beside it.</summary>
    public static GltfModel Load(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        return Load(File.ReadAllBytes(path), uri => File.ReadAllBytes(Path.Combine(folder, Uri.UnescapeDataString(uri))));
    }

    /// <summary>
    /// Reads a model from <paramref name="file"/> (<c>.gltf</c> text or <c>.glb</c>), asking <paramref name="resolve"/>
    /// for the files it refers to by relative URI (embedded <c>data:</c> URIs need none).
    /// </summary>
    public static GltfModel Load(byte[] file, Func<string, byte[]>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        byte[]? binary = null;
        ReadOnlyMemory<byte> json = file;
        if (file.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(file) == GlbMagic)
        {
            (json, binary) = SplitGlb(file);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return new Reader(document.RootElement, binary, resolve).Read();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or FormatException)
        {
            throw new InvalidDataException($"The model isn't valid glTF: {exception.Message}", exception);
        }
    }

    private static (ReadOnlyMemory<byte> Json, byte[]? Binary) SplitGlb(byte[] file)
    {
        var length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(8)), (uint)file.Length);
        ReadOnlyMemory<byte> json = default;
        byte[]? binary = null;
        for (var at = 12; at + 8 <= length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));
            var type = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 4));
            if (at + 8 + size > length)
            {
                throw new InvalidDataException("The .glb file is cut short.");
            }

            if (type == JsonChunk)
            {
                json = file.AsMemory(at + 8, size);
            }
            else if (type == BinaryChunk && binary is null)
            {
                binary = file.AsSpan(at + 8, size).ToArray();
            }

            at += 8 + size;
        }

        return json.Length > 0 ? (json, binary) : throw new InvalidDataException("The .glb file has no JSON chunk.");
    }

    private sealed class Reader(JsonElement root, byte[]? binary, Func<string, byte[]>? resolve)
    {
        private readonly List<string> _warnings = [];
        private readonly Dictionary<int, byte[]> _buffers = [];
        private readonly Dictionary<int, Texture?> _textures = [];

        public GltfModel Read()
        {
            var version = root.TryGetProperty("asset", out var asset) && asset.TryGetProperty("version", out var v) ? v.GetString() : null;
            if (version is null || !version.StartsWith('2'))
            {
                throw new InvalidDataException($"Only glTF 2.0 can be read, not version '{version ?? "(none)"}'.");
            }

            foreach (var required in Items(root, "extensionsRequired"))
            {
                if (required.GetString() is not ("KHR_materials_unlit" or "KHR_texture_transform"))
                {
                    throw new InvalidDataException($"The model needs the extension {required.GetString()}, which isn't supported.");
                }
            }

            var materials = Items(root, "materials").Select(ReadMaterial).ToList();
            var meshes = Items(root, "meshes").Select((mesh, i) => ReadMesh(mesh, i, materials)).ToList();
            var nodeElements = Items(root, "nodes").ToList();
            var nodes = new Node[nodeElements.Count];
            var children = new int[nodeElements.Count][];
            var hasParent = new bool[nodeElements.Count];
            for (var i = 0; i < nodes.Length; i++)
            {
                var element = nodeElements[i];
                nodes[i] = ReadNode(element, i, meshes);
                children[i] = [.. Items(element, "children").Select(c => c.GetInt32())];
                foreach (var child in children[i])
                {
                    if (child < 0 || child >= nodes.Length || hasParent[child])
                    {
                        throw new InvalidDataException($"Node {i} has a child {child} that is missing or has another parent.");
                    }

                    hasParent[child] = true;
                }
            }

            var scenes = Items(root, "scenes").ToList();
            var scene = root.TryGetProperty("scene", out var chosen) ? chosen.GetInt32() : 0;
            int[] roots = scene < scenes.Count
                ? [.. Items(scenes[scene], "nodes").Select(n => n.GetInt32())]
                : [.. Enumerable.Range(0, nodes.Length).Where(i => !hasParent[i])];
            if (scenes.Count > 1)
            {
                _warnings.Add($"The file has {scenes.Count} scenes; only scene {scene} is read.");
            }

            var animations = Items(root, "animations").Select(ReadAnimation).ToList();
            var skins = Items(root, "skins").Select(ReadSkin).ToArray();
            var nodeSkins = nodeElements.Select(e => Int(e, "skin")).ToArray();
            if (nodeSkins.Any(s => s is { } index && (index < 0 || index >= skins.Length)) || skins.Any(s => s.Joints.Any(j => j < 0 || j >= nodes.Length)))
            {
                throw new InvalidDataException("A skin, or one of its joints, is missing.");
            }

            return new GltfModel(nodes, children, roots, materials, animations, _warnings, skins, nodeSkins);
        }

        private static JsonElement[] Items(JsonElement element, string name) =>
            element.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array ? [.. array.EnumerateArray()] : [];

        private static float[] Floats(JsonElement element, string name, params float[] fallback) =>
            !element.TryGetProperty(name, out var value) ? fallback
            : value.ValueKind == JsonValueKind.Number ? [value.GetSingle()]
            : [.. value.EnumerateArray().Select(e => e.GetSingle())];

        private static int? Int(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetInt32() : null;

        private static Node ReadNode(JsonElement element, int index, List<List<MeshPart>> meshes)
        {
            var node = new Node(element.TryGetProperty("name", out var name) ? name.GetString() ?? "" : $"node{index}");
            if (element.TryGetProperty("matrix", out _))
            {
                // Column-major with column vectors is, element for element, row-major with row vectors.
                var m = Floats(element, "matrix");
                node.Local = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
            }
            else
            {
                var t = Floats(element, "translation", 0, 0, 0);
                var r = Floats(element, "rotation", 0, 0, 0, 1);
                var s = Floats(element, "scale", 1, 1, 1);
                node.Position = new Vector3(t[0], t[1], t[2]);
                node.Rotation = Quaternion.Normalize(new Quaternion(r[0], r[1], r[2], r[3]));
                node.Scale = new Vector3(s[0], s[1], s[2]);
            }

            if (Int(element, "mesh") is { } mesh)
            {
                node.Parts.AddRange(meshes[mesh]);
            }

            return node;
        }

        private Material ReadMaterial(JsonElement element)
        {
            var color = Vector4.One;
            Texture? texture = null;
            var (metallic, roughness) = (1f, 1f);
            if (element.TryGetProperty("pbrMetallicRoughness", out var pbr))
            {
                var c = Floats(pbr, "baseColorFactor", 1, 1, 1, 1);
                color = new Vector4(c[0], c[1], c[2], c[3]);
                metallic = Floats(pbr, "metallicFactor", 1)[0];
                roughness = Floats(pbr, "roughnessFactor", 1)[0];
                if (pbr.TryGetProperty("baseColorTexture", out var reference) && Int(reference, "index") is { } index)
                {
                    texture = ReadTexture(index);
                }
            }

            var mode = element.TryGetProperty("alphaMode", out var alpha) ? alpha.GetString() : "OPAQUE";
            var unlit = element.TryGetProperty("extensions", out var extensions) && extensions.TryGetProperty("KHR_materials_unlit", out _);
            var emissive = Floats(element, "emissiveFactor", 0, 0, 0);

            // A rough guess at the shine from the physically based numbers: smooth surfaces shine tightly, metals in their own colour.
            var smooth = 1 - Math.Clamp(roughness, 0, 1);
            var shine = Vector3.Lerp(Vector3.One, new Vector3(color.X, color.Y, color.Z), Math.Clamp(metallic, 0, 1)) * (smooth * smooth * 0.8f);
            return new Material(mode == "OPAQUE" ? color with { W = 1 } : color, unlit ? Shading.Unlit : Shading.Smooth, texture, Translucent: mode == "BLEND")
            {
                Emissive = new Vector3(emissive[0], emissive[1], emissive[2]),
                Specular = shine,
                Shininess = 4 + (124 * smooth * smooth),
                DoubleSided = element.TryGetProperty("doubleSided", out var sides) && sides.GetBoolean(),
            };
        }

        private Texture? ReadTexture(int index)
        {
            if (_textures.TryGetValue(index, out var known))
            {
                return known;
            }

            var element = Items(root, "textures")[index];
            Texture? texture = null;
            if (Int(element, "source") is { } source)
            {
                var image = Items(root, "images")[source];
                var bytes = image.TryGetProperty("uri", out var uri) ? Fetch(uri.GetString()!) : View(Int(image, "bufferView") ?? throw new InvalidDataException($"Image {source} has neither a uri nor a bufferView."));
                var picture = Picture(bytes, source);
                if (picture is not null)
                {
                    var (filter, wrap) = (TextureFilter.Bilinear, TextureWrap.Repeat);
                    if (Int(element, "sampler") is { } samplerIndex)
                    {
                        var sampler = Items(root, "samplers")[samplerIndex];
                        filter = Int(sampler, "magFilter") == 9728 ? TextureFilter.Nearest : TextureFilter.Bilinear;
                        wrap = Int(sampler, "wrapS") == 33071 ? TextureWrap.Clamp : TextureWrap.Repeat;
                    }

                    texture = new Texture(picture.Width, picture.Height, Texture.FromRgba(picture.Width, picture.Height, picture.Pixels).Pixels) { Filter = filter, Wrap = wrap };
                }
            }

            _textures[index] = texture;
            return texture;
        }

        private Image? Picture(byte[] bytes, int source)
        {
            try
            {
                if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P')
                {
                    return Png.Decode(bytes);
                }

                if (Jpeg.IsJpeg(bytes))
                {
                    return Jpeg.Decode(bytes);
                }

                _warnings.Add($"Image {source} is neither PNG nor JPEG; its material is drawn in its colour alone.");
            }
            catch (NotSupportedException unsupported)
            {
                _warnings.Add($"Image {source} can't be read ({unsupported.Message}); its material is drawn in its colour alone.");
            }

            return null;
        }

        private List<MeshPart> ReadMesh(JsonElement element, int index, List<Material> materials)
        {
            var parts = new List<MeshPart>();
            foreach (var primitive in Items(element, "primitives"))
            {
                var mode = Int(primitive, "mode") ?? 4;
                if (mode is < 4 or > 6)
                {
                    _warnings.Add($"Mesh {index} has points or lines, which are left out.");
                    continue;
                }

                if (primitive.TryGetProperty("targets", out _))
                {
                    _warnings.Add($"Mesh {index} has morph targets, which aren't supported.");
                }

                var attributes = primitive.GetProperty("attributes");
                var (positions, _) = ReadAccessor(attributes.GetProperty("POSITION").GetInt32());
                var count = positions.Length / 3;
                var corners = Int(primitive, "indices") is { } indices
                    ? [.. ReadAccessor(indices).Values.Select(i => (int)i)]
                    : Enumerable.Range(0, count).ToArray();
                var triangles = Triangles(corners, mode).Where(t => t.A != t.B && t.B != t.C && t.A != t.C).ToList();
                var normals = Int(attributes, "NORMAL") is { } normal ? Vectors3(ReadAccessor(normal).Values) : null;
                var uvs = Int(attributes, "TEXCOORD_0") is { } uv ? Vectors2(ReadAccessor(uv).Values) : null;
                var material = Int(primitive, "material") is { } m ? materials[m] : new Material(Vector4.One);

                // Without normals glTF asks for flat shading.
                if (normals is null && material.Shading == Shading.Smooth)
                {
                    material = material with { Shading = Shading.Flat };
                }

                // A skinned mesh: four joints and weights per vertex.
                SkinWeights? weights = null;
                if (Int(attributes, "JOINTS_0") is { } joints && Int(attributes, "WEIGHTS_0") is { } weighting)
                {
                    weights = new SkinWeights([.. ReadAccessor(joints).Values.Select(j => (int)j)], ReadAccessor(weighting).Values);
                }

                parts.Add(new MeshPart(new Mesh(Vectors3(positions), triangles, normals, uvs), material) { Weights = weights });
            }

            return parts;
        }

        private static IEnumerable<TriangleIndices> Triangles(int[] corners, int mode)
        {
            switch (mode)
            {
                case 4:
                    for (var i = 0; i + 2 < corners.Length; i += 3)
                    {
                        yield return new TriangleIndices(corners[i], corners[i + 1], corners[i + 2]);
                    }

                    break;
                case 5:
                    // A strip: every other triangle runs the other way round.
                    for (var i = 0; i + 2 < corners.Length; i++)
                    {
                        yield return i % 2 == 0
                            ? new TriangleIndices(corners[i], corners[i + 1], corners[i + 2])
                            : new TriangleIndices(corners[i + 1], corners[i], corners[i + 2]);
                    }

                    break;
                default:
                    for (var i = 1; i + 1 < corners.Length; i++)
                    {
                        yield return new TriangleIndices(corners[0], corners[i], corners[i + 1]);
                    }

                    break;
            }
        }

        private AnimationClip ReadAnimation(JsonElement element, int index)
        {
            var samplers = Items(element, "samplers").ToList();
            var channels = new List<AnimationChannel>();
            foreach (var channel in Items(element, "channels"))
            {
                var target = channel.GetProperty("target");
                var path = target.TryGetProperty("path", out var p) ? p.GetString() : null;
                if (Int(target, "node") is not { } node || path is not ("translation" or "rotation" or "scale"))
                {
                    _warnings.Add($"Animation {index} moves {path ?? "something"} that isn't a node's position, rotation or scale; that is left out.");
                    continue;
                }

                var sampler = samplers[channel.GetProperty("sampler").GetInt32()];
                var times = ReadAccessor(sampler.GetProperty("input").GetInt32()).Values;
                var (values, width) = ReadAccessor(sampler.GetProperty("output").GetInt32());
                var interpolation = (sampler.TryGetProperty("interpolation", out var i) ? i.GetString() : "LINEAR") switch
                {
                    "STEP" => Interpolation.Step,
                    "CUBICSPLINE" => Interpolation.CubicSpline,
                    _ => Interpolation.Linear,
                };
                var keys = new Vector4[values.Length / width];
                for (var k = 0; k < keys.Length; k++)
                {
                    keys[k] = new Vector4(values[k * width], values[(k * width) + 1], values[(k * width) + 2], width > 3 ? values[(k * width) + 3] : 0);
                }

                var kind = path switch
                {
                    "translation" => AnimationPath.Translation,
                    "rotation" => AnimationPath.Rotation,
                    _ => AnimationPath.Scale,
                };
                channels.Add(new AnimationChannel(node, kind, times, keys, interpolation));
            }

            var name = element.TryGetProperty("name", out var n) ? n.GetString() ?? "" : $"animation{index}";
            return new AnimationClip(name, channels);
        }

        private (int[] Joints, Matrix4x4[] InverseBind) ReadSkin(JsonElement element, int index)
        {
            int[] joints = [.. Items(element, "joints").Select(j => j.GetInt32())];
            var inverse = new Matrix4x4[joints.Length];
            Array.Fill(inverse, Matrix4x4.Identity);
            if (Int(element, "inverseBindMatrices") is { } accessor)
            {
                var (m, _) = ReadAccessor(accessor);
                if (m.Length < joints.Length * 16)
                {
                    throw new InvalidDataException($"Skin {index} has fewer inverse bind matrices than joints.");
                }

                // Column-major with column vectors is, element for element, row-major with row vectors.
                for (var j = 0; j < joints.Length; j++)
                {
                    var o = j * 16;
                    inverse[j] = new Matrix4x4(m[o], m[o + 1], m[o + 2], m[o + 3], m[o + 4], m[o + 5], m[o + 6], m[o + 7], m[o + 8], m[o + 9], m[o + 10], m[o + 11], m[o + 12], m[o + 13], m[o + 14], m[o + 15]);
                }
            }

            return (joints, inverse);
        }

        private static Vector3[] Vectors3(float[] values)
        {
            var vectors = new Vector3[values.Length / 3];
            for (var i = 0; i < vectors.Length; i++)
            {
                vectors[i] = new Vector3(values[i * 3], values[(i * 3) + 1], values[(i * 3) + 2]);
            }

            return vectors;
        }

        private static Vector2[] Vectors2(float[] values)
        {
            var vectors = new Vector2[values.Length / 2];
            for (var i = 0; i < vectors.Length; i++)
            {
                vectors[i] = new Vector2(values[i * 2], values[(i * 2) + 1]);
            }

            return vectors;
        }

        /// <summary>An accessor's numbers, unpacked to floats (normalised integers to 0..1 or -1..1), and how many make one element.</summary>
        private (float[] Values, int Width) ReadAccessor(int index)
        {
            var accessor = Items(root, "accessors")[index];
            if (accessor.TryGetProperty("sparse", out _))
            {
                throw new InvalidDataException($"Accessor {index} is sparse, which isn't supported.");
            }

            var width = accessor.GetProperty("type").GetString() switch
            {
                "SCALAR" => 1,
                "VEC2" => 2,
                "VEC3" => 3,
                "VEC4" => 4,
                "MAT2" => 4,
                "MAT3" => 9,
                "MAT4" => 16,
                var other => throw new InvalidDataException($"Accessor {index} has an unknown type '{other}'."),
            };
            var component = accessor.GetProperty("componentType").GetInt32();
            var size = component switch
            {
                5120 or 5121 => 1,
                5122 or 5123 => 2,
                5125 or 5126 => 4,
                _ => throw new InvalidDataException($"Accessor {index} has an unknown component type {component}."),
            };
            var count = accessor.GetProperty("count").GetInt32();
            var normalized = accessor.TryGetProperty("normalized", out var n) && n.GetBoolean();
            var values = new float[count * width];
            if (Int(accessor, "bufferView") is not { } viewIndex)
            {
                return (values, width);
            }

            var view = Items(root, "bufferViews")[viewIndex];
            var data = Buffer(view.GetProperty("buffer").GetInt32());
            var start = (Int(view, "byteOffset") ?? 0) + (Int(accessor, "byteOffset") ?? 0);
            var stride = Int(view, "byteStride") ?? (size * width);
            var end = start + ((count - 1) * stride) + (size * width);
            if (count > 0 && (start < 0 || end > data.Length))
            {
                throw new InvalidDataException($"Accessor {index} reaches past the end of its buffer.");
            }

            for (var e = 0; e < count; e++)
            {
                for (var c = 0; c < width; c++)
                {
                    var span = data.AsSpan(start + (e * stride) + (c * size));
                    values[(e * width) + c] = component switch
                    {
                        5126 => BinaryPrimitives.ReadSingleLittleEndian(span),
                        5125 => BinaryPrimitives.ReadUInt32LittleEndian(span),
                        5123 => normalized ? BinaryPrimitives.ReadUInt16LittleEndian(span) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian(span),
                        5122 => normalized ? Math.Max(BinaryPrimitives.ReadInt16LittleEndian(span) / 32767f, -1) : BinaryPrimitives.ReadInt16LittleEndian(span),
                        5121 => normalized ? span[0] / 255f : span[0],
                        _ => normalized ? Math.Max((sbyte)span[0] / 127f, -1) : (sbyte)span[0],
                    };
                }
            }

            return (values, width);
        }

        private byte[] View(int index)
        {
            var view = Items(root, "bufferViews")[index];
            var data = Buffer(view.GetProperty("buffer").GetInt32());
            return data.AsSpan(Int(view, "byteOffset") ?? 0, view.GetProperty("byteLength").GetInt32()).ToArray();
        }

        private byte[] Buffer(int index)
        {
            if (!_buffers.TryGetValue(index, out var data))
            {
                var buffer = Items(root, "buffers")[index];
                data = buffer.TryGetProperty("uri", out var uri)
                    ? Fetch(uri.GetString()!)
                    : binary ?? throw new InvalidDataException($"Buffer {index} has no uri and there is no binary chunk.");
                _buffers[index] = data;
            }

            return data;
        }

        private byte[] Fetch(string uri)
        {
            if (uri.StartsWith("data:", StringComparison.Ordinal))
            {
                var comma = uri.IndexOf(',', StringComparison.Ordinal);
                return comma > 0 && uri[..comma].EndsWith(";base64", StringComparison.Ordinal)
                    ? System.Convert.FromBase64String(uri[(comma + 1)..])
                    : throw new InvalidDataException("Only base64 data URIs can be read.");
            }

            return resolve is null
                ? throw new InvalidDataException($"The model refers to the file '{uri}'; load it with a way to find files.")
                : resolve(uri);
        }
    }
}
