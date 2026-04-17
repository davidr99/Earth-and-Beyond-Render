using System.IO;
using UnityEngine;
using W3DImporter.IO;
using W3DImporter.Structs;
using W3DImporter.Structs.CollisionBox;

namespace W3DImporter.Parsers
{
    public static class W3DFileParser
    {
        public static W3DDataContext Parse(string filePath)
        {
            var context = new W3DDataContext();
            ParseFile(filePath, context);
            return context;
        }

        public static void ParseFile(string filePath, W3DDataContext context)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[W3D] File not found: {filePath}");
                return;
            }

            Debug.Log($"[W3D] Loading: {filePath}");

            using var stream = File.OpenRead(filePath);
            using var reader = new W3DBinaryReader(stream);
            var chunks = new W3DChunkReader(reader);

            long fileSize = stream.Length;

            while (reader.Position < fileSize)
            {
                var chunk = chunks.ReadChunkHeader();

                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.MESH:
                        context.Meshes.Add(W3DMeshParser.Read(reader, chunks, chunk.ChunkEnd));
                        break;

                    case W3DChunkTypes.HIERARCHY:
                        if (context.Hierarchy == null)
                            context.Hierarchy = W3DHierarchyParser.Read(reader, chunks, chunk.ChunkEnd);
                        else
                            Debug.Log("[W3D] Skipping duplicate hierarchy chunk");
                        break;

                    case W3DChunkTypes.HLOD:
                        if (context.HLod == null)
                            context.HLod = W3DHLodParser.Read(reader, chunks, chunk.ChunkEnd);
                        else
                            Debug.Log("[W3D] Skipping duplicate HLOD chunk");
                        break;

                    case W3DChunkTypes.ANIMATION:
                        if (context.Animation == null && context.CompressedAnimation == null)
                            context.Animation = W3DAnimationParser.Read(reader, chunks, chunk.ChunkEnd);
                        break;

                    case W3DChunkTypes.COMPRESSED_ANIMATION:
                        if (context.Animation == null && context.CompressedAnimation == null)
                            context.CompressedAnimation = W3DCompressedAnimationParser.Read(reader, chunks, chunk.ChunkEnd);
                        break;

                    case W3DChunkTypes.BOX:
                        context.CollisionBoxes.Add(ReadCollisionBox(reader));
                        break;

                    case W3DChunkTypes.EMITTER:
                        context.Emitters.Add(W3DEmitterParser.Read(reader, chunks, chunk.ChunkEnd));
                        break;

                    case W3DChunkTypes.AGGREGATE:
                        context.Aggregate = W3DAggregateParser.Read(reader, chunks, chunk.ChunkEnd);
                        break;

                    default:
                        if (chunk.ChunkType != 0)
                            Debug.Log($"[W3D] Skipping chunk: {chunk.ChunkType:X}");
                        break;
                }

                reader.Seek(chunk.ChunkEnd);
            }
        }

        private static W3DCollisionBox ReadCollisionBox(W3DBinaryReader reader)
        {
            var box = new W3DCollisionBox();
            box.Version = reader.ReadUInt32();
            box.Attributes = reader.ReadUInt32();
            box.Name = reader.ReadLongFixedString();
            box.Color = reader.ReadRGBA();
            box.Center = reader.ReadVector3();
            box.Extent = reader.ReadVector3();
            return box;
        }
    }
}
