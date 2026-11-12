using System.Numerics;
using System.Text;

namespace Jentl.Scene3D.Tests;

public class SkinTests
{
    /// <summary>
    /// A triangle skinned to two joints: a root at the origin and a tip a unit up. The bottom corner follows the root,
    /// the two top corners the tip.
    /// </summary>
    private static GltfModel Arm()
    {
        using var data = new MemoryStream();
        using (var w = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var f in new float[] { 0, 0, 0, 0, 2, 0, 1, 2, 0 })
            {
                w.Write(f);
            }

            w.Write(new byte[] { 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 });
            foreach (var f in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 })
            {
                w.Write(f);
            }

            // Inverse bind matrices, column-major: the identity, and a move a unit down.
            foreach (var f in new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1 })
            {
                w.Write(f);
            }
        }

        var uri = "data:application/octet-stream;base64," + Convert.ToBase64String(data.ToArray());
        var json = $$"""
            {
              "asset": { "version": "2.0" },
              "buffers": [ { "byteLength": {{data.Length}}, "uri": "{{uri}}" } ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 12 },
                { "buffer": 0, "byteOffset": 48, "byteLength": 48 },
                { "buffer": 0, "byteOffset": 96, "byteLength": 128 }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
                { "bufferView": 1, "componentType": 5121, "count": 3, "type": "VEC4" },
                { "bufferView": 2, "componentType": 5126, "count": 3, "type": "VEC4" },
                { "bufferView": 3, "componentType": 5126, "count": 2, "type": "MAT4" }
              ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0, "JOINTS_0": 1, "WEIGHTS_0": 2 } } ] } ],
              "skins": [ { "joints": [1, 2], "inverseBindMatrices": 3 } ],
              "nodes": [
                { "name": "arm", "mesh": 0, "skin": 0 },
                { "name": "shoulder", "children": [2] },
                { "name": "elbow", "translation": [0, 1, 0] }
              ],
              "scenes": [ { "nodes": [0, 1] } ]
            }
            """;
        return Gltf.Load(Encoding.UTF8.GetBytes(json));
    }

    [Fact]
    public void ASkin_IsRead_AndRestsAsModelled()
    {
        var model = Arm();
        var arm = model.Instantiate();

        model.Warnings.Should().BeEmpty();
        arm.Nodes[0].Skin!.Joints.Should().Equal(arm.Nodes[1], arm.Nodes[2]);
        arm.UpdateSkins();
        arm.Nodes[0].Parts[0].Mesh.Positions[1].Should().Be(new Vector3(0, 2, 0));
    }

    [Fact]
    public void TurningAJoint_BendsTheVerticesItHolds()
    {
        var arm = Arm().Instantiate();

        arm.Nodes[2].Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        arm.UpdateSkins();

        var mesh = arm.Nodes[0].Parts[0].Mesh;
        mesh.Positions[0].Should().Be(Vector3.Zero, "the bottom follows the shoulder, which stayed");
        Vector3.Distance(mesh.Positions[1], new Vector3(-1, 1, 0)).Should().BeLessThan(1e-4f, "the elbow turned the top a quarter round");
        mesh.Original.Should().NotBeNull();

        // Again from the rest shape, not from the last pose.
        arm.UpdateSkins();
        Vector3.Distance(arm.Nodes[0].Parts[0].Mesh.Positions[1], new Vector3(-1, 1, 0)).Should().BeLessThan(1e-4f);
    }
}
