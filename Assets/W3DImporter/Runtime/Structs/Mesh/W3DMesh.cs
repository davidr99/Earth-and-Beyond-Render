using System.Collections.Generic;
using UnityEngine;

namespace W3DImporter.Structs.Mesh
{
    public class W3DMeshHeader
    {
        public uint Version;
        public uint Attrs;
        public string MeshName = "";
        public string ContainerName = "";
        public int FaceCount;
        public int VertCount;
        public int MatlCount;
        public int DamageStageCount;
        public int SortLevel;
        public int PrelitVersion;
        public int FutureCount;
        public uint VertChannelFlags;
        public uint FaceChannelFlags;
        public Vector3 MinCorner;
        public Vector3 MaxCorner;
        public Vector3 SphCenter;
        public float SphRadius;
    }

    public class W3DTriangle
    {
        public uint[] VertIds = new uint[3];
        public uint Attrs;
        public Vector3 Normal;
        public float Dist;
    }

    public class W3DVertexInfluence
    {
        public ushort BoneIdx;
        public ushort XtraIdx;
        public float BoneInf;
        public float XtraInf;
    }

    public class W3DMaterialInfo
    {
        public int PassCount;
        public int VertMatCount;
        public int ShaderCount;
        public int TextureCount;
    }

    public class W3DShader
    {
        public byte DepthCompare;
        public byte DepthMask;
        public byte ColorMask;
        public byte DestBlend;
        public byte FogFunc;
        public byte PriGradient;
        public byte SecGradient;
        public byte SrcBlend;
        public byte Texturing;
        public byte DetailColorFunc;
        public byte DetailAlphaFunc;
        public byte ShaderPreset;
        public byte AlphaTest;
        public byte PostDetailColorFunc;
        public byte PostDetailAlphaFunc;
        public byte Pad;
    }

    public class W3DVertexMaterialInfo
    {
        public uint Attributes;
        public Color Ambient;
        public Color Diffuse;
        public Color Specular;
        public Color Emissive;
        public float Shininess;
        public float Opacity;
        public float Translucency;
    }

    public class W3DVertexMaterial
    {
        public string Name = "";
        public W3DVertexMaterialInfo Info;
        public string VmArgs0 = "";
        public string VmArgs1 = "";
    }

    public class W3DTexture
    {
        public string Id = "";
        public string File = "";
        public uint TextureInfo;
    }

    public class W3DTextureStage
    {
        public List<List<int>> TxIds = new List<List<int>>();
        public List<List<Vector2>> TxCoords = new List<List<Vector2>>();
    }

    public class W3DMaterialPass
    {
        public List<int> VertexMaterialIds = new List<int>();
        public List<int> ShaderIds = new List<int>();
        public List<int> ShaderMaterialIds = new List<int>();
        public List<W3DTextureStage> TxStages = new List<W3DTextureStage>();
        public List<Vector2> TxCoords = new List<Vector2>();
        public List<Vector2> TxCoords2 = new List<Vector2>();
        public List<Color32> Dcg = new List<Color32>();
        public List<Color32> Dig = new List<Color32>();
        public List<Color32> Scg = new List<Color32>();
    }

    // Geometry type flags
    public static class GeometryFlags
    {
        public const uint SKIN = 0x00020000;
        public const uint HIDDEN = 0x00001000;
        public const uint TWO_SIDED = 0x00002000;
        public const uint CAST_SHADOW = 0x00008000;
        public const uint CAMERA_ALIGNED = 0x00010000;
        public const uint CAMERA_ORIENTED = 0x00060000;
    }

    public class W3DMesh
    {
        public W3DMeshHeader Header;
        public string UserText = "";
        public List<Vector3> Verts = new List<Vector3>();
        public List<Vector3> Normals = new List<Vector3>();
        public List<Vector2> DirectUVs = new List<Vector2>();
        public List<Vector3> Tangents = new List<Vector3>();
        public List<Vector3> Bitangents = new List<Vector3>();
        public List<W3DVertexInfluence> VertInfs = new List<W3DVertexInfluence>();
        public List<W3DTriangle> Triangles = new List<W3DTriangle>();
        public List<int> ShadeIds = new List<int>();
        public List<int> PerTriangleShaderIndices = new List<int>();
        public W3DMaterialInfo MatInfo;
        public List<W3DShader> Shaders = new List<W3DShader>();
        public List<W3DVertexMaterial> VertMaterials = new List<W3DVertexMaterial>();
        public List<W3DTexture> Textures = new List<W3DTexture>();
        public List<W3DMaterialPass> MaterialPasses = new List<W3DMaterialPass>();
        public List<Color32> DirectVertexColors = new List<Color32>();
        public W3DUserTextData UserTextData;

        public string Name => Header?.MeshName ?? "";
        public string Identifier => (Header?.ContainerName ?? "") + "." + Name;
        public W3DEffectPassDescription EffectPass => UserTextData?.EffectPass;
        public W3DSurfaceDescription Surface => UserTextData?.Surface;

        public bool IsSkin => Header != null && (Header.Attrs & GeometryFlags.SKIN) != 0;
        public bool IsTwoSided => Header != null && (Header.Attrs & GeometryFlags.TWO_SIDED) != 0;
        public bool IsHidden => Header != null && (Header.Attrs & GeometryFlags.HIDDEN) != 0;
        public bool CastsShadow => Header != null && (Header.Attrs & GeometryFlags.CAST_SHADOW) != 0;
        public bool IsCameraAligned => Header != null && (Header.Attrs & GeometryFlags.CAMERA_ALIGNED) == GeometryFlags.CAMERA_ALIGNED;
        public bool IsCameraOriented => Header != null && (Header.Attrs & GeometryFlags.CAMERA_ORIENTED) == GeometryFlags.CAMERA_ORIENTED;
        public bool EffectiveTwoSided => IsTwoSided || EffectPass?.TwoSided == true;
        public bool EffectiveHidden => IsHidden || EffectPass?.Hidden == true;
    }
}
