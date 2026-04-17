using System.Collections.Generic;

namespace W3DImporter.Structs.Aggregate
{
    public class W3DAggregateHeader
    {
        public uint Version;
        public string Name = "";
    }

    public class W3DAggregateSubObject
    {
        public string SubObjectName = "";
        public string BoneName = "";
    }

    public class W3DAggregateInfo
    {
        public string BaseModelName = "";
        public List<W3DAggregateSubObject> SubObjects = new List<W3DAggregateSubObject>();
    }

    public class W3DAggregateClassInfo
    {
        public uint OriginalClassId;
        public uint Flags;
    }

    public class W3DAggregate
    {
        public W3DAggregateHeader Header;
        public W3DAggregateInfo Info;
        public W3DAggregateClassInfo ClassInfo;
    }
}
