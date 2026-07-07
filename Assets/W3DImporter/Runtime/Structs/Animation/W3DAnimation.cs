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
        public int ChannelType; // 0 = visibility (only type kept by the client)
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

    // Compressed channel (0x282) decoded to keyframes. On disk (flavor 0) the data is
    // interleaved packets { u32 TimeCode; f32 Value[VectorLen] }; bit 31 of a key's
    // timecode means "hold the PREVIOUS key until this one" (no interpolation).
    // Channel types match the uncompressed family: 0/1/2 = X/Y/Z translation,
    // 6 = quaternion (VectorLen = 4, xyzw per key). Flavor 1 (AdaptiveDelta) is
    // decoded into one key per frame with NoInterp = false.
    public class W3DTimeCodedChannel
    {
        public int ChannelType;
        public int VectorLen;
        public int Pivot;
        public List<int> TimeCodes = new List<int>();   // frame numbers (bit 31 stripped)
        public List<bool> NoInterp = new List<bool>();  // per key: hold previous key's value
        public List<float> Data = new List<float>();    // TimeCodes.Count * VectorLen floats
    }

    /// <summary>
    /// W3D_CHUNK_COMPRESSED_BIT_CHANNEL (0x283). Layout differs from the
    /// uncompressed BitChannel: each time-code entry is a single u32 whose
    /// high bit (0x80000000) IS the boolean value at that time, with the
    /// low 31 bits being the actual frame number. Default value is the
    /// implicit value before the first time-code.
    /// </summary>
    public class W3DCompressedBitChannel
    {
        public int ChannelType;     // 0 = visibility (only type kept by the client)
        public int Pivot;
        public bool DefaultValue;
        public List<W3DCompressedBitDatum> TimeCodes = new List<W3DCompressedBitDatum>();
    }

    public struct W3DCompressedBitDatum
    {
        public int TimeCode;
        public bool Value;
    }

    /// <summary>
    /// W3D_CHUNK_COMPRESSED_ANIMATION_MOTION_CHANNEL (0x284). Used for
    /// per-bone motion channels in compressed animations. Two delta types
    /// exist: 0 = time-coded (short timecodes + values), >0 = adaptive
    /// delta (rarely used in E&B). We parse the time-coded form only;
    /// adaptive delta is skipped with a flag.
    /// </summary>
    public class W3DMotionChannel
    {
        public int DeltaType;        // 0 = time-coded; >0 = adaptive delta (unsupported)
        public int VectorLen;
        public int ChannelType;
        public int NumTimeCodes;
        public int Pivot;
        public List<int> TimeCodes = new List<int>();   // empty for adaptive-delta
        public List<float> Data = new List<float>();    // length = NumTimeCodes * VectorLen
        public bool AdaptiveDeltaSkipped;               // true if we couldn't parse it
    }

    public class W3DCompressedAnimation
    {
        public W3DCompressedAnimationHeader Header;
        public List<W3DTimeCodedChannel> TimeCodedChannels = new List<W3DTimeCodedChannel>();
        public List<W3DAnimationBitChannel> BitChannels = new List<W3DAnimationBitChannel>();
        public List<W3DCompressedBitChannel> CompressedBitChannels = new List<W3DCompressedBitChannel>();
        public List<W3DMotionChannel> MotionChannels = new List<W3DMotionChannel>();
    }
}
