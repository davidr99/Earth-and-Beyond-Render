using System.Collections.Generic;

namespace W3DImporter.Structs.HLod
{
    public class W3DHLodHeader
    {
        public uint Version;
        public int LodCount;
        public string ModelName = "";
        public string HierarchyName = "";
    }

    public class W3DHLodArrayHeader
    {
        public int ModelCount;
        public float MaxScreenSize;
    }

    public class W3DHLodSubObject
    {
        public int BoneIndex;
        public string Identifier = "";
        public string Name = "";
    }

    public class W3DHLodArray
    {
        public W3DHLodArrayHeader Header;
        public List<W3DHLodSubObject> SubObjects = new List<W3DHLodSubObject>();
    }

    public class W3DHLod
    {
        public W3DHLodHeader Header;
        public List<W3DHLodArray> LodArrays = new List<W3DHLodArray>();
        public W3DHLodArray AggregateArray;
        public W3DHLodArray ProxyArray;
    }
}
