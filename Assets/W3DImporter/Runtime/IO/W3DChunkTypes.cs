namespace W3DImporter.IO
{
    public static class W3DChunkTypes
    {
        // Top-level chunks
        public const uint MESH = 0x00000000;
        public const uint HIERARCHY = 0x00000100;
        public const uint ANIMATION = 0x00000200;
        public const uint COMPRESSED_ANIMATION = 0x00000280;
        public const uint MORPH_ANIMATION = 0x000002C0;
        public const uint HMODEL = 0x00000300;
        public const uint LODMODEL = 0x00000400;
        public const uint COLLECTION = 0x00000420;
        public const uint POINTS = 0x00000440;
        public const uint LIGHT = 0x00000460;
        public const uint EMITTER = 0x00000500;
        public const uint AGGREGATE = 0x00000600;
        public const uint HLOD = 0x00000700;
        public const uint BOX = 0x00000740;
        public const uint NULL_OBJECT = 0x00000750;
        public const uint LIGHTSCAPE = 0x00000800;
        public const uint DAZZLE = 0x00000900;
        public const uint SOUNDROBJ = 0x00000A00;
        public const uint SHDMESH = 0x00000B00;

        // Mesh sub-chunks
        public const uint MESH_HEADER = 0x0000001F;
        public const uint VERTICES = 0x00000002;
        public const uint VERTICES_2 = 0x00000C00;
        public const uint VERTEX_NORMALS = 0x00000003;
        public const uint TEXCOORDS = 0x00000005;
        public const uint ENB_UV_COORDS = TEXCOORDS;
        public const uint NORMALS_2 = 0x00000C01;
        public const uint MESH_USER_TEXT = 0x0000000C;
        public const uint VERTEX_COLORS = 0x0000000D;
        public const uint ENB_MATERIAL_VERTEX_COLORS = VERTEX_COLORS;
        public const uint VERTEX_INFLUENCES = 0x0000000E;
        public const uint MATERIALS3 = 0x00000015;
        public const uint MATERIAL3 = 0x00000016;
        public const uint MATERIAL3_NAME = 0x00000017;
        public const uint MATERIAL3_INFO = 0x00000018;
        public const uint MATERIAL3_DC_MAP = 0x00000019;
        public const uint MAP3_FILENAME = 0x0000001A;
        public const uint MAP3_INFO = 0x0000001B;
        public const uint MATERIAL3_SI_MAP = 0x0000001E;
        public const uint TRIANGLES = 0x00000020;
        public const uint PER_TRIANGLE_MATERIAL_IDS = 0x00000021;
        public const uint ENB_PER_TRIANGLE_SHADER_INDEX = PER_TRIANGLE_MATERIAL_IDS;
        public const uint VERTEX_SHADE_INDICES = 0x00000022;
        public const uint MATERIAL_INFO = 0x00000028;
        public const uint SHADERS = 0x00000029;
        public const uint VERTEX_MATERIALS = 0x0000002A;
        public const uint VERTEX_MATERIAL = 0x0000002B;
        public const uint VERTEX_MATERIAL_NAME = 0x0000002C;
        public const uint VERTEX_MATERIAL_INFO = 0x0000002D;
        public const uint VERTEX_MAPPER_ARGS0 = 0x0000002E;
        public const uint VERTEX_MAPPER_ARGS1 = 0x0000002F;
        public const uint TEXTURES = 0x00000030;
        public const uint TEXTURE = 0x00000031;
        public const uint TEXTURE_NAME = 0x00000032;
        public const uint TEXTURE_INFO = 0x00000033;
        public const uint MATERIAL_PASS = 0x00000038;
        public const uint VERTEX_MATERIAL_IDS = 0x00000039;
        public const uint SHADER_IDS = 0x0000003A;
        public const uint DCG = 0x0000003B;
        public const uint DIG = 0x0000003C;
        public const uint SCG = 0x0000003E;
        public const uint SHADER_MATERIAL_ID = 0x0000003F;
        public const uint TEXTURE_STAGE = 0x00000048;
        public const uint TEXTURE_IDS = 0x00000049;
        public const uint STAGE_TEXCOORDS = 0x0000004A;
        public const uint PER_FACE_TEXCOORD_IDS = 0x0000004B;
        public const uint SHADER_MATERIALS = 0x00000050;
        public const uint SHADER_MATERIAL = 0x00000051;
        public const uint SHADER_MATERIAL_HEADER = 0x00000052;
        public const uint SHADER_MATERIAL_PROPERTY = 0x00000053;
        public const uint TANGENTS = 0x00000060;
        public const uint BITANGENTS = 0x00000061;
        public const uint PS2_SHADERS = 0x00000080;
        public const uint AABBTREE = 0x00000090;
        public const uint AABBTREE_HEADER = 0x00000091;
        public const uint AABBTREE_POLYINDICES = 0x00000092;
        public const uint AABBTREE_NODES = 0x00000093;

        // Prelit chunks
        public const uint PRELIT_UNLIT = 0x00000023;
        public const uint PRELIT_VERTEX = 0x00000024;
        public const uint PRELIT_LIGHTMAP_MULTI_PASS = 0x00000025;
        public const uint PRELIT_LIGHTMAP_MULTI_TEXTURE = 0x00000026;

        // Hierarchy sub-chunks
        public const uint HIERARCHY_HEADER = 0x00000101;
        public const uint PIVOTS = 0x00000102;
        public const uint PIVOT_FIXUPS = 0x00000103;

        // Animation sub-chunks
        public const uint ANIMATION_HEADER = 0x00000201;
        public const uint ANIMATION_CHANNEL = 0x00000202;
        public const uint BIT_CHANNEL = 0x00000203;

        // Compressed animation sub-chunks
        public const uint COMPRESSED_ANIMATION_HEADER = 0x00000281;
        public const uint COMPRESSED_ANIMATION_CHANNEL = 0x00000282;
        public const uint COMPRESSED_BIT_CHANNEL = 0x00000283;
        public const uint COMPRESSED_ANIMATION_MOTION_CHANNEL = 0x00000284;

        // LOD model sub-chunks
        public const uint LODMODEL_HEADER = 0x00000401;
        public const uint LODMODEL_LOD = 0x00000402;

        // HLOD sub-chunks
        public const uint HLOD_HEADER = 0x00000701;
        public const uint HLOD_LOD_ARRAY = 0x00000702;
        public const uint HLOD_SUB_OBJECT_ARRAY_HEADER = 0x00000703;
        public const uint HLOD_SUB_OBJECT = 0x00000704;
        public const uint HLOD_AGGREGATE_ARRAY = 0x00000705;
        public const uint HLOD_PROXY_ARRAY = 0x00000706;

        // Emitter sub-chunks
        public const uint EMITTER_HEADER = 0x00000501;
        public const uint EMITTER_USER_DATA = 0x00000502;
        public const uint EMITTER_INFO = 0x00000503;
        public const uint EMITTER_INFOV2 = 0x00000504;
        public const uint EMITTER_PROPS = 0x00000505;
        public const uint EMITTER_LINE_PROPERTIES = 0x00000509;
        public const uint EMITTER_ROTATION_KEYFRAMES = 0x0000050A;
        public const uint EMITTER_FRAME_KEYFRAMES = 0x0000050B;
        public const uint EMITTER_BLUR_TIME_KEYFRAMES = 0x0000050C;
        public const uint EMITTER_EXTRA_KEYFRAMES = 0x0000050D;

        // Aggregate sub-chunks
        public const uint AGGREGATE_HEADER = 0x00000601;
        public const uint AGGREGATE_INFO = 0x00000602;
        public const uint AGGREGATE_CLASS_INFO = 0x00000604;

        // Collision box sub-chunks
        public const uint BOX_NAME = 0x00000741;
        public const uint BOX_COLOR = 0x00000742;
        public const uint BOX_CENTER = 0x00000743;
        public const uint BOX_EXTENT = 0x00000744;

        // ShdMesh sub-chunks
        public const uint SHDMESH_NAME = 0x00000B01;
        public const uint SHDMESH_HEADER = 0x00000B02;
        public const uint SHDMESH_USER_TEXT = 0x00000B03;
        public const uint SHDMESH_SUBMESH = 0x00000B20;
        public const uint SHDMESH_SUBMESH_HEADER = 0x00000B21;
        public const uint SHDMESH_SHADER = 0x00000B40;
        public const uint SHDMESH_VERTICES = 0x00000B43;
        public const uint SHDMESH_VERTEX_NORMALS = 0x00000B44;
        public const uint SHDMESH_TRIANGLES = 0x00000B45;
        public const uint SHDMESH_VERTEX_SHADE_INDICES = 0x00000B46;
        public const uint SHDMESH_UV0 = 0x00000B47;
        public const uint SHDMESH_UV1 = 0x00000B48;
        public const uint SHDMESH_TANGENTS = 0x00000B49;
        public const uint SHDMESH_BITANGENTS = 0x00000B4A;
        public const uint SHDMESH_TANGENT_CROSS = 0x00000B4B;

        // Material pass sub-chunks
        public const uint TX_COORDS = 0x00000048;

        // EnB-specific
        public const uint ENB_SHADER_CONTAINER = 0x00000800;
        public const uint ENB_SHADER_ARRAY = 0x00000808;
        public const uint ENB_SHADER_PARAM = 0x00000810;
        public const uint ENB_SHADER_PARAM_EXT = 0x00000811;
    }
}
