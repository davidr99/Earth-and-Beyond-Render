using System.Collections.Generic;
using System.IO;
using UnityEngine;
using W3DImporter.IO;
using W3DImporter.Structs.Mesh;

namespace W3DImporter.Parsers
{
    public static class W3DMeshParser
    {
        public static W3DMesh Read(W3DBinaryReader reader, W3DChunkReader chunks, long chunkEnd)
        {
            var mesh = new W3DMesh();
            byte[] triangleChunkData = null;
            byte[] influenceChunkData = null;
            bool sawEnbSpecificMeshData = false;

            foreach (var chunk in chunks.EnumerateChunks(chunkEnd))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.MESH_HEADER:
                        mesh.Header = ReadMeshHeader(reader);
                        break;
                    case W3DChunkTypes.VERTICES:
                        mesh.Verts = ReadVectorList(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.VERTEX_NORMALS:
                        mesh.Normals = ReadVectorList(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.ENB_UV_COORDS:
                        sawEnbSpecificMeshData = true;
                        mesh.DirectUVs = ReadVector2List(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.TRIANGLES:
                        triangleChunkData = reader.ReadBytes((int)(chunk.ChunkEnd - reader.Position));
                        break;
                    case W3DChunkTypes.ENB_PER_TRIANGLE_SHADER_INDEX:
                        sawEnbSpecificMeshData = true;
                        mesh.PerTriangleShaderIndices = ReadUInt16List(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.ENB_MATERIAL_VERTEX_COLORS:
                        sawEnbSpecificMeshData = true;
                        mesh.DirectVertexColors = ReadEnbVertexColors(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.VERTEX_INFLUENCES:
                        influenceChunkData = reader.ReadBytes((int)(chunk.ChunkEnd - reader.Position));
                        break;
                    case W3DChunkTypes.MESH_USER_TEXT:
                        mesh.UserText = reader.ReadNullTerminatedString();
                        mesh.UserTextData = W3DUserTextParser.Parse(mesh.UserText);
                        if (mesh.UserTextData?.EffectPass != null || mesh.UserTextData?.Surface != null)
                            sawEnbSpecificMeshData = true;
                        break;
                    case W3DChunkTypes.VERTEX_SHADE_INDICES:
                        mesh.ShadeIds = ReadIntList(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.MATERIAL_INFO:
                        mesh.MatInfo = ReadMaterialInfo(reader);
                        break;
                    case W3DChunkTypes.SHADERS:
                        mesh.Shaders = ReadShaderList(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.VERTEX_MATERIALS:
                        mesh.VertMaterials = ReadVertexMaterials(reader, chunks, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.TEXTURES:
                        mesh.Textures = ReadTextures(reader, chunks, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.MATERIAL_PASS:
                        mesh.MaterialPasses.Add(ReadMaterialPass(reader, chunks, chunk.ChunkEnd));
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }

            if (triangleChunkData != null)
                mesh.Triangles = ReadTriangleList(triangleChunkData, mesh.Header, sawEnbSpecificMeshData);

            if (influenceChunkData != null)
                mesh.VertInfs = ReadVertexInfluences(influenceChunkData, mesh.Header, sawEnbSpecificMeshData);

            return mesh;
        }

        private static W3DMeshHeader ReadMeshHeader(W3DBinaryReader reader)
        {
            var header = new W3DMeshHeader();
            header.Version = reader.ReadUInt32();
            header.Attrs = reader.ReadUInt32();
            header.MeshName = reader.ReadFixedString();
            header.ContainerName = reader.ReadFixedString();
            header.FaceCount = reader.ReadInt32();
            header.VertCount = reader.ReadInt32();
            header.MatlCount = reader.ReadInt32();
            header.DamageStageCount = reader.ReadInt32();
            header.SortLevel = reader.ReadInt32();
            header.PrelitVersion = reader.ReadInt32();
            header.FutureCount = reader.ReadInt32();
            header.VertChannelFlags = reader.ReadUInt32();
            header.FaceChannelFlags = reader.ReadUInt32();
            header.MinCorner = reader.ReadVector3();
            header.MaxCorner = reader.ReadVector3();
            header.SphCenter = reader.ReadVector3();
            header.SphRadius = reader.ReadFloat();
            return header;
        }

        private static List<Vector3> ReadVectorList(W3DBinaryReader reader, long end)
        {
            var list = new List<Vector3>();
            while (reader.Position < end)
                list.Add(reader.ReadVector3());
            return list;
        }

        private static List<int> ReadIntList(W3DBinaryReader reader, long end)
        {
            var list = new List<int>();
            while (reader.Position < end)
                list.Add(reader.ReadInt32());
            return list;
        }

        private static List<int> ReadUInt16List(W3DBinaryReader reader, long end)
        {
            var list = new List<int>();
            while (reader.Position < end)
                list.Add(reader.ReadUInt16());
            return list;
        }

        private static List<Vector2> ReadVector2List(W3DBinaryReader reader, long end)
        {
            var list = new List<Vector2>();
            while (reader.Position < end)
                list.Add(reader.ReadVector2());
            return list;
        }

        private static List<W3DTriangle> ReadTriangleList(byte[] data, W3DMeshHeader header, bool preferEnb)
        {
            var list = new List<W3DTriangle>();
            using var stream = new MemoryStream(data);
            using var reader = new W3DBinaryReader(stream);

            long end = stream.Length;
            int faceCount = header != null ? Mathf.Max(header.FaceCount, 0) : 0;

            if (preferEnb && faceCount > 0 && end == faceCount * 32L)
            {
                while (reader.Position < end)
                {
                    var tri = new W3DTriangle();
                    tri.VertIds[0] = reader.ReadUInt16();
                    tri.VertIds[1] = reader.ReadUInt16();
                    tri.VertIds[2] = reader.ReadUInt16();
                    tri.Attrs = reader.ReadByte();
                    reader.ReadByte();
                    reader.Skip(12);
                    tri.Dist = -reader.ReadFloat();
                    reader.Skip(8);
                    list.Add(tri);
                }

                return list;
            }

            while (reader.Position < end)
            {
                var tri = new W3DTriangle();
                tri.VertIds[0] = reader.ReadUInt32();
                tri.VertIds[1] = reader.ReadUInt32();
                tri.VertIds[2] = reader.ReadUInt32();
                tri.Attrs = reader.ReadUInt32();
                tri.Normal = reader.ReadVector3();
                tri.Dist = reader.ReadFloat();
                list.Add(tri);
            }
            return list;
        }

        private static List<W3DVertexInfluence> ReadVertexInfluences(byte[] data, W3DMeshHeader header, bool preferEnb)
        {
            var list = new List<W3DVertexInfluence>();
            using var stream = new MemoryStream(data);
            using var reader = new W3DBinaryReader(stream);

            long end = stream.Length;
            int vertCount = header != null ? Mathf.Max(header.VertCount, 0) : 0;

            if (preferEnb && vertCount > 0 && end == vertCount * 8L)
            {
                while (reader.Position < end)
                {
                    var vi = new W3DVertexInfluence
                    {
                        BoneIdx = reader.ReadUInt16(),
                        XtraIdx = 0,
                        BoneInf = 1f,
                        XtraInf = 0f
                    };

                    reader.Skip(6);
                    list.Add(vi);
                }

                return list;
            }

            while (reader.Position < end)
            {
                var vi = new W3DVertexInfluence();
                vi.BoneIdx = reader.ReadUInt16();
                vi.XtraIdx = reader.ReadUInt16();
                vi.BoneInf = (float)reader.ReadUInt16() / 100f;
                vi.XtraInf = (float)reader.ReadUInt16() / 100f;
                list.Add(vi);
            }
            return list;
        }

        private static List<Color32> ReadEnbVertexColors(W3DBinaryReader reader, long end)
        {
            var list = new List<Color32>();
            while (reader.Position < end)
            {
                byte r = reader.ReadByte();
                byte g = reader.ReadByte();
                byte b = reader.ReadByte();
                reader.ReadByte();
                list.Add(new Color32(r, g, b, 255));
            }
            return list;
        }

        private static W3DMaterialInfo ReadMaterialInfo(W3DBinaryReader reader)
        {
            var info = new W3DMaterialInfo();
            info.PassCount = reader.ReadInt32();
            info.VertMatCount = reader.ReadInt32();
            info.ShaderCount = reader.ReadInt32();
            info.TextureCount = reader.ReadInt32();
            return info;
        }

        private static List<W3DShader> ReadShaderList(W3DBinaryReader reader, long end)
        {
            var list = new List<W3DShader>();
            while (reader.Position + 16 <= end)
            {
                var s = new W3DShader();
                s.DepthCompare = reader.ReadByte();
                s.DepthMask = reader.ReadByte();
                s.ColorMask = reader.ReadByte();
                s.DestBlend = reader.ReadByte();
                s.FogFunc = reader.ReadByte();
                s.PriGradient = reader.ReadByte();
                s.SecGradient = reader.ReadByte();
                s.SrcBlend = reader.ReadByte();
                s.Texturing = reader.ReadByte();
                s.DetailColorFunc = reader.ReadByte();
                s.DetailAlphaFunc = reader.ReadByte();
                s.ShaderPreset = reader.ReadByte();
                s.AlphaTest = reader.ReadByte();
                s.PostDetailColorFunc = reader.ReadByte();
                s.PostDetailAlphaFunc = reader.ReadByte();
                s.Pad = reader.ReadByte();
                list.Add(s);
            }
            return list;
        }

        private static List<W3DVertexMaterial> ReadVertexMaterials(W3DBinaryReader reader, W3DChunkReader chunks, long end)
        {
            var list = new List<W3DVertexMaterial>();
            foreach (var chunk in chunks.EnumerateChunks(end))
            {
                if (chunk.ChunkType == W3DChunkTypes.VERTEX_MATERIAL)
                {
                    var vm = new W3DVertexMaterial();
                    foreach (var sub in chunks.EnumerateChunks(chunk.ChunkEnd))
                    {
                        switch (sub.ChunkType)
                        {
                            case W3DChunkTypes.VERTEX_MATERIAL_NAME:
                                vm.Name = reader.ReadNullTerminatedString();
                                break;
                            case W3DChunkTypes.VERTEX_MATERIAL_INFO:
                                vm.Info = ReadVertexMaterialInfo(reader);
                                break;
                            default:
                                chunks.SkipChunk(sub);
                                break;
                        }
                        reader.Seek(sub.ChunkEnd);
                    }
                    list.Add(vm);
                }
                else
                {
                    chunks.SkipChunk(chunk);
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return list;
        }

        private static W3DVertexMaterialInfo ReadVertexMaterialInfo(W3DBinaryReader reader)
        {
            var info = new W3DVertexMaterialInfo();
            info.Attributes = reader.ReadUInt32();
            info.Ambient = ReadColor(reader);
            info.Diffuse = ReadColor(reader);
            info.Specular = ReadColor(reader);
            info.Emissive = ReadColor(reader);
            info.Shininess = reader.ReadFloat();
            info.Opacity = reader.ReadFloat();
            info.Translucency = reader.ReadFloat();
            return info;
        }

        private static Color ReadColor(W3DBinaryReader reader)
        {
            byte r = reader.ReadByte();
            byte g = reader.ReadByte();
            byte b = reader.ReadByte();
            byte a = reader.ReadByte();
            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        private static List<W3DTexture> ReadTextures(W3DBinaryReader reader, W3DChunkReader chunks, long end)
        {
            var list = new List<W3DTexture>();
            foreach (var chunk in chunks.EnumerateChunks(end))
            {
                if (chunk.ChunkType == W3DChunkTypes.TEXTURE)
                {
                    var tex = new W3DTexture();
                    foreach (var sub in chunks.EnumerateChunks(chunk.ChunkEnd))
                    {
                        switch (sub.ChunkType)
                        {
                            case W3DChunkTypes.TEXTURE_NAME:
                                tex.File = reader.ReadNullTerminatedString();
                                tex.Id = tex.File;
                                break;
                            case W3DChunkTypes.TEXTURE_INFO:
                                tex.TextureInfo = reader.ReadUInt32();
                                reader.Skip(8);
                                break;
                            default:
                                chunks.SkipChunk(sub);
                                break;
                        }
                        reader.Seek(sub.ChunkEnd);
                    }
                    list.Add(tex);
                }
                else
                {
                    chunks.SkipChunk(chunk);
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return list;
        }

        private static W3DMaterialPass ReadMaterialPass(W3DBinaryReader reader, W3DChunkReader chunks, long end)
        {
            var pass = new W3DMaterialPass();
            foreach (var chunk in chunks.EnumerateChunks(end))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.VERTEX_MATERIAL_IDS:
                        while (reader.Position < chunk.ChunkEnd)
                            pass.VertexMaterialIds.Add(reader.ReadInt32());
                        break;
                    case W3DChunkTypes.SHADER_IDS:
                        while (reader.Position < chunk.ChunkEnd)
                            pass.ShaderIds.Add(reader.ReadInt32());
                        break;
                    case W3DChunkTypes.SHADER_MATERIAL_ID:
                        while (reader.Position < chunk.ChunkEnd)
                            pass.ShaderMaterialIds.Add(reader.ReadInt32());
                        break;
                    case W3DChunkTypes.DCG:
                        while (reader.Position < chunk.ChunkEnd)
                            pass.Dcg.Add(reader.ReadRGBA());
                        break;
                    case W3DChunkTypes.DIG:
                        while (reader.Position < chunk.ChunkEnd)
                            pass.Dig.Add(reader.ReadRGBA());
                        break;
                    case W3DChunkTypes.SCG:
                        while (reader.Position < chunk.ChunkEnd)
                            pass.Scg.Add(reader.ReadRGBA());
                        break;
                    case W3DChunkTypes.TEXTURE_STAGE:
                        pass.TxStages.Add(ReadTextureStage(reader, chunks, chunk.ChunkEnd));
                        break;
                    case W3DChunkTypes.STAGE_TEXCOORDS:
                        var coords = new List<Vector2>();
                        while (reader.Position < chunk.ChunkEnd)
                            coords.Add(reader.ReadVector2());
                        pass.TxCoords = coords;
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return pass;
        }

        private static W3DTextureStage ReadTextureStage(W3DBinaryReader reader, W3DChunkReader chunks, long end)
        {
            var stage = new W3DTextureStage();
            foreach (var chunk in chunks.EnumerateChunks(end))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.TEXTURE_IDS:
                        var ids = new List<int>();
                        while (reader.Position < chunk.ChunkEnd)
                            ids.Add(reader.ReadInt32());
                        stage.TxIds.Add(ids);
                        break;
                    case W3DChunkTypes.STAGE_TEXCOORDS:
                        var coords = new List<Vector2>();
                        while (reader.Position < chunk.ChunkEnd)
                            coords.Add(reader.ReadVector2());
                        stage.TxCoords.Add(coords);
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return stage;
        }
    }
}
