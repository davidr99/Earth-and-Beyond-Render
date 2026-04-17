using UnityEngine;

namespace W3DImporter.Structs.CollisionBox
{
    public class W3DCollisionBox
    {
        public uint Version;
        public uint Attributes;
        public string Name = "";
        public Color32 Color;
        public Vector3 Center;
        public Vector3 Extent;
    }
}
