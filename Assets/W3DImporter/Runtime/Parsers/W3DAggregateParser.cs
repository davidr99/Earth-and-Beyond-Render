using W3DImporter.IO;
using W3DImporter.Structs.Aggregate;

namespace W3DImporter.Parsers
{
    public static class W3DAggregateParser
    {
        public static W3DAggregate Read(W3DBinaryReader reader, W3DChunkReader chunks, long chunkEnd)
        {
            var aggregate = new W3DAggregate();

            foreach (var chunk in chunks.EnumerateChunks(chunkEnd))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.AGGREGATE_HEADER:
                        aggregate.Header = new W3DAggregateHeader
                        {
                            Version = reader.ReadUInt32(),
                            Name = reader.ReadFixedString()
                        };
                        break;
                    case W3DChunkTypes.AGGREGATE_INFO:
                        aggregate.Info = ReadAggregateInfo(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.AGGREGATE_CLASS_INFO:
                        aggregate.ClassInfo = new W3DAggregateClassInfo
                        {
                            OriginalClassId = reader.ReadUInt32(),
                            Flags = reader.ReadUInt32()
                        };
                        reader.Skip(12); // reserved
                        break;
                    default:
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return aggregate;
        }

        private static W3DAggregateInfo ReadAggregateInfo(W3DBinaryReader reader, long chunkEnd)
        {
            var info = new W3DAggregateInfo();
            info.BaseModelName = reader.ReadLongFixedString();
            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                var sub = new W3DAggregateSubObject
                {
                    SubObjectName = reader.ReadLongFixedString(),
                    BoneName = reader.ReadLongFixedString()
                };
                info.SubObjects.Add(sub);
            }

            return info;
        }
    }
}
