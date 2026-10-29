using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jentl.Assets;

namespace Jentl.Scene3D.Tests;

/// <summary>Writes small glTF files for the tests: a textured triangle under a moved parent, with an animation.</summary>
internal static class GltfFixture
{
    /// <summary>A 2x2 PNG: red, green / blue, see-through white.</summary>
    public static byte[] Picture()
    {
        var image = new Image(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 0]);
        return Png.Encode(image);
    }

    /// <summary>The buffer: three positions, three normals, three UVs, three indices (with padding), two times, two positions, two rotations.</summary>
    public static byte[] Buffer()
    {
        var data = new List<byte>();
        void Floats(params float[] values)
        {
            foreach (var value in values)
            {
                var bytes = new byte[4];
                BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
                data.AddRange(bytes);
            }
        }

        Floats(0, 0, 0, 1, 0, 0, 0, 1, 0);                  // 0: positions, 36 bytes
        Floats(0, 0, 1, 0, 0, 1, 0, 0, 1);                  // 36: normals
        Floats(0, 0, 1, 0, 0, 1);                           // 72: uvs, 24 bytes
        foreach (var index in new ushort[] { 0, 1, 2, 0 })  // 96: indices (the fourth pads to 4 bytes)
        {
            data.Add((byte)index);
            data.Add((byte)(index >> 8));
        }

        Floats(0, 2);                                       // 104: times
        Floats(0, 0, 0, 4, 0, 0);                           // 112: positions to animate, 24 bytes
        Floats(0, 0, 0, 1, 0, 0.7071068f, 0, 0.7071068f);   // 136: rotations, 32 bytes
        return [.. data];
    }

    public static JsonObject Document(string bufferUri, string? imageUri, int imageView = -1)
    {
        var image = imageUri is null ? new JsonObject { ["bufferView"] = imageView, ["mimeType"] = "image/png" } : new JsonObject { ["uri"] = imageUri };
        var buffer = Buffer();
        var buffers = new JsonArray(new JsonObject { ["byteLength"] = buffer.Length + (imageView >= 0 ? Picture().Length : 0) });
        if (bufferUri.Length > 0)
        {
            buffers[0]!["uri"] = bufferUri;
        }

        var views = new JsonArray(
            View(0, 36), View(36, 36), View(72, 24), View(96, 6), View(104, 8), View(112, 24), View(136, 32));
        if (imageView >= 0)
        {
            views.Add(View(buffer.Length, Picture().Length));
        }

        return new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0) }),
            ["nodes"] = new JsonArray(
                new JsonObject { ["name"] = "base", ["translation"] = new JsonArray(10, 0, 0), ["children"] = new JsonArray(1, 2) },
                new JsonObject { ["name"] = "blade", ["mesh"] = 0, ["matrix"] = new JsonArray(2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2, 0, 0, 5, 0, 1) },
                new JsonObject { ["name"] = "tip", ["rotation"] = new JsonArray(0, 0.7071068, 0, 0.7071068) }),
            ["meshes"] = new JsonArray(new JsonObject
            {
                ["primitives"] = new JsonArray(new JsonObject
                {
                    ["attributes"] = new JsonObject { ["POSITION"] = 0, ["NORMAL"] = 1, ["TEXCOORD_0"] = 2 },
                    ["indices"] = 3,
                    ["material"] = 0,
                }),
            }),
            ["materials"] = new JsonArray(new JsonObject
            {
                ["pbrMetallicRoughness"] = new JsonObject
                {
                    ["baseColorFactor"] = new JsonArray(1, 0.5, 0.25, 0.5),
                    ["baseColorTexture"] = new JsonObject { ["index"] = 0 },
                    ["metallicFactor"] = 0,
                    ["roughnessFactor"] = 0.5,
                },
                ["alphaMode"] = "BLEND",
                ["doubleSided"] = true,
                ["emissiveFactor"] = new JsonArray(0.1, 0.2, 0.3),
            }),
            ["textures"] = new JsonArray(new JsonObject { ["source"] = 0, ["sampler"] = 0 }),
            ["samplers"] = new JsonArray(new JsonObject { ["magFilter"] = 9728, ["wrapS"] = 33071 }),
            ["images"] = new JsonArray(image),
            ["accessors"] = new JsonArray(
                Accessor(0, 5126, 3, "VEC3"),
                Accessor(1, 5126, 3, "VEC3"),
                Accessor(2, 5126, 3, "VEC2"),
                Accessor(3, 5123, 3, "SCALAR"),
                Accessor(4, 5126, 2, "SCALAR"),
                Accessor(5, 5126, 2, "VEC3"),
                Accessor(6, 5126, 2, "VEC4")),
            ["animations"] = new JsonArray(new JsonObject
            {
                ["name"] = "swing",
                ["samplers"] = new JsonArray(
                    new JsonObject { ["input"] = 4, ["output"] = 5 },
                    new JsonObject { ["input"] = 4, ["output"] = 6, ["interpolation"] = "STEP" }),
                ["channels"] = new JsonArray(
                    new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["node"] = 0, ["path"] = "translation" } },
                    new JsonObject { ["sampler"] = 1, ["target"] = new JsonObject { ["node"] = 2, ["path"] = "rotation" } }),
            }),
            ["buffers"] = buffers,
            ["bufferViews"] = views,
        };

        static JsonObject View(int offset, int length) => new() { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = length };

        static JsonObject Accessor(int view, int component, int count, string type) =>
            new() { ["bufferView"] = view, ["componentType"] = component, ["count"] = count, ["type"] = type };
    }

    /// <summary>A .gltf with the buffer and the picture inside as data URIs.</summary>
    public static byte[] Embedded() => Encoding.UTF8.GetBytes(Document(
        "data:application/octet-stream;base64," + Convert.ToBase64String(Buffer()),
        "data:image/png;base64," + Convert.ToBase64String(Picture())).ToJsonString());

    /// <summary>A .glb: the JSON and one binary chunk holding the buffer and the picture.</summary>
    public static byte[] Binary()
    {
        var json = Encoding.UTF8.GetBytes(Document("", null, imageView: 7).ToJsonString());
        var bin = Buffer().Concat(Picture()).ToList();
        while (json.Length % 4 != 0)
        {
            json = [.. json, (byte)' '];
        }

        while (bin.Count % 4 != 0)
        {
            bin.Add(0);
        }

        var file = new List<byte>();
        void Word(uint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            file.AddRange(bytes);
        }

        Word(0x46546C67);
        Word(2);
        Word((uint)(12 + 8 + json.Length + 8 + bin.Count));
        Word((uint)json.Length);
        Word(0x4E4F534A);
        file.AddRange(json);
        Word((uint)bin.Count);
        Word(0x004E4942);
        file.AddRange(bin);
        return [.. file];
    }

    public static byte[] With(Action<JsonObject> change)
    {
        var document = Document(
            "data:application/octet-stream;base64," + Convert.ToBase64String(Buffer()),
            "data:image/png;base64," + Convert.ToBase64String(Picture()));
        change(document);
        return Encoding.UTF8.GetBytes(document.ToJsonString(new JsonSerializerOptions()));
    }
}
