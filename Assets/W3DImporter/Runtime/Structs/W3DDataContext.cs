using System.Collections.Generic;
using W3DImporter.Structs.Mesh;
using W3DImporter.Structs.Hierarchy;
using W3DImporter.Structs.HLod;
using W3DImporter.Structs.Animation;
using W3DImporter.Structs.Emitter;
using W3DImporter.Structs.Aggregate;
using W3DImporter.Structs.CollisionBox;

namespace W3DImporter.Structs
{
    public class W3DDataContext
    {
        public List<W3DMesh> Meshes = new List<W3DMesh>();
        public W3DHierarchy Hierarchy;
        public W3DHLod HLod;
        public W3DAnimation Animation;
        public W3DCompressedAnimation CompressedAnimation;
        public List<W3DCollisionBox> CollisionBoxes = new List<W3DCollisionBox>();
        public List<W3DEmitter> Emitters = new List<W3DEmitter>();
        public W3DAggregate Aggregate;
        public List<W3DDazzle> Dazzles = new List<W3DDazzle>();
    }

    public class W3DDazzle
    {
        public string Name = "";
        public string TypeName = "";
    }
}
