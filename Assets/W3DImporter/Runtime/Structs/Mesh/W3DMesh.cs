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
        public ushort AnimType;
        public uint FrameCount = 1;
        public float FrameRate;

        public bool IsAnimated => FrameCount > 1;
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

    public static class W3DShaderMaterialPropertyType
    {
        public const int String = 1;
        public const int Float = 2;
        public const int Vec2 = 3;
        public const int Vec3 = 4;
        public const int Vec4 = 5;
        public const int Int = 6;
        public const int Bool = 7;
    }

    public class W3DShaderMaterialProperty
    {
        public int Type;
        public string Name = "";
        public string StringValue = "";
        public float FloatValue;
        public int IntValue;
        public bool BoolValue;
        public Vector4 VectorValue = Vector4.zero;

        public bool TryGetVector4(out Vector4 value)
        {
            value = VectorValue;
            return Type == W3DShaderMaterialPropertyType.Vec4
                || Type == W3DShaderMaterialPropertyType.Vec3
                || Type == W3DShaderMaterialPropertyType.Vec2;
        }
    }

    public class W3DShaderMaterial
    {
        public byte Version;
        public string TypeName = "";
        public int Technique;
        public List<W3DShaderMaterialProperty> Properties = new List<W3DShaderMaterialProperty>();
    }

    // W3D mesh header Attrs:
    //   bits 0-15 are independent flag bits (HIDDEN, TWO_SIDED, CAST_SHADOW, etc.)
    //   bits 16-23 hold an enum-valued geometry type, masked by GEOMETRY_TYPE_MASK.
    // SKIN, CAMERA_ALIGNED and CAMERA_ORIENTED are mutually exclusive enum values,
    // not OR-able bits — testing them with `& VALUE != 0` gives false positives
    // because 0x60000 overlaps 0x20000.
    public static class GeometryFlags
    {
        public const uint HIDDEN = 0x00001000;
        public const uint TWO_SIDED = 0x00002000;
        public const uint CAST_SHADOW = 0x00008000;

        public const uint GEOMETRY_TYPE_MASK = 0x00FF0000;
        public const uint GEOMETRY_TYPE_NORMAL = 0x00000000;
        public const uint GEOMETRY_TYPE_CAMERA_ALIGNED = 0x00010000;
        public const uint GEOMETRY_TYPE_SKIN = 0x00020000;
        public const uint GEOMETRY_TYPE_AABOX = 0x00040000;
        public const uint GEOMETRY_TYPE_OBBOX = 0x00050000;
        public const uint GEOMETRY_TYPE_CAMERA_ORIENTED = 0x00060000;
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
        public List<int> LegacyMaterialTextureIds = new List<int>();
        public List<W3DMaterialPass> MaterialPasses = new List<W3DMaterialPass>();
        public List<W3DShaderMaterial> ShaderMaterials = new List<W3DShaderMaterial>();
        public List<Color32> DirectVertexColors = new List<Color32>();
        public W3DUserTextData UserTextData;

        public string Name => Header?.MeshName ?? "";
        public string Identifier => (Header?.ContainerName ?? "") + "." + Name;
        public W3DEffectPassDescription EffectPass => UserTextData?.EffectPass;
        public W3DSurfaceDescription Surface => UserTextData?.Surface;

        private uint GeometryType => (Header?.Attrs ?? 0) & GeometryFlags.GEOMETRY_TYPE_MASK;
        public bool IsSkin => GeometryType == GeometryFlags.GEOMETRY_TYPE_SKIN;
        public bool IsCameraAligned => GeometryType == GeometryFlags.GEOMETRY_TYPE_CAMERA_ALIGNED;
        public bool IsCameraOriented => GeometryType == GeometryFlags.GEOMETRY_TYPE_CAMERA_ORIENTED;
        public bool IsTwoSided => Header != null && (Header.Attrs & GeometryFlags.TWO_SIDED) != 0;
        public bool IsHidden => Header != null && (Header.Attrs & GeometryFlags.HIDDEN) != 0;
        public bool CastsShadow => Header != null && (Header.Attrs & GeometryFlags.CAST_SHADOW) != 0;
        public bool EffectiveTwoSided => IsTwoSided || EffectPass?.TwoSided == true;
        public bool EffectiveHidden => IsHidden || EffectPass?.Hidden == true;

        // Index of the material pass this object stands for (see CreatePassView).
        public int PassIndex;

        // The client draws every material pass over the previous ones (glow / lightmap /
        // environment overlays). The builders read pass 0, so an additional pass is built from
        // a view that shares everything with this mesh but has that pass as its only pass.
        public W3DMesh CreatePassView(int passIndex)
        {
            var pass = MaterialPasses[passIndex];
            var view = (W3DMesh)MemberwiseClone();
            view.MaterialPasses = new List<W3DMaterialPass> { pass };
            view.PassIndex = passIndex;
            // mesh-level uvs would take precedence over the pass's own coordinates
            if (pass.TxCoords.Count > 0 || pass.TxStages.Exists(s => s.TxCoords.Count > 0))
                view.DirectUVs = new List<Vector2>();
            return view;
        }
    }
}
