using System.Collections.Generic;
using UnityEngine;

namespace W3DImporter.Structs.Emitter
{
    public class W3DEmitterHeader
    {
        public uint Version;
        public string Name = "";
    }

    public class W3DEmitterInfo
    {
        public string TextureFilename = "";
        public float StartSize;
        public float EndSize;
        public float Lifetime;
        public float EmissionRate;
        public float MaxEmissions;
        public float VelocityRandom;
        public float PositionRandom;
        public float FadeTime;
        public float Gravity;
        public float Elasticity;
        public Vector3 Velocity;
        public Vector3 Acceleration;
        public Color32 StartColor;
        public Color32 EndColor;
    }

    public class W3DEmitterInfoV2
    {
        public uint BurstSize;
        public float OutwardVel;
        public float VelInherit;
        public uint RenderMode;
        public uint FrameMode;
    }

    public class W3DColorKeyframe
    {
        public float Time;
        public Color32 Color;
    }

    public class W3DFloatKeyframe
    {
        public float Time;
        public float Value;
    }

    public class W3DEmitterRotationKeyframe
    {
        public float Time;
        public float Rotation;
    }

    public class W3DEmitterFrameKeyframe
    {
        public float Time;
        public float Frame;
    }

    public class W3DEmitterRotationData
    {
        public float Random;
        public float OrientationRandom;
        public List<W3DEmitterRotationKeyframe> Keyframes = new List<W3DEmitterRotationKeyframe>();
    }

    public class W3DEmitterFrameData
    {
        public float Random;
        public List<W3DEmitterFrameKeyframe> Keyframes = new List<W3DEmitterFrameKeyframe>();
    }

    public class W3DEmitterProps
    {
        public List<W3DColorKeyframe> ColorKeyframes = new List<W3DColorKeyframe>();
        public List<W3DFloatKeyframe> OpacityKeyframes = new List<W3DFloatKeyframe>();
        public List<W3DFloatKeyframe> SizeKeyframes = new List<W3DFloatKeyframe>();
        public Color32 ColorRandom;
        public float OpacityRandom;
        public float SizeRandom;
    }

    /// <summary>
    /// EmitterLineProperties — chunk 0x509. Describes "line emitter" config
    /// (W3D's beam/laser/trail emitters). Unsupported by Unity's particle
    /// system as a unit, but parsing it lets the importer surface the data
    /// for any custom downstream processing instead of silently dropping it.
    /// 64-byte struct on disk.
    /// </summary>
    public class W3DEmitterLineProperties
    {
        public uint Flags;
        public uint SubdivisionLevel;
        public float NoiseAmplitude;
        public float MergeAbortFactor;
        public float TextureTileFactor;
        public float UPerSec;
        public float VPerSec;
    }

    /// <summary>
    /// EmitterBlurTimeKeyframes — chunk 0x50C. Per-particle motion-blur
    /// time keyframes. Layout: u32 count + f32 random + 4 bytes reserved
    /// + count × {f32 time, f32 value}.
    /// </summary>
    public class W3DEmitterBlurTimeData
    {
        public float Random;
        public List<W3DFloatKeyframe> Keyframes = new List<W3DFloatKeyframe>();
    }

    /// <summary>
    /// EmitterExtraKeyframes — chunk 0x50D, EnB-specific. Same shape as the
    /// other keyframe chunks (count + random + 8-byte pad + keys), purpose
    /// undocumented but surfacing the data is valuable for E&B content
    /// inspection. The on-disk pad is 8 bytes here, not 4 like blur-time.
    /// </summary>
    public class W3DEmitterExtraData
    {
        public float Random;
        public List<W3DFloatKeyframe> Keyframes = new List<W3DFloatKeyframe>();
    }

    public class W3DEmitter
    {
        public W3DEmitterHeader Header;
        public W3DEmitterInfo Info;
        public W3DEmitterInfoV2 InfoV2;
        public W3DEmitterProps Props;
        public W3DEmitterRotationData RotationData;
        public W3DEmitterFrameData FrameData;
        public W3DEmitterLineProperties LineProperties;
        public W3DEmitterBlurTimeData BlurTimeData;
        public W3DEmitterExtraData ExtraData;
        public string UserData = "";

        public string Name => Header?.Name ?? "Emitter";
    }
}
