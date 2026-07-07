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
            bool sawLegacyMaterials = false;

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
                    case W3DChunkTypes.TEXCOORDS:
                        mesh.DirectUVs = ReadVector2List(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.TRIANGLES:
                        triangleChunkData = reader.ReadBytes((int)(chunk.ChunkEnd - reader.Position));
                        break;
                    case W3DChunkTypes.PER_TRIANGLE_MATERIAL_IDS:
                        mesh.PerTriangleShaderIndices = ReadUInt16List(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.VERTEX_COLORS:
                        mesh.DirectVertexColors = ReadEnbVertexColors(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.VERTEX_INFLUENCES:
                        influenceChunkData = reader.ReadBytes((int)(chunk.ChunkEnd - reader.Position));
                        break;
                    case W3DChunkTypes.MESH_USER_TEXT:
                        mesh.UserText = reader.ReadNullTerminatedString();
                        mesh.UserTextData = W3DUserTextParser.Parse(mesh.UserText);
                        break;
                    case W3DChunkTypes.MATERIALS3:
                        ReadMaterials3(reader, chunks, chunk.ChunkEnd, mesh);
                        sawLegacyMaterials = true;
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
                    case W3DChunkTypes.SHADER_MATERIALS:
                        mesh.ShaderMaterials = ReadShaderMaterials(reader, chunks, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.MATERIAL_PASS:
                        mesh.MaterialPasses.Add(ReadMaterialPass(reader, chunks, chunk.ChunkEnd));
                        break;
                    case W3DChunkTypes.PRELIT_UNLIT:
                    case W3DChunkTypes.PRELIT_VERTEX:
                    case W3DChunkTypes.PRELIT_LIGHTMAP_MULTI_PASS:
                    case W3DChunkTypes.PRELIT_LIGHTMAP_MULTI_TEXTURE:
                        if (ShouldUsePrelitContainer(mesh, chunk.ChunkType))
                            ReadMaterialSet(reader, chunks, chunk.ChunkEnd, mesh, replaceExisting: true,
                                prelitVertex: chunk.ChunkType == W3DChunkTypes.PRELIT_VERTEX);
                        else
                            chunks.SkipChunk(chunk);
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }

            if (triangleChunkData != null)
                mesh.Triangles = ReadTriangleList(triangleChunkData);

            if (influenceChunkData != null)
                mesh.VertInfs = ReadVertexInfluences(influenceChunkData);

            if (sawLegacyMaterials)
                ApplyLegacyMaterialBindings(mesh);

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

        private static List<W3DTriangle> ReadTriangleList(byte[] data)
        {
            var list = new List<W3DTriangle>();
            using var stream = new MemoryStream(data);
            using var reader = new W3DBinaryReader(stream);

            long end = stream.Length;

            while (reader.Position + 32 <= end)
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

        private static List<W3DVertexInfluence> ReadVertexInfluences(byte[] data)
        {
            var list = new List<W3DVertexInfluence>();
            using var stream = new MemoryStream(data);
            using var reader = new W3DBinaryReader(stream);

            long end = stream.Length;

            while (reader.Position + 8 <= end)
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

        private static void ReadMaterialSet(W3DBinaryReader reader, W3DChunkReader chunks, long end, W3DMesh mesh, bool replaceExisting, bool prelitVertex = false)
        {
            if (replaceExisting)
            {
                mesh.MatInfo = null;
                mesh.Shaders.Clear();
                mesh.VertMaterials.Clear();
                mesh.Textures.Clear();
                mesh.LegacyMaterialTextureIds.Clear();
                mesh.MaterialPasses.Clear();
                mesh.ShaderMaterials.Clear();
            }

            foreach (var chunk in chunks.EnumerateChunks(end))
            {
                switch (chunk.ChunkType)
                {
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
                        mesh.MaterialPasses.Add(ReadMaterialPass(reader, chunks, chunk.ChunkEnd, prelitVertex));
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }

                reader.Seek(chunk.ChunkEnd);
            }
        }

        private static bool ShouldUsePrelitContainer(W3DMesh mesh, uint chunkType)
        {
            uint attrs = mesh?.Header?.Attrs ?? 0u;
            uint prelit = attrs & 0x0F000000u;
            uint selected = prelit switch
            {
                0x02000000u => W3DChunkTypes.PRELIT_VERTEX,
                0x04000000u => W3DChunkTypes.PRELIT_LIGHTMAP_MULTI_PASS,
                0x08000000u => W3DChunkTypes.PRELIT_LIGHTMAP_MULTI_TEXTURE,
                _ => W3DChunkTypes.PRELIT_UNLIT
            };

            if (prelit != 0)
                return chunkType == selected;

            return chunkType == W3DChunkTypes.PRELIT_UNLIT &&
                   mesh.VertMaterials.Count == 0 &&
                   mesh.MaterialPasses.Count == 0 &&
                   mesh.Textures.Count == 0;
        }

        private static void ReadMaterials3(W3DBinaryReader reader, W3DChunkReader chunks, long end, W3DMesh mesh)
        {
            while (reader.Position < end)
            {
                var chunk = chunks.ReadChunkHeader();
                if (chunk.ChunkType == W3DChunkTypes.MATERIAL3)
                    ReadMaterial3(reader, chunks, chunk.ChunkEnd, mesh);
                else
                    chunks.SkipChunk(chunk);

                reader.Seek(chunk.ChunkEnd);
            }
        }

        private static void ReadMaterial3(W3DBinaryReader reader, W3DChunkReader chunks, long end, W3DMesh mesh)
        {
            string name = "";
            W3DVertexMaterialInfo info = null;
            Color rawDiffuse = Color.white;
            string diffuseTexture = "";
            string selfIllumTexture = "";

            foreach (var chunk in chunks.EnumerateChunks(end))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.MATERIAL3_NAME:
                        name = reader.ReadNullTerminatedString();
                        break;
                    case W3DChunkTypes.MATERIAL3_INFO:
                        info = ReadMaterial3Info(reader, out rawDiffuse);
                        break;
                    case W3DChunkTypes.MATERIAL3_DC_MAP:
                        diffuseTexture = ReadMap3TextureName(reader, chunks, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.MATERIAL3_SI_MAP:
                        selfIllumTexture = ReadMap3TextureName(reader, chunks, chunk.ChunkEnd);
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }

                reader.Seek(chunk.ChunkEnd);
            }

            if (info == null)
                info = new W3DVertexMaterialInfo { Diffuse = Color.white, Ambient = Color.white };

            // Client honors the SI map only when the RAW DiffuseColor is (0,0,0) — before
            // the DiffuseCoeffs multiply (W3D_FORMAT.md §5).
            bool useSelfIllum = !string.IsNullOrWhiteSpace(selfIllumTexture) &&
                                rawDiffuse.r <= 0.001f &&
                                rawDiffuse.g <= 0.001f &&
                                rawDiffuse.b <= 0.001f;

            int materialIndex = mesh.VertMaterials.Count;
            mesh.VertMaterials.Add(new W3DVertexMaterial
            {
                Name = string.IsNullOrWhiteSpace(name) ? $"MATERIAL3_{materialIndex}" : name,
                Info = info
            });

            string textureName = useSelfIllum ? selfIllumTexture : diffuseTexture;
            int textureIndex = -1;
            if (!string.IsNullOrWhiteSpace(textureName))
            {
                textureIndex = mesh.Textures.Count;
                mesh.Textures.Add(new W3DTexture
                {
                    Id = textureName,
                    File = textureName
                });
            }

            mesh.LegacyMaterialTextureIds.Add(textureIndex);
            mesh.Shaders.Add(CreateMaterial3Shader(info, useSelfIllum));
        }

        private static W3DVertexMaterialInfo ReadMaterial3Info(W3DBinaryReader reader, out Color rawDiffuse)
        {
            var info = new W3DVertexMaterialInfo();
            info.Attributes = reader.ReadUInt32();
            var diffuse = ReadColor(reader);
            rawDiffuse = diffuse;
            var specular = ReadColor(reader);
            var emissive = ReadColor(reader);
            var ambient = ReadColor(reader);
            var diffuseCoeffs = ReadColor(reader);
            var specularCoeffs = ReadColor(reader);

            info.Diffuse = MultiplyRgb(diffuse, diffuseCoeffs);
            info.Specular = MultiplyRgb(specular, specularCoeffs);
            info.Emissive = emissive;
            info.Ambient = ambient;
            info.Shininess = reader.ReadFloat();
            info.Opacity = reader.ReadFloat();
            info.Translucency = reader.ReadFloat();
            reader.ReadFloat(); // FogCoeff
            return info;
        }

        private static string ReadMap3TextureName(W3DBinaryReader reader, W3DChunkReader chunks, long end)
        {
            string textureName = "";

            foreach (var chunk in chunks.EnumerateChunks(end))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.MAP3_FILENAME:
                        textureName = reader.ReadNullTerminatedString();
                        break;
                    case W3DChunkTypes.MAP3_INFO:
                        chunks.SkipChunk(chunk);
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }

                reader.Seek(chunk.ChunkEnd);
            }

            return textureName;
        }

        private static W3DShader CreateMaterial3Shader(W3DVertexMaterialInfo info, bool additive)
        {
            var shader = new W3DShader
            {
                DepthCompare = 1,
                DepthMask = 1,
                ColorMask = 1,
                SrcBlend = 1,
                DestBlend = 0,
                Texturing = 1
            };

            if (additive)
            {
                shader.DepthMask = 0;
                shader.SrcBlend = 1;
                shader.DestBlend = 1;
            }
            // Only MATERIAL3 attribute bit 0 enables alpha blending in the client
            // (W3D_FORMAT.md §5); Opacity/Translucency do not switch the blend mode.
            else if (info != null && (info.Attributes & 0x1u) != 0)
            {
                shader.DepthMask = 0;
                shader.SrcBlend = 2;
                shader.DestBlend = 5;
            }

            return shader;
        }

        private static void ApplyLegacyMaterialBindings(W3DMesh mesh)
        {
            if (mesh.MaterialPasses.Count > 0 || mesh.VertMaterials.Count == 0)
                return;

            var pass = new W3DMaterialPass();
            var stage = new W3DTextureStage();
            var textureIds = new List<int>();

            bool hasPerFaceMaterials = mesh.PerTriangleShaderIndices.Count == mesh.Triangles.Count && mesh.Triangles.Count > 0;
            if (hasPerFaceMaterials)
            {
                for (int i = 0; i < mesh.PerTriangleShaderIndices.Count; i++)
                {
                    int materialId = Mathf.Clamp(mesh.PerTriangleShaderIndices[i], 0, mesh.VertMaterials.Count - 1);
                    pass.VertexMaterialIds.Add(materialId);
                    pass.ShaderIds.Add(materialId < mesh.Shaders.Count ? materialId : -1);
                    textureIds.Add(GetLegacyMaterialTextureId(mesh, materialId));
                }
            }
            else
            {
                pass.VertexMaterialIds.Add(0);
                pass.ShaderIds.Add(mesh.Shaders.Count > 0 ? 0 : -1);
                textureIds.Add(GetLegacyMaterialTextureId(mesh, 0));
            }

            stage.TxIds.Add(textureIds);
            pass.TxStages.Add(stage);
            mesh.MaterialPasses.Add(pass);
        }

        private static int GetLegacyMaterialTextureId(W3DMesh mesh, int materialId)
        {
            if (mesh.LegacyMaterialTextureIds.Count == 0)
                return materialId < mesh.Textures.Count ? materialId : -1;

            return materialId >= 0 && materialId < mesh.LegacyMaterialTextureIds.Count
                ? mesh.LegacyMaterialTextureIds[materialId]
                : -1;
        }

        private static Color MultiplyRgb(Color color, Color multiplier)
        {
            return new Color(color.r * multiplier.r, color.g * multiplier.g, color.b * multiplier.b, color.a);
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
                            case W3DChunkTypes.VERTEX_MAPPER_ARGS0:
                                vm.VmArgs0 = reader.ReadString((int)(sub.ChunkEnd - reader.Position));
                                break;
                            case W3DChunkTypes.VERTEX_MAPPER_ARGS1:
                                vm.VmArgs1 = reader.ReadString((int)(sub.ChunkEnd - reader.Position));
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
                                tex.TextureInfo = reader.ReadUInt16();
                                tex.AnimType = reader.ReadUInt16();
                                tex.FrameCount = reader.ReadUInt32();
                                tex.FrameRate = reader.ReadFloat();
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

        private static W3DMaterialPass ReadMaterialPass(W3DBinaryReader reader, W3DChunkReader chunks, long end, bool prelitVertex = false)
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
                        // Second DCG in a PRELIT_VERTEX container replaces only the alpha
                        // channel of the existing per-vertex colors (W3D_FORMAT.md §3.3).
                        if (prelitVertex && pass.Dcg.Count > 0)
                        {
                            int dcgIndex = 0;
                            while (reader.Position < chunk.ChunkEnd)
                            {
                                var replacement = reader.ReadRGBA();
                                if (dcgIndex < pass.Dcg.Count)
                                {
                                    var existing = pass.Dcg[dcgIndex];
                                    existing.a = replacement.a;
                                    pass.Dcg[dcgIndex] = existing;
                                }
                                dcgIndex++;
                            }
                        }
                        else
                        {
                            while (reader.Position < chunk.ChunkEnd)
                                pass.Dcg.Add(reader.ReadRGBA());
                        }
                        break;
                    case W3DChunkTypes.DIG:
                        // DIG alpha is forced opaque; a second DIG multiplies into the
                        // existing colors (W3D_FORMAT.md §3.3).
                        if (pass.Dig.Count > 0)
                        {
                            int digIndex = 0;
                            while (reader.Position < chunk.ChunkEnd)
                            {
                                var factor = reader.ReadRGBA();
                                if (digIndex < pass.Dig.Count)
                                {
                                    var existing = pass.Dig[digIndex];
                                    existing.r = (byte)(existing.r * factor.r / 255);
                                    existing.g = (byte)(existing.g * factor.g / 255);
                                    existing.b = (byte)(existing.b * factor.b / 255);
                                    pass.Dig[digIndex] = existing;
                                }
                                digIndex++;
                            }
                        }
                        else
                        {
                            while (reader.Position < chunk.ChunkEnd)
                            {
                                var dig = reader.ReadRGBA();
                                dig.a = 255;
                                pass.Dig.Add(dig);
                            }
                        }
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

        private static List<W3DShaderMaterial> ReadShaderMaterials(W3DBinaryReader reader, W3DChunkReader chunks, long end)
        {
            var list = new List<W3DShaderMaterial>();
            foreach (var chunk in chunks.EnumerateChunks(end))
            {
                if (chunk.ChunkType == W3DChunkTypes.SHADER_MATERIAL)
                    list.Add(ReadShaderMaterial(reader, chunks, chunk.ChunkEnd));
                else
                    chunks.SkipChunk(chunk);

                reader.Seek(chunk.ChunkEnd);
            }

            return list;
        }

        private static W3DShaderMaterial ReadShaderMaterial(W3DBinaryReader reader, W3DChunkReader chunks, long end)
        {
            var material = new W3DShaderMaterial();
            foreach (var chunk in chunks.EnumerateChunks(end))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.SHADER_MATERIAL_HEADER:
                        material.Version = reader.ReadByte();
                        material.TypeName = reader.ReadLongFixedString();
                        material.Technique = reader.ReadInt32();
                        break;
                    case W3DChunkTypes.SHADER_MATERIAL_PROPERTY:
                        material.Properties.Add(ReadShaderMaterialProperty(reader));
                        break;
                    default:
                        chunks.SkipChunk(chunk);
                        break;
                }

                reader.Seek(chunk.ChunkEnd);
            }

            return material;
        }

        private static W3DShaderMaterialProperty ReadShaderMaterialProperty(W3DBinaryReader reader)
        {
            var property = new W3DShaderMaterialProperty
            {
                Type = reader.ReadInt32()
            };

            int nameLength = reader.ReadInt32();
            property.Name = reader.ReadString(nameLength);

            switch (property.Type)
            {
                case W3DShaderMaterialPropertyType.String:
                {
                    int valueLength = reader.ReadInt32();
                    property.StringValue = reader.ReadString(valueLength);
                    break;
                }
                case W3DShaderMaterialPropertyType.Float:
                    property.FloatValue = reader.ReadFloat();
                    break;
                case W3DShaderMaterialPropertyType.Vec2:
                {
                    var value = reader.ReadVector2();
                    property.VectorValue = new Vector4(value.x, value.y, 0f, 0f);
                    break;
                }
                case W3DShaderMaterialPropertyType.Vec3:
                {
                    var value = reader.ReadVector3();
                    property.VectorValue = new Vector4(value.x, value.y, value.z, 0f);
                    break;
                }
                case W3DShaderMaterialPropertyType.Vec4:
                {
                    var value = reader.ReadBytes(16);
                    property.VectorValue = new Vector4(
                        System.BitConverter.ToSingle(value, 0),
                        System.BitConverter.ToSingle(value, 4),
                        System.BitConverter.ToSingle(value, 8),
                        System.BitConverter.ToSingle(value, 12));
                    break;
                }
                case W3DShaderMaterialPropertyType.Int:
                    property.IntValue = reader.ReadInt32();
                    break;
                case W3DShaderMaterialPropertyType.Bool:
                    property.BoolValue = reader.ReadByte() != 0;
                    break;
            }

            return property;
        }
    }
}
