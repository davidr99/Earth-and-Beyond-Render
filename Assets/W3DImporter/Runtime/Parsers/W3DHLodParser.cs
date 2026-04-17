using W3DImporter.IO;
using W3DImporter.Structs.HLod;

namespace W3DImporter.Parsers
{
    public static class W3DHLodParser
    {
        public static W3DHLod Read(W3DBinaryReader reader, W3DChunkReader chunks, long chunkEnd)
        {
            var hlod = new W3DHLod();

            foreach (var chunk in chunks.EnumerateChunks(chunkEnd))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.HLOD_HEADER:
                        hlod.Header = new W3DHLodHeader
                        {
                            Version = reader.ReadUInt32(),
                            LodCount = reader.ReadInt32(),
                            ModelName = reader.ReadFixedString(),
                            HierarchyName = reader.ReadFixedString()
                        };
                        break;
                    case W3DChunkTypes.HLOD_LOD_ARRAY:
                        hlod.LodArrays.Add(ReadLodArray(reader, chunks, chunk.ChunkEnd));
                        break;
                    case W3DChunkTypes.HLOD_AGGREGATE_ARRAY:
                        hlod.AggregateArray = ReadLodArray(reader, chunks, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.HLOD_PROXY_ARRAY:
                        hlod.ProxyArray = ReadLodArray(reader, chunks, chunk.ChunkEnd);
                        break;
                    default:
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return hlod;
        }

        private static W3DHLodArray ReadLodArray(W3DBinaryReader reader, W3DChunkReader chunks, long end)
        {
            var array = new W3DHLodArray();

            foreach (var chunk in chunks.EnumerateChunks(end))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.HLOD_SUB_OBJECT_ARRAY_HEADER:
                        array.Header = new W3DHLodArrayHeader
                        {
                            ModelCount = reader.ReadInt32(),
                            MaxScreenSize = reader.ReadFloat()
                        };
                        break;
                    case W3DChunkTypes.HLOD_SUB_OBJECT:
                        var sub = new W3DHLodSubObject
                        {
                            BoneIndex = reader.ReadInt32(),
                            Identifier = reader.ReadLongFixedString()
                        };
                        int dotIdx = sub.Identifier.IndexOf('.');
                        sub.Name = dotIdx >= 0 ? sub.Identifier.Substring(dotIdx + 1) : sub.Identifier;
                        array.SubObjects.Add(sub);
                        break;
                    default:
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return array;
        }
    }
}
