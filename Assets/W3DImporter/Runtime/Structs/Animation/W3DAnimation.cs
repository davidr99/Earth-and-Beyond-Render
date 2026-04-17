using System.Collections.Generic;
using UnityEngine;

namespace W3DImporter.Structs.Animation
{
    public class W3DAnimationHeader
    {
        public uint Version;
        public string Name = "";
        public string HierarchyName = "";
        public int NumFrames;
        public float FrameRate;
    }

    public class W3DAnimationChannel
    {
        public int ChannelType; // 0=X, 1=Y, 2=Z, 6=Quaternion
        public int VectorLen;
        public int Pivot;
        public int FirstFrame;
        public int LastFrame;
        public List<float> Data = new List<float>();
    }

    public class W3DAnimationBitChannel
    {
        public int ChannelType; // typically 15 = visibility
        public int Pivot;
        public int FirstFrame;
        public int LastFrame;
        public bool DefaultValue;
        public List<bool> Data = new List<bool>();
    }

    public class W3DAnimation
    {
        public W3DAnimationHeader Header;
        public List<W3DAnimationChannel> Channels = new List<W3DAnimationChannel>();
        public List<W3DAnimationBitChannel> BitChannels = new List<W3DAnimationBitChannel>();
    }

    public class W3DCompressedAnimationHeader
    {
        public uint Version;
        public string Name = "";
        public string HierarchyName = "";
        public int NumFrames;
        public ushort Flavor; // 0=TimeCoded, 1=AdaptiveDelta
        public float FrameRate;
    }

    public class W3DTimeCodedDatum
    {
        public uint TimeCode;
        public float Value;
        public bool NonInterpolated;
    }

    public class W3DTimeCodedChannel
    {
        public int ChannelType;
        public int VectorLen;
        public int Pivot;
        public List<W3DTimeCodedDatum> Data = new List<W3DTimeCodedDatum>();
    }

    public class W3DCompressedAnimation
    {
        public W3DCompressedAnimationHeader Header;
        public List<W3DTimeCodedChannel> TimeCodedChannels = new List<W3DTimeCodedChannel>();
        public List<W3DAnimationBitChannel> BitChannels = new List<W3DAnimationBitChannel>();
    }
}
