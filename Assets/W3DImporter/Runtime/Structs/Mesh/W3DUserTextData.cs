using System.Collections.Generic;

namespace W3DImporter.Structs.Mesh
{
    public sealed class W3DEffectPassDescription
    {
        public int EffectPassType;
        public string Shader = "";
        public string Texture = "";
        public bool? TwoSided;
        public bool? Hidden;
        public float? CycleTime;
        public float? StartVCoord;
        public float? EndVCoord;
    }

    public sealed class W3DSurfaceDescription
    {
        public int? LightmapChunk;
        public string ColorMap = "";
        public bool FlipTga;
        public string DetailMap = "";
        public float? XDetailScale;
        public float? YDetailScale;
    }

    public sealed class W3DUserTextData
    {
        public readonly Dictionary<string, Dictionary<string, string>> Sections =
            new Dictionary<string, Dictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase);

        public W3DEffectPassDescription EffectPass;
        public W3DSurfaceDescription Surface;
    }
}
