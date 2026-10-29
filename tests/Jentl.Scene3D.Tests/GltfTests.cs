using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Jentl.Raster;

namespace Jentl.Scene3D.Tests;

public class GltfTests
{
    private const float Close = 1e-4f;

    public static TheoryData<string> Files => ["gltf", "glb"];

    private static GltfModel Load(string kind) => Gltf.Load(kind == "glb" ? GltfFixture.Binary() : GltfFixture.Embedded());

    [Theory]
    [MemberData(nameof(Files))]
    public void TheNodeTree_IsRead_WithMatricesSplitIntoTheirParts(string kind)
    {
        var model = Load(kind).Instantiate("windmill");

        model.Root.Name.Should().Be("windmill");
        model.Root.Children.Select(n => n.Name).Should().Equal("base");
        model.Nodes.Select(n => n.Name).Should().Equal("base", "blade", "tip");
        model.Nodes[0].Children.Should().Equal(model.Nodes[1], model.Nodes[2]);
        var blade = model.Nodes[1];
        blade.Scale.Should().Be(new Vector3(2));
        blade.WorldPosition.Should().Be(new Vector3(10, 5, 0));
        Vector3.Distance(model.Nodes[2].Forward, Vector3.UnitX).Should().BeLessThan(Close);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Meshes_AreRead_WithNormalsAndTextureCoordinates(string kind)
    {
        var part = Load(kind).Instantiate().Nodes[1].Parts.Should().ContainSingle().Subject;

        part.Mesh.Positions.Should().Equal(Vector3.Zero, Vector3.UnitX, Vector3.UnitY);
        part.Mesh.Normals.Should().AllBeEquivalentTo(Vector3.UnitZ);
        part.Mesh.UVs.Should().Equal(Vector2.Zero, Vector2.UnitX, Vector2.UnitY);
        part.Mesh.Triangles.Should().Equal(new Geometry.TriangleIndices(0, 1, 2));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Materials_TakeColourTextureTransparencySidesGlowAndShine(string kind)
    {
        var material = Load(kind).Materials.Should().ContainSingle().Subject;

        material.Color.Should().Be(new Vector4(1, 0.5f, 0.25f, 0.5f));
        material.Translucent.Should().BeTrue();
        material.DoubleSided.Should().BeTrue();
        Vector3.Distance(material.Emissive, new Vector3(0.1f, 0.2f, 0.3f)).Should().BeLessThan(Close);
        Vector3.Distance(material.Specular, new Vector3(0.2f)).Should().BeLessThan(Close);
        material.Shininess.Should().BeApproximately(35, Close);
        var texture = material.Texture!;
        (texture.Width, texture.Height, texture.Filter, texture.Wrap).Should().Be((2, 2, TextureFilter.Nearest, TextureWrap.Clamp));
        texture.Pixels.Should().Equal(0xFF0000FF, 0xFF00FF00, 0xFFFF0000, 0x00FFFFFF);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Animations_MoveTheNodesTheyName(string kind)
    {
        var model = Load(kind);
        var copy = model.Instantiate();
        var clip = model.Animation("swing")!;

        clip.Duration.Should().Be(2);
        clip.Apply(copy.Nodes, 1);

        copy.Nodes[0].Position.Should().Be(new Vector3(2, 0, 0));
        copy.Nodes[2].Rotation.Should().Be(Quaternion.Identity, "a step channel holds its key");
        clip.Apply(copy.Nodes, 2);
        Vector3.Distance(copy.Nodes[2].Forward, Vector3.UnitX).Should().BeLessThan(Close);
    }

    [Fact]
    public void Copies_ShareMeshes_ButMoveApart()
    {
        var model = Load("gltf");
        var (a, b) = (model.Instantiate(), model.Instantiate());

        a.Nodes[0].Position = new Vector3(-50, 0, 0);

        b.Nodes[0].Position.Should().Be(new Vector3(10, 0, 0));
        a.Nodes[1].Parts[0].Mesh.Should().BeSameAs(b.Nodes[1].Parts[0].Mesh);
    }

    [Fact]
    public void FilesBesideTheModel_AreAskedFor()
    {
        var asked = new List<string>();
        var file = Encoding.UTF8.GetBytes(GltfFixture.Document("parts/mill.bin", "parts/mill%20texture.png").ToJsonString());

        var model = Gltf.Load(file, uri =>
        {
            asked.Add(uri);
            return uri.EndsWith(".png", StringComparison.Ordinal) ? GltfFixture.Picture() : GltfFixture.Buffer();
        });

        asked.Should().BeEquivalentTo("parts/mill.bin", "parts/mill%20texture.png");
        model.Materials[0].Texture.Should().NotBeNull();
    }

    [Fact]
    public void WhatIsLeftOut_IsListed()
    {
        var file = GltfFixture.With(document =>
        {
            document["images"]![0]!["uri"] = "data:image/webp;base64," + Convert.ToBase64String("RIFF0000WEBP"u8.ToArray());
            document["nodes"]![1]!["skin"] = 0;
            ((JsonObject)document["meshes"]![0]!["primitives"]![0]!["attributes"]!).Remove("NORMAL");
        });

        var model = Gltf.Load(file);

        model.Warnings.Should().HaveCount(2);
        model.Warnings.Should().Contain(w => w.Contains("neither PNG nor JPEG", StringComparison.Ordinal))
            .And.Contain(w => w.Contains("skinned", StringComparison.Ordinal));
        model.Materials[0].Texture.Should().BeNull();
        model.Instantiate().Nodes[1].Parts[0].Material.Shading.Should().Be(Shading.Flat, "glTF draws meshes without normals flat");
    }

    [Fact]
    public void Strips_BecomeTriangles()
    {
        var file = GltfFixture.With(document =>
        {
            var primitive = document["meshes"]![0]!["primitives"]![0]!;
            primitive["mode"] = 5;
            ((JsonObject)primitive).Remove("indices");
        });

        Gltf.Load(file).Instantiate().Nodes[1].Parts[0].Mesh.Triangles.Should().Equal(new Geometry.TriangleIndices(0, 1, 2));
    }

    [Theory]
    [InlineData("version", "Only glTF 2.0 can be read, not version '1.0'.")]
    [InlineData("sparse", "Accessor 0 is sparse, which isn't supported.")]
    [InlineData("short", "Accessor 0 reaches past the end of its buffer.")]
    [InlineData("external", "The model refers to the file 'mill.bin'; load it with a way to find files.")]
    public void BrokenFiles_SayWhatIsWrong(string fault, string message)
    {
        var file = GltfFixture.With(document =>
        {
            switch (fault)
            {
                case "version":
                    document["asset"]!["version"] = "1.0";
                    break;
                case "sparse":
                    document["accessors"]![0]!["sparse"] = new JsonObject();
                    break;
                case "short":
                    document["accessors"]![0]!["count"] = 100;
                    break;
                default:
                    document["buffers"]![0]!["uri"] = "mill.bin";
                    break;
            }
        });

        var act = () => Gltf.Load(file);

        act.Should().Throw<InvalidDataException>().WithMessage(message);
    }
}
