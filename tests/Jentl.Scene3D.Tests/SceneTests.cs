using System.Numerics;
using Jentl.Geometry;
using Jentl.Raster;

namespace Jentl.Scene3D.Tests;

public class SceneTests
{
    private const float Close = 1e-4f;

    private static readonly Mesh Cube = MeshBuilder.Box(Vector3.One);
    private static readonly Material Solid = new(Vector4.One);
    private static readonly Material Glass = new(new Vector4(1, 1, 1, 0.5f), Translucent: true);

    private static Node At(Node parent, string name, Vector3 position, Material? material = null)
    {
        var node = parent.Add(name, Cube, material ?? Solid);
        node.Position = position;
        return node;
    }

    private static Camera LookingDownZ() => new() { Position = new Vector3(0, 0, 10), Target = Vector3.Zero };

    [Fact]
    public void Children_MoveTurnAndScaleWithTheirParents()
    {
        var car = new Node("car") { Position = new Vector3(10, 0, 0), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), Scale = new Vector3(2) };
        var wheel = car.Add(new Node("wheel") { Position = new Vector3(1, 0, 0) });

        // Turned a quarter about Y, +X becomes -Z; doubled, the wheel is 2 away.
        Vector3.Distance(wheel.WorldPosition, new Vector3(10, 0, -2)).Should().BeLessThan(Close);
        Vector3.Distance(wheel.Forward, Vector3.UnitX).Should().BeLessThan(Close);
    }

    [Fact]
    public void Nodes_MoveBetweenParents_ButNeverIntoThemselves()
    {
        var (a, b) = (new Node("a"), new Node("b"));
        var child = a.Add(new Node("child"));

        b.Add(child);

        a.Children.Should().BeEmpty();
        child.Parent.Should().Be(b);
        var act = () => child.Add(b);
        act.Should().Throw<InvalidOperationException>();
        child.Detach();
        child.Parent.Should().BeNull();
    }

    [Fact]
    public void Find_SelfAndDescendants_AndClone()
    {
        var root = new Node("root");
        var arm = At(root, "arm", Vector3.UnitX);
        At(arm, "hand", Vector3.UnitX);

        root.SelfAndDescendants().Select(n => n.Name).Should().Equal("root", "arm", "hand");
        root.Find("hand")!.WorldPosition.Should().Be(new Vector3(2, 0, 0));
        var copy = root.Clone();
        copy.Find("arm")!.Position = Vector3.Zero;
        arm.Position.Should().Be(Vector3.UnitX);
        copy.Find("hand")!.Parts[0].Mesh.Should().BeSameAs(Cube);
    }

    [Fact]
    public void Local_SplitsIntoPositionRotationAndScale()
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f);
        var node = new Node { Local = Matrix4x4.CreateScale(3) * Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation(1, 2, 3) };

        node.Position.Should().Be(new Vector3(1, 2, 3));
        Vector3.Distance(node.Scale, new Vector3(3)).Should().BeLessThan(Close);
        MathF.Abs(Quaternion.Dot(node.Rotation, turn)).Should().BeApproximately(1, Close);
    }

    [Fact]
    public void LookAt_FacesThePoint_EvenUnderATurnedParent()
    {
        var parent = new Node { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1), Position = new Vector3(5, 0, 0) };
        var turret = parent.Add(new Node("turret"));

        turret.LookAt(new Vector3(5, 0, 20));

        Vector3.Distance(turret.Forward, Vector3.UnitZ).Should().BeLessThan(Close);
        turret.LookAt(new Vector3(5, 10, 0));
        Vector3.Distance(turret.Forward, Vector3.UnitY).Should().BeLessThan(1e-3f);
    }

    [Fact]
    public void WorldBounds_CoverEveryVisibleMesh()
    {
        var root = new Node();
        At(root, "a", new Vector3(-5, 0, 0));
        At(root, "b", new Vector3(5, 0, 0)).Scale = new Vector3(2);
        At(root, "hidden", new Vector3(50, 0, 0)).Visible = false;

        root.WorldBounds().Should().Be(new Box3(new Vector3(-5.5f, -1, -1), new Vector3(6, 1, 1)));
        new Node().WorldBounds().Should().BeNull();
    }

    [Fact]
    public void TheRenderList_LeavesOutWhatIsOutOfView_AndDrawsSolidFirstThenGlassFurthestFirst()
    {
        var root = new Node();
        At(root, "far glass", new Vector3(0, 0, -5), Glass);
        At(root, "near glass", new Vector3(0, 0, 5), Glass);
        At(root, "far solid", new Vector3(0, 0, -5));
        At(root, "near solid", new Vector3(0, 0, 5));
        At(root, "behind", new Vector3(0, 0, 20));
        At(root, "hidden", Vector3.Zero).Visible = false;
        var camera = LookingDownZ();

        var list = new RenderList().Build(root, camera.ViewProjection(1), camera.Position);

        list.Items.Select(i => i.Node.Name).Should().Equal("near solid", "far solid", "far glass", "near glass");
        list.Culled.Should().Be(1);
        list.Drawn.Should().Be(4);
    }

    [Fact]
    public void TheRenderList_KeepsMaterialsTogether()
    {
        var root = new Node();
        var red = new Material(new Vector4(1, 0, 0, 1));
        At(root, "red far", new Vector3(0, 0, -5), red);
        At(root, "white", Vector3.Zero);
        At(root, "red near", new Vector3(0, 0, 5), red);
        var camera = LookingDownZ();

        var list = new RenderList().Build(root, camera.ViewProjection(1), camera.Position);

        list.Items.Select(i => i.Node.Name).Should().Equal("red near", "red far", "white");
    }

    [Fact]
    public void ARayThroughTheMiddle_GoesWhereTheCameraLooks()
    {
        var camera = LookingDownZ();

        var ray = camera.RayThrough(new Vector2(320, 240), new Vector2(640, 480));

        Vector3.Distance(ray.Direction, -Vector3.UnitZ).Should().BeLessThan(Close);
        Vector3.Distance(ray.Origin, new Vector3(0, 0, 9.9f)).Should().BeLessThan(1e-3f);
    }

    [Fact]
    public void Picking_FindsTheNearestMeshUnderTheRay()
    {
        var root = new Node();
        At(root, "back", new Vector3(0, 0, -5));
        var front = At(root, "front", Vector3.Zero);
        front.Scale = new Vector3(4);
        front.Turn(Vector3.UnitY, 0.3f);
        At(root, "aside", new Vector3(10, 0, 0));
        var ray = new Ray(new Vector3(0, 0, 10), -Vector3.UnitZ);

        var hit = root.Pick(ray);

        hit!.Value.Node.Should().Be(front);
        hit.Value.Point.Z.Should().BeApproximately(2 / MathF.Cos(0.3f), 1e-3f);
        root.Pick(ray, node => node.Name != "front")!.Value.Node.Name.Should().Be("back");
        root.Pick(new Ray(new Vector3(0, 10, 10), -Vector3.UnitZ)).Should().BeNull();
    }

    [Fact]
    public void AnOrbitCamera_CirclesItsTarget_WithinItsLimits()
    {
        var orbit = new OrbitCamera { Target = new Vector3(1, 0, 0), Pitch = 0, Distance = 5 };

        orbit.Position.Should().Be(new Vector3(1, 0, 5));
        orbit.Rotate(MathF.PI / 2, 10);
        orbit.Pitch.Should().Be(orbit.MaxPitch);
        orbit.Zoom(0.0001f);
        orbit.Distance.Should().Be(orbit.MinDistance);

        var camera = new Camera();
        orbit.Frame(new Box3(new Vector3(-1), new Vector3(1)));
        orbit.Apply(camera);
        camera.Target.Should().Be(Vector3.Zero);
        Vector3.Distance(camera.Position, Vector3.Zero).Should().BeApproximately(MathF.Sqrt(3) * 2, 1e-3f);
    }

    [Fact]
    public void AFollowCamera_JumpsIntoPlaceFirst_ThenCatchesUp()
    {
        var car = new Node { Position = new Vector3(0, 0, 0) };
        var follow = new FollowCamera(car) { Offset = new Vector3(0, 2, -5), LookAt = Vector3.Zero, Stiffness = 5 };
        var camera = new Camera();

        follow.Update(camera, TimeSpan.FromSeconds(0.1));
        camera.Position.Should().Be(new Vector3(0, 2, -5));

        car.Position = new Vector3(0, 0, 10);
        follow.Update(camera, TimeSpan.FromSeconds(0.1));
        camera.Position.Z.Should().BeInRange(-5, 5).And.BeApproximately(-5 + (10 * (1 - MathF.Exp(-0.5f))), 1e-3f);
    }
}
