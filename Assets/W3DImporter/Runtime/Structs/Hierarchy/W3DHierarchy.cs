using System.Collections.Generic;
using UnityEngine;

namespace W3DImporter.Structs.Hierarchy
{
    public class W3DHierarchyHeader
    {
        public uint Version;
        public string Name = "";
        public int PivotCount;
        public Vector3 Center;
    }

    public class W3DHierarchyPivot
    {
        public string Name = "";
        public int ParentId = -1;
        public Vector3 Translation;
        public Vector3 EulerAngles;
        public Quaternion Rotation = Quaternion.identity;
    }

    public class W3DHierarchy
    {
        public W3DHierarchyHeader Header;
        public List<W3DHierarchyPivot> Pivots = new List<W3DHierarchyPivot>();

        public int FindBoneIndex(string boneName)
        {
            for (int i = 0; i < Pivots.Count; i++)
            {
                if (string.Equals(Pivots[i].Name, boneName, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }
    }
}
