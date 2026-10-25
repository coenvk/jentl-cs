using System.Numerics;
using Jentl.Geometry;

namespace Jentl.Raster.Tests;

public class ModelBuilderTests
{
    private static readonly Material Red = new(new Vector4(1, 0, 0, 1), Shading.Flat);
    private static readonly Material Glass = new(new Vector4(0.6f, 0.8f, 1, 0.5f), Shading.Flat, Translucent: true);

    [Fact]
    public void Build_JoinsThePartsOfEachMaterial_TranslucentOnesLast()
    {
        var builder = new MeshModelBuilder();
        builder.Add(MeshBuilder.Box(Vector3.One), Matrix4x4.Identity, Glass);
        builder.Add(MeshBuilder.Box(Vector3.One), Matrix4x4.CreateTranslation(2, 0, 0), Red);
        builder.Add(MeshBuilder.Box(Vector3.One), Matrix4x4.CreateTranslation(4, 0, 0), Red);

        var model = builder.Build();

        model.Parts.Should().HaveCount(2);
        model.Parts[0].Material.Should().Be(Red);
        model.Parts[0].Mesh.Triangles.Should().HaveCount(24);
        model.Parts[1].Material.Should().Be(Glass);
        model.Bounds.Should().Be(new Box3(new Vector3(-0.5f), new Vector3(4.5f, 0.5f, 0.5f)));
    }

    [Fact]
    public void DrawModel_DrawsEveryPart()
    {
        var camera = new Camera { Position = new Vector3(2, 0, 8), Target = new Vector3(2, 0, 0) };
        var renderer = new Renderer(new FrameBuffer(64, 32)) { ViewProjection = camera.ViewProjection(2) };
        renderer.Clear(0);
        var builder = new MeshModelBuilder();
        builder.Add(MeshBuilder.Box(Vector3.One), Matrix4x4.Identity, new Material(new Vector4(0, 1, 0, 1), Shading.Unlit));
        builder.Add(MeshBuilder.Box(Vector3.One), Matrix4x4.CreateTranslation(4, 0, 0), new Material(new Vector4(0, 0, 1, 1), Shading.Unlit));

        renderer.DrawModel(builder.Build(), Matrix4x4.Identity);

        renderer.Target.Pixels.Should().Contain(Colors.Pack(new Vector4(0, 1, 0, 1)));
        renderer.Target.Pixels.Should().Contain(Colors.Pack(new Vector4(0, 0, 1, 1)));
    }
}
