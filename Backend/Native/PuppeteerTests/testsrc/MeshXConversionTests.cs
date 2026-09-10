using nadena.dev.resonity.remote.puppeteer.rpc;
using NUnit.Framework;
using Submesh = nadena.dev.ndmf.proto.mesh.Submesh;

namespace nadena.dev.resonity.remote.puppeteer.tests;

using p = nadena.dev.ndmf.proto;
using pm = nadena.dev.ndmf.proto.mesh;

public class MeshXConversionTests
{

    private pm::Mesh BasicMesh()
    {
        var mesh = new pm::Mesh();
        
        mesh.Positions.Add(new p::Vector() { X = -1, Y = -1, Z = 0 });
        mesh.Positions.Add(new p::Vector() { X = 1, Y = -1, Z = 0 });
        mesh.Positions.Add(new p::Vector() { X = 1, Y = 1, Z = 0 });
        mesh.Positions.Add(new p::Vector() { X = -1, Y = -1, Z = 1 });
        
        mesh.Submeshes.Add(new Submesh()
        {
            Triangles = new pm.TriangleList()
            {
                Triangles =
                {
                    new pm::Triangle() { V0 = 0, V1 = 1, V2 = 2 },
                    new pm::Triangle() { V0 = 2, V1 = 1, V2 = 3 }
                }
            }
        });

        return mesh;
    }
    
    [Test]
    public void WhenDuplicateBlendshapesArePresent_BuildDoesNotFail()
    {
        var mesh = BasicMesh();
        
        mesh.Blendshapes.Add(new pm::Blendshape()
        {
            Name = "A",
            Frames =
            {
                new pm.BlendshapeFrame()
                {
                    Weight = 1,
                    DeltaPositions =
                    {
                        new p::Vector() { X = -1, Y = -1, Z = 0 },
                        new p::Vector() { X = 1, Y = -1, Z = 0 },
                        new p::Vector() { X = 1, Y = 1, Z = 0 },
                        new p::Vector() { X = -1, Y = -1, Z = 1 }
                    }
                }
            }
        });
        mesh.Blendshapes.Add(mesh.Blendshapes[0]);

        MeshTranslator.ToMeshX(mesh);
    }
}