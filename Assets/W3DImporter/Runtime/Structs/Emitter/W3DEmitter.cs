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

    public class W3DEmitter
    {
        public W3DEmitterHeader Header;
        public W3DEmitterInfo Info;
        public W3DEmitterInfoV2 InfoV2;
        public W3DEmitterProps Props;
        public W3DEmitterRotationData RotationData;
        public W3DEmitterFrameData FrameData;
        public string UserData = "";

        public string Name => Header?.Name ?? "Emitter";
    }
}
