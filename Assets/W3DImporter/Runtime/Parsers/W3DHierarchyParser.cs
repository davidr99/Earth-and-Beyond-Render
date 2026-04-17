using UnityEngine;
using W3DImporter.IO;
using W3DImporter.Structs.Hierarchy;

namespace W3DImporter.Parsers
{
    public static class W3DHierarchyParser
    {
        public static W3DHierarchy Read(W3DBinaryReader reader, W3DChunkReader chunks, long chunkEnd)
        {
            var hierarchy = new W3DHierarchy();

            foreach (var chunk in chunks.EnumerateChunks(chunkEnd))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.HIERARCHY_HEADER:
                        hierarchy.Header = new W3DHierarchyHeader
                        {
                            Version = reader.ReadUInt32(),
                            Name = reader.ReadFixedString(),
                            PivotCount = reader.ReadInt32(),
                            Center = reader.ReadVector3()
                        };
                        break;
                    case W3DChunkTypes.PIVOTS:
                        while (reader.Position < chunk.ChunkEnd)
                        {
                            var pivot = new W3DHierarchyPivot
                            {
                                Name = reader.ReadFixedString(),
                                ParentId = reader.ReadInt32(),
                                Translation = reader.ReadVector3(),
                                EulerAngles = reader.ReadVector3(),
                                Rotation = reader.ReadQuaternion()
                            };
                            hierarchy.Pivots.Add(pivot);
                        }
                        break;
                    case W3DChunkTypes.PIVOT_FIXUPS:
                        break;
                    default:
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return hierarchy;
        }
    }
}

