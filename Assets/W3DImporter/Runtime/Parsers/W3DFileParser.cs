using System.IO;
using System.Collections.Generic;
using UnityEngine;
using W3DImporter.IO;
using W3DImporter.Structs;
using W3DImporter.Structs.CollisionBox;
using W3DImporter.Structs.Animation;
using W3DImporter.Structs.HLod;
using W3DImporter.Structs.Mesh;

namespace W3DImporter.Parsers
{
    public static class W3DFileParser
    {
        private const bool EnableParserLogging = false;
        private static readonly Dictionary<string, W3DDataContext> ParseCache = new Dictionary<string, W3DDataContext>(System.StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, (System.DateTime, long)> ParseCacheStamps = new Dictionary<string, (System.DateTime, long)>(System.StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AnimationScanResult> AnimationScanCache = new Dictionary<string, AnimationScanResult>(System.StringComparer.OrdinalIgnoreCase);

        private sealed class AnimationScanResult
        {
            public W3DAnimation[] Animations = System.Array.Empty<W3DAnimation>();
            public W3DCompressedAnimation[] CompressedAnimations = System.Array.Empty<W3DCompressedAnimation>();
        }

        public static W3DDataContext Parse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return new W3DDataContext();

            string normalizedPath = Path.GetFullPath(filePath);
            var stamp = GetFileStamp(normalizedPath);
            // re-parse when the file changed on disk (editor reimports)
            if (ParseCache.TryGetValue(normalizedPath, out var cached) &&
                ParseCacheStamps.TryGetValue(normalizedPath, out var cachedStamp) && cachedStamp == stamp)
                return cached;

            var context = new W3DDataContext();
            ParseFile(normalizedPath, context);
            ParseCache[normalizedPath] = context;
            ParseCacheStamps[normalizedPath] = stamp;
            return context;
        }

        private static (System.DateTime, long) GetFileStamp(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.LastWriteTimeUtc, info.Length) : (System.DateTime.MinValue, -1);
        }

        public static void ParseFile(string filePath, W3DDataContext context)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[W3D] File not found: {filePath}");
                return;
            }

            LogParser($"[W3D] Loading: {filePath}");
            if (string.IsNullOrEmpty(context.SourcePath))
                context.SourcePath = Path.GetFullPath(filePath);

            // one sequential read: the parser seeks between chunks, which is slow on a busy disk
            using var stream = new MemoryStream(File.ReadAllBytes(filePath), false);
            using var reader = new W3DBinaryReader(stream);
            var chunks = new W3DChunkReader(reader);

            long fileSize = stream.Length;
            W3DHLod hmodelLod = null;

            foreach (var chunk in chunks.EnumerateChunks(fileSize))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.MESH:
                    {
                        var mesh = W3DMeshParser.Read(reader, chunks, chunk.ChunkEnd);
                        // Meshes without a MESH_HEADER3 (W3D 1.x files) cannot be loaded by the client either.
                        if (mesh.Header != null)
                            context.Meshes.Add(mesh);
                        else
                            Debug.LogWarning($"[W3D] Skipping a mesh without header (W3D 1.x format) in {Path.GetFileName(filePath)}");
                        break;
                    }

                    case W3DChunkTypes.HMODEL:
                        if (hmodelLod == null)
                            hmodelLod = ReadHModel(reader, chunks, chunk.ChunkEnd);
                        break;

                    case W3DChunkTypes.HIERARCHY:
                        if (context.Hierarchy == null)
                            context.Hierarchy = W3DHierarchyParser.Read(reader, chunks, chunk.ChunkEnd);
                        else
                            LogParser("[W3D] Skipping duplicate hierarchy chunk");
                        break;

                    case W3DChunkTypes.HLOD:
                        if (context.HLod == null)
                            context.HLod = W3DHLodParser.Read(reader, chunks, chunk.ChunkEnd);
                        else
                            LogParser("[W3D] Skipping duplicate HLOD chunk");
                        break;

                    case W3DChunkTypes.ANIMATION:
                    {
                        var animation = W3DAnimationParser.Read(reader, chunks, chunk.ChunkEnd);
                        if (animation != null)
                        {
                            context.Animations.Add(animation);
                            if (context.Animation == null && context.CompressedAnimation == null)
                                context.Animation = animation;
                        }
                        break;
                    }

                    case W3DChunkTypes.COMPRESSED_ANIMATION:
                    {
                        var compressedAnimation = W3DCompressedAnimationParser.Read(reader, chunks, chunk.ChunkEnd);
                        if (compressedAnimation != null)
                        {
                            context.CompressedAnimations.Add(compressedAnimation);
                            if (context.Animation == null && context.CompressedAnimation == null)
                                context.CompressedAnimation = compressedAnimation;
                        }
                        break;
                    }

                    case W3DChunkTypes.BOX:
                        context.CollisionBoxes.Add(ReadCollisionBox(reader));
                        break;

                    case W3DChunkTypes.EMITTER:
                        context.Emitters.Add(W3DEmitterParser.Read(reader, chunks, chunk.ChunkEnd));
                        break;

                    case W3DChunkTypes.AGGREGATE:
                        context.Aggregate = W3DAggregateParser.Read(reader, chunks, chunk.ChunkEnd);
                        break;

                    case W3DChunkTypes.LODMODEL:
                        context.LodModel = ReadLodModel(reader, chunks, chunk.ChunkEnd);
                        break;

                    case W3DChunkTypes.SHDMESH:
                        context.Meshes.AddRange(ReadShdMesh(reader, chunks, chunk.ChunkEnd));
                        break;

                    case W3DChunkTypes.DAZZLE:
                        context.Dazzles.Add(ReadDazzle(reader, chunks, chunk.ChunkEnd));
                        break;

                    // Recognized but not implemented. Logging "skipping" rather
                    // than silently dropping helps diagnose missing-feature
                    // reports — without this you get the same observable result
                    // for "chunk doesn't exist in the file" and "chunk exists
                    // but the importer can't render it."
                    case W3DChunkTypes.SPHERE:
                    case W3DChunkTypes.RING:
                    case W3DChunkTypes.COLLECTION:
                    case W3DChunkTypes.POINTS:
                    case W3DChunkTypes.LIGHT:
                    case W3DChunkTypes.NULL_OBJECT:
                    case W3DChunkTypes.LIGHTSCAPE:
                    case W3DChunkTypes.SOUNDROBJ:
                    case W3DChunkTypes.MORPH_ANIMATION:
                        Debug.Log($"[W3D] Recognized but unimplemented top-level chunk 0x{chunk.ChunkType:X3} in {Path.GetFileName(filePath)}");
                        break;

                    default:
                        if (chunk.ChunkType != 0)
                            LogParser($"[W3D] Skipping chunk: {chunk.ChunkType:X}");
                        break;
                }

                reader.Seek(chunk.ChunkEnd);
            }

            // HModels attach render objects to pivots exactly like a single-LOD HLOD
            if (context.HLod == null && hmodelLod != null)
                context.HLod = hmodelLod;
        }

        // HMODEL (0x300): header {u32 version; char name[16]; char hierarchy[16]; u16 count; u16 pad}
        // followed by 18-byte node chunks {char renderObjName[16]; u16 pivotIndex}.
        private static W3DHLod ReadHModel(W3DBinaryReader reader, W3DChunkReader chunks, long chunkEnd)
        {
            var hlod = new W3DHLod { Header = new W3DHLodHeader { LodCount = 1 } };
            var lodArray = new W3DHLodArray { Header = new W3DHLodArrayHeader { MaxScreenSize = float.MaxValue } };
            hlod.LodArrays.Add(lodArray);

            foreach (var chunk in chunks.EnumerateChunks(chunkEnd))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.HMODEL_HEADER:
                        hlod.Header.Version = reader.ReadUInt32();
                        hlod.Header.ModelName = reader.ReadFixedString();
                        hlod.Header.HierarchyName = reader.ReadFixedString();
                        break;
                    case W3DChunkTypes.HMODEL_NODE:
                    case W3DChunkTypes.HMODEL_COLLISION_NODE:
                    case W3DChunkTypes.HMODEL_SKIN_NODE:
                    case W3DChunkTypes.HMODEL_SHADOW_NODE:
                    {
                        string name = reader.ReadFixedString();
                        int pivot = reader.ReadUInt16();
                        lodArray.SubObjects.Add(new W3DHLodSubObject
                        {
                            BoneIndex = pivot,
                            Name = name,
                            Identifier = name
                        });
                        break;
                    }
                }

                reader.Seek(chunk.ChunkEnd);
            }

            foreach (var sub in lodArray.SubObjects)
            {
                sub.Identifier = hlod.Header.ModelName + "." + sub.Name;
                // pre-3.0 models index pivots without the implicit root pivot the client inserts
                if (hlod.Header.Version < 0x30000)
                    sub.BoneIndex += 1;
            }

            lodArray.Header.ModelCount = lodArray.SubObjects.Count;
            return hlod;
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

        private static W3DLODModel ReadLodModel(W3DBinaryReader reader, W3DChunkReader chunks, long chunkEnd)
        {
            var lodModel = new W3DLODModel();

            foreach (var chunk in chunks.EnumerateChunks(chunkEnd))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.LODMODEL_HEADER:
                        lodModel.Header = new W3DLODModelHeader
                        {
                            Version = reader.ReadUInt32(),
                            Name = reader.ReadFixedString(),
                            LodCount = (int)(reader.ReadUInt32() & 0xFFFFu)
                        };
                        break;
                    case W3DChunkTypes.LODMODEL_LOD:
                        lodModel.Levels.Add(new W3DLODModelLevel
                        {
                            RenderObjName = reader.ReadLongFixedString(),
                            LodMin = reader.ReadFloat(),
                            LodMax = reader.ReadFloat()
                        });
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }

                reader.Seek(chunk.ChunkEnd);
            }

            return lodModel;
        }

        private static List<W3DMesh> ReadShdMesh(W3DBinaryReader reader, W3DChunkReader chunks, long chunkEnd)
        {
            string meshName = "";
            int expectedSubMeshes = 0;
            var meshes = new List<W3DMesh>();

            foreach (var chunk in chunks.EnumerateChunks(chunkEnd))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.SHDMESH_NAME:
                        meshName = reader.ReadString((int)(chunk.ChunkEnd - reader.Position));
                        break;
                    case W3DChunkTypes.SHDMESH_HEADER:
                        reader.Skip(16);
                        expectedSubMeshes = reader.ReadInt32();
                        break;
                    case W3DChunkTypes.SHDMESH_SUBMESH:
                        meshes.Add(ReadShdSubMesh(reader, chunks, chunk.ChunkEnd, meshName, meshes.Count));
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }

                reader.Seek(chunk.ChunkEnd);
            }

            if (expectedSubMeshes > 0 && meshes.Count != expectedSubMeshes)
                Debug.LogWarning($"[W3D] ShdMesh '{meshName}' expected {expectedSubMeshes} submesh(es), parsed {meshes.Count}");

            return meshes;
        }

        private static W3DMesh ReadShdSubMesh(W3DBinaryReader reader, W3DChunkReader chunks, long end, string meshName, int index)
        {
            string name = string.IsNullOrWhiteSpace(meshName) ? $"ShdMesh_{index}" : $"{meshName}_{index}";
            var mesh = new W3DMesh
            {
                Header = new W3DMeshHeader
                {
                    MeshName = name,
                    ContainerName = meshName ?? "",
                    Attrs = 0
                }
            };

            foreach (var chunk in chunks.EnumerateChunks(end))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.SHDMESH_SUBMESH_HEADER:
                        mesh.Header.FaceCount = reader.ReadInt32();
                        mesh.Header.VertCount = reader.ReadInt32();
                        chunks.SkipChunk(chunk);
                        break;
                    case W3DChunkTypes.SHDMESH_VERTICES:
                        mesh.Verts = ReadVectorList(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.SHDMESH_VERTEX_NORMALS:
                        mesh.Normals = ReadVectorList(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.SHDMESH_TRIANGLES:
                        mesh.Triangles = ReadUInt16TriangleList(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.SHDMESH_VERTEX_SHADE_INDICES:
                        chunks.SkipChunk(chunk);
                        break;
                    case W3DChunkTypes.SHDMESH_UV0:
                        mesh.DirectUVs = ReadVector2List(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.SHDMESH_UV1:
                    case W3DChunkTypes.SHDMESH_TANGENTS:
                    case W3DChunkTypes.SHDMESH_BITANGENTS:
                    case W3DChunkTypes.SHDMESH_TANGENT_CROSS:
                    case W3DChunkTypes.SHDMESH_SHADER:
                        chunks.SkipChunk(chunk);
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }

                reader.Seek(chunk.ChunkEnd);
            }

            if (mesh.Header.VertCount == 0)
                mesh.Header.VertCount = mesh.Verts.Count;
            if (mesh.Header.FaceCount == 0)
                mesh.Header.FaceCount = mesh.Triangles.Count;

            return mesh;
        }

        private static List<Vector3> ReadVectorList(W3DBinaryReader reader, long end)
        {
            var list = new List<Vector3>();
            while (reader.Position + 12 <= end)
                list.Add(reader.ReadVector3());
            return list;
        }

        private static List<Vector2> ReadVector2List(W3DBinaryReader reader, long end)
        {
            var list = new List<Vector2>();
            while (reader.Position + 8 <= end)
                list.Add(reader.ReadVector2());
            return list;
        }

        private static List<W3DTriangle> ReadUInt16TriangleList(W3DBinaryReader reader, long end)
        {
            var list = new List<W3DTriangle>();
            while (reader.Position + 6 <= end)
            {
                var tri = new W3DTriangle();
                tri.VertIds[0] = reader.ReadUInt16();
                tri.VertIds[1] = reader.ReadUInt16();
                tri.VertIds[2] = reader.ReadUInt16();
                list.Add(tri);
            }

            return list;
        }

        // DAZZLE (0x900) container with two string sub-chunks:
        //   0x901 DAZZLE_NAME      — instance name
        //   0x902 DAZZLE_TYPENAME  — preset name (refs an external dazzle.ini entry)
        // Both are null-terminated ASCII. Used for flares / lens glints; the
        // Westwood viewer renders them as camera-facing billboard quads with
        // a brightness curve based on view angle. We just capture the data
        // here — actually rendering them requires a billboard helper that
        // the existing scene builder does not yet have.
        private static W3DDazzle ReadDazzle(W3DBinaryReader reader, W3DChunkReader chunks, long chunkEnd)
        {
            var dazzle = new W3DDazzle();
            foreach (var chunk in chunks.EnumerateChunks(chunkEnd))
            {
                switch (chunk.ChunkType)
                {
                    case 0x00000901: // DAZZLE_NAME
                        dazzle.Name = reader.ReadNullTerminatedString();
                        break;
                    case 0x00000902: // DAZZLE_TYPENAME
                        dazzle.TypeName = reader.ReadNullTerminatedString();
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return dazzle;
        }

        public static void CollectAnimations(string filePath, string hierarchyName, List<W3DAnimation> animations, List<W3DCompressedAnimation> compressedAnimations)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || string.IsNullOrWhiteSpace(hierarchyName))
                return;

            string normalizedPath = Path.GetFullPath(filePath);
            string cacheKey = normalizedPath + "|" + hierarchyName;
            if (AnimationScanCache.TryGetValue(cacheKey, out var cached))
            {
                animations.AddRange(cached.Animations);
                compressedAnimations.AddRange(cached.CompressedAnimations);
                return;
            }

            var foundAnimations = new List<W3DAnimation>();
            var foundCompressedAnimations = new List<W3DCompressedAnimation>();

            try
            {
                using var stream = new MemoryStream(File.ReadAllBytes(normalizedPath), false);
                using var reader = new W3DBinaryReader(stream);
                var chunks = new W3DChunkReader(reader);

                while (reader.Position < stream.Length)
                {
                    var chunk = chunks.ReadChunkHeader();

                    switch (chunk.ChunkType)
                    {
                        case W3DChunkTypes.ANIMATION:
                        {
                            var animation = W3DAnimationParser.Read(reader, chunks, chunk.ChunkEnd);
                            if (animation?.Header != null && string.Equals(animation.Header.HierarchyName, hierarchyName, System.StringComparison.OrdinalIgnoreCase))
                                foundAnimations.Add(animation);
                            break;
                        }

                        case W3DChunkTypes.COMPRESSED_ANIMATION:
                        {
                            var compressedAnimation = W3DCompressedAnimationParser.Read(reader, chunks, chunk.ChunkEnd);
                            if (compressedAnimation?.Header != null && string.Equals(compressedAnimation.Header.HierarchyName, hierarchyName, System.StringComparison.OrdinalIgnoreCase))
                                foundCompressedAnimations.Add(compressedAnimation);
                            break;
                        }
                    }

                    reader.Seek(chunk.ChunkEnd);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[W3D] Skipping external animation scan for '{normalizedPath}': {ex.Message}");
                return;
            }

            var result = new AnimationScanResult
            {
                Animations = foundAnimations.ToArray(),
                CompressedAnimations = foundCompressedAnimations.ToArray()
            };
            AnimationScanCache[cacheKey] = result;
            animations.AddRange(result.Animations);
            compressedAnimations.AddRange(result.CompressedAnimations);
        }

        private static void LogParser(string message)
        {
            if (EnableParserLogging)
                Debug.Log(message);
        }
    }
}
