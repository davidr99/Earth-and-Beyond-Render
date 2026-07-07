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

            SynthesizeLegacyRoot(hierarchy);
            return hierarchy;
        }

        // Hierarchies with Version < 0x30000 get a synthetic "RootTransform" pivot 0
        // prepended by the client; all parent indices shift +1, so the original root's
        // ParentIdx of -1 becomes 0 (W3D_FORMAT.md §9). HLOD/animation pivot indices in
        // such files assume the shifted table.
        private static void SynthesizeLegacyRoot(W3DHierarchy hierarchy)
        {
            if (hierarchy.Header == null || hierarchy.Header.Version >= 0x30000 || hierarchy.Pivots.Count == 0)
                return;

            foreach (var pivot in hierarchy.Pivots)
                pivot.ParentId += 1;

            hierarchy.Pivots.Insert(0, new W3DHierarchyPivot
            {
                Name = "RootTransform",
                ParentId = -1,
                Translation = Vector3.zero,
                EulerAngles = Vector3.zero,
                Rotation = Quaternion.identity
            });

            hierarchy.Header.PivotCount = hierarchy.Pivots.Count;
        }
    }
}

