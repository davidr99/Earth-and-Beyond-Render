using W3DImporter.IO;
using W3DImporter.Structs.Animation;

namespace W3DImporter.Parsers
{
    public static class W3DAnimationParser
    {
        public static W3DAnimation Read(W3DBinaryReader reader, W3DChunkReader chunks, long chunkEnd)
        {
            var anim = new W3DAnimation();

            foreach (var chunk in chunks.EnumerateChunks(chunkEnd))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.ANIMATION_HEADER:
                        anim.Header = new W3DAnimationHeader
                        {
                            Version = reader.ReadUInt32(),
                            Name = reader.ReadFixedString(),
                            HierarchyName = reader.ReadFixedString(),
                            NumFrames = reader.ReadInt32(),
                            FrameRate = reader.ReadUInt32() // on disk u32, stored as float in struct
                        };
                        break;
                    case W3DChunkTypes.ANIMATION_CHANNEL:
                        anim.Channels.Add(ReadChannel(reader, chunk.ChunkEnd));
                        break;
                    case W3DChunkTypes.BIT_CHANNEL:
                        anim.BitChannels.Add(ReadBitChannel(reader, chunk.ChunkEnd));
                        break;
                    default:
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return anim;
        }

        private static W3DAnimationChannel ReadChannel(W3DBinaryReader reader, long end)
        {
            var ch = new W3DAnimationChannel();
            ch.FirstFrame = reader.ReadUInt16();
            ch.LastFrame = reader.ReadUInt16();
            ch.VectorLen = reader.ReadUInt16();
            ch.ChannelType = reader.ReadUInt16();
            ch.Pivot = reader.ReadUInt16();
            reader.Skip(2); // padding

            // (last - first + 1) * vectorLen floats; some files carry trailing pad bytes and
            // one (SG01-IDL.W3D) declares more frames than the chunk holds.
            long expected = (long)System.Math.Max(0, ch.LastFrame - ch.FirstFrame + 1) * System.Math.Max(1, ch.VectorLen);
            for (long i = 0; i < expected && reader.Position + 4 <= end; i++)
                ch.Data.Add(reader.ReadFloat());

            return ch;
        }

        // BIT_CHANNEL (0x203): 9-byte header, then a PACKED bitstream (LSB-first),
        // one bit per frame: bit(f) = (Bits[(f-First)>>3] >> ((f-First)&7)) & 1.
        private static W3DAnimationBitChannel ReadBitChannel(W3DBinaryReader reader, long end)
        {
            var ch = new W3DAnimationBitChannel();
            ch.FirstFrame = reader.ReadUInt16();
            ch.LastFrame = reader.ReadUInt16();
            ch.ChannelType = reader.ReadUInt16();
            ch.Pivot = reader.ReadUInt16();
            ch.DefaultValue = reader.ReadByte() != 0;

            int frameCount = ch.LastFrame - ch.FirstFrame + 1;
            if (frameCount <= 0)
                return ch;

            int byteCount = (int)System.Math.Min(end - reader.Position, (frameCount + 7) >> 3);
            var packed = byteCount > 0 ? reader.ReadBytes(byteCount) : System.Array.Empty<byte>();

            for (int f = 0; f < frameCount; f++)
            {
                int byteIdx = f >> 3;
                bool value = byteIdx < packed.Length
                    ? ((packed[byteIdx] >> (f & 7)) & 1) != 0
                    : ch.DefaultValue;
                ch.Data.Add(value);
            }

            return ch;
        }
    }

    public static class W3DCompressedAnimationParser
    {
        public static W3DCompressedAnimation Read(W3DBinaryReader reader, W3DChunkReader chunks, long chunkEnd)
        {
            var anim = new W3DCompressedAnimation();

            foreach (var chunk in chunks.EnumerateChunks(chunkEnd))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.COMPRESSED_ANIMATION_HEADER:
                        anim.Header = new W3DCompressedAnimationHeader
                        {
                            Version = reader.ReadUInt32(),
                            Name = reader.ReadFixedString(),
                            HierarchyName = reader.ReadFixedString(),
                            NumFrames = reader.ReadInt32(),
                            FrameRate = reader.ReadUInt16(), // u16 on disk, stored as float
                            Flavor = reader.ReadUInt16()
                        };
                        break;
                    case W3DChunkTypes.COMPRESSED_ANIMATION_CHANNEL:
                        if (anim.Header != null)
                        {
                            var channel = anim.Header.Flavor == 0
                                ? ReadTimeCodedChannel(reader, chunk.ChunkEnd)
                                : anim.Header.Flavor == 1
                                    ? ReadAdaptiveDeltaChannel(reader, chunk.ChunkEnd)
                                    : null;
                            if (channel != null)
                                anim.TimeCodedChannels.Add(channel);
                        }
                        break;
                    case W3DChunkTypes.COMPRESSED_BIT_CHANNEL:
                    {
                        var bitChannel = ReadCompressedBitChannel(reader, chunk.ChunkEnd);
                        if (bitChannel != null)
                            anim.CompressedBitChannels.Add(bitChannel);
                        break;
                    }
                    case W3DChunkTypes.COMPRESSED_ANIMATION_MOTION_CHANNEL:
                    {
                        var motion = ReadMotionChannel(reader, chunk.ChunkEnd);
                        if (motion != null)
                            anim.MotionChannels.Add(motion);
                        break;
                    }
                    default:
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return anim;
        }

        // COMPRESSED_BIT_CHANNEL (0x283).
        // Header: u32 num_time_codes, u16 pivot, u8 type, u8 default_value
        // Then num_time_codes × u32 (high bit = value, low 31 bits = frame).
        private static W3DCompressedBitChannel ReadCompressedBitChannel(W3DBinaryReader reader, long end)
        {
            const int headerBytes = 8;
            if (end - reader.Position < headerBytes)
                return null;

            int numTimeCodes = reader.ReadInt32();
            int pivot = reader.ReadUInt16();
            int channelType = reader.ReadByte();
            byte defaultByte = reader.ReadByte();

            if (numTimeCodes < 0)
                return null;

            long bytesAvail = end - reader.Position;
            if ((long)numTimeCodes * 4L > bytesAvail)
                numTimeCodes = (int)(bytesAvail / 4L);

            var ch = new W3DCompressedBitChannel
            {
                ChannelType = channelType,
                Pivot = pivot,
                DefaultValue = defaultByte != 0
            };

            for (int i = 0; i < numTimeCodes; i++)
            {
                uint raw = reader.ReadUInt32();
                bool value = (raw & 0x80000000u) != 0;
                int frame = (int)(raw & 0x7FFFFFFFu);
                ch.TimeCodes.Add(new W3DCompressedBitDatum { TimeCode = frame, Value = value });
            }
            return ch;
        }

        // COMPRESSED_ANIMATION_MOTION_CHANNEL (0x284).
        // Header: u8 zero, u8 delta_type, u8 vector_len, u8 type,
        //         i16 num_time_codes, i16 pivot.
        // For delta_type == 0 (time-coded): num_time_codes × i16 timecode,
        // optional 2-byte align if num_time_codes is odd, then
        // num_time_codes * vector_len floats of data.
        // delta_type > 0 = adaptive delta — parsing skipped (rare in E&B);
        // the channel is recorded with AdaptiveDeltaSkipped = true so callers
        // know data isn't usable.
        private static W3DMotionChannel ReadMotionChannel(W3DBinaryReader reader, long end)
        {
            const int headerBytes = 8;
            if (end - reader.Position < headerBytes)
                return null;

            reader.ReadByte();              // leading zero per format
            int deltaType = reader.ReadByte();
            int vectorLen = reader.ReadByte();
            int channelType = reader.ReadByte();
            int numTimeCodes = reader.ReadInt16();
            int pivot = reader.ReadInt16();

            var motion = new W3DMotionChannel
            {
                DeltaType = deltaType,
                VectorLen = vectorLen,
                ChannelType = channelType,
                NumTimeCodes = numTimeCodes,
                Pivot = pivot
            };

            if (numTimeCodes < 0 || vectorLen <= 0)
                return motion;

            if (deltaType != 0)
            {
                // Adaptive delta: scale + initial value + delta blocks.
                // We don't decode the delta chain; mark it so the bone is
                // simply animated by zero/identity for that channel rather
                // than producing garbage.
                motion.AdaptiveDeltaSkipped = true;
                return motion;
            }

            long timeCodeBytes = (long)numTimeCodes * 2L;
            long alignmentBytes = (numTimeCodes & 1) != 0 ? 2L : 0L;
            long dataBytes = (long)numTimeCodes * vectorLen * 4L;
            long needed = timeCodeBytes + alignmentBytes + dataBytes;
            if (needed > end - reader.Position)
                return motion;  // truncated; bail without producing partial garbage

            for (int i = 0; i < numTimeCodes; i++)
                motion.TimeCodes.Add(reader.ReadInt16());

            if ((numTimeCodes & 1) != 0)
                reader.Skip(2);

            int totalFloats = numTimeCodes * vectorLen;
            for (int i = 0; i < totalFloats; i++)
                motion.Data.Add(reader.ReadFloat());

            return motion;
        }

        // COMPRESSED_ANIMATION_CHANNEL (0x282), flavor 0 = TimeCoded (W3D_FORMAT.md §10.2):
        //   u32 NumTimeCodes; u16 Pivot; u8 VectorLen; u8 ChannelType;
        //   then NumTimeCodes interleaved packets { u32 TimeCode; f32 Value[VectorLen] }.
        // TimeCode low 31 bits = frame number; bit 31 = "binary movement" (hold the
        // previous key up to this one instead of interpolating).
        private static W3DTimeCodedChannel ReadTimeCodedChannel(W3DBinaryReader reader, long end)
        {
            const int channelHeaderSize = 8;
            if (end - reader.Position < channelHeaderSize)
                return null;

            var ch = new W3DTimeCodedChannel();
            int numTimeCodes = reader.ReadInt32();
            ch.Pivot = reader.ReadUInt16();
            ch.VectorLen = reader.ReadByte();
            ch.ChannelType = reader.ReadByte();

            if (numTimeCodes <= 0 || ch.VectorLen <= 0)
                return null;

            long packetBytes = (long)numTimeCodes * (1 + ch.VectorLen) * 4L;
            if (packetBytes > end - reader.Position)
                return null;

            for (int i = 0; i < numTimeCodes; i++)
            {
                uint raw = reader.ReadUInt32();
                ch.TimeCodes.Add((int)(raw & 0x7FFFFFFFu));
                ch.NoInterp.Add((raw & 0x80000000u) != 0);
                for (int c = 0; c < ch.VectorLen; c++)
                    ch.Data.Add(reader.ReadFloat());
            }

            return ch;
        }

        // COMPRESSED_ANIMATION_CHANNEL (0x282), flavor 1 = AdaptiveDelta (W3D_FORMAT.md §10.2):
        //   u32 NumFrames; u16 Pivot; u8 VectorLen; u8 ChannelType; f32 Scale;
        //   f32 Initial[VectorLen];
        //   ceil((NumFrames-1)/16) groups × VectorLen blocks of 9 bytes:
        //     { u8 FilterTableIndex; u8 Nibbles[8] }  — 16 signed 4-bit deltas, low nibble first.
        // Decoded here into one key per frame so downstream handling matches TimeCoded.
        private static W3DTimeCodedChannel ReadAdaptiveDeltaChannel(W3DBinaryReader reader, long end)
        {
            const int channelHeaderSize = 12;
            if (end - reader.Position < channelHeaderSize)
                return null;

            int numFrames = reader.ReadInt32();
            int pivot = reader.ReadUInt16();
            int vectorLen = reader.ReadByte();
            int channelType = reader.ReadByte();
            float scale = reader.ReadFloat();

            if (numFrames <= 0 || vectorLen <= 0)
                return null;

            var data = reader.ReadBytes((int)(end - reader.Position));
            int initialBytes = vectorLen * 4;
            if (data.Length < initialBytes)
                return null;

            var ch = new W3DTimeCodedChannel
            {
                Pivot = pivot,
                VectorLen = vectorLen,
                ChannelType = channelType
            };

            var value = new float[vectorLen];
            for (int c = 0; c < vectorLen; c++)
                value[c] = System.BitConverter.ToSingle(data, c * 4);

            AppendAdaptiveDeltaFrame(ch, 0, value);

            for (int f = 1; f < numFrames; f++)
            {
                int group = (f - 1) >> 4;
                int nibbleIdx = (f - 1) & 15;

                bool truncated = false;
                for (int c = 0; c < vectorLen; c++)
                {
                    int blockOffset = initialBytes + (group * vectorLen + c) * 9;
                    if (blockOffset + 9 > data.Length)
                    {
                        truncated = true;
                        break;
                    }

                    byte tableIndex = data[blockOffset];
                    byte packed = data[blockOffset + 1 + (nibbleIdx >> 1)];
                    int nibble = (nibbleIdx & 1) != 0 ? (packed >> 4) & 0xF : packed & 0xF;
                    if ((nibble & 8) != 0)
                        nibble |= ~0xF; // sign-extend the 4-bit delta

                    value[c] += nibble * AdaptiveDeltaFilterTable[tableIndex] * scale;
                }

                if (truncated)
                    break;

                AppendAdaptiveDeltaFrame(ch, f, value);
            }

            return ch;
        }

        private static void AppendAdaptiveDeltaFrame(W3DTimeCodedChannel ch, int frame, float[] value)
        {
            ch.TimeCodes.Add(frame);
            ch.NoInterp.Add(false);
            for (int c = 0; c < value.Length; c++)
                ch.Data.Add(value[c]);
        }

        // Entries 0–15: 10^(i−8); entries 16–255: 1 − sin((i−16) × (90/240)°).
        // Matches the client's static table at 0xBC5DA4 (W3D_FORMAT.md §10.2).
        private static readonly float[] AdaptiveDeltaFilterTable = BuildAdaptiveDeltaFilterTable();

        private static float[] BuildAdaptiveDeltaFilterTable()
        {
            var table = new float[256];
            for (int i = 0; i < 16; i++)
                table[i] = (float)System.Math.Pow(10.0, i - 8);
            for (int i = 16; i < 256; i++)
                table[i] = (float)(1.0 - System.Math.Sin((i - 16) * 0.375 * System.Math.PI / 180.0));
            return table;
        }
    }
}
