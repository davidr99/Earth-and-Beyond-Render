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

            while (reader.Position < end)
                ch.Data.Add(reader.ReadFloat());

            return ch;
        }

        private static W3DAnimationBitChannel ReadBitChannel(W3DBinaryReader reader, long end)
        {
            var ch = new W3DAnimationBitChannel();
            ch.FirstFrame = reader.ReadUInt16();
            ch.LastFrame = reader.ReadUInt16();
            ch.ChannelType = reader.ReadUInt16();
            ch.Pivot = reader.ReadUInt16();
            ch.DefaultValue = reader.ReadByte() != 0;
            reader.Skip(1); // padding

            while (reader.Position < end)
                ch.Data.Add(reader.ReadByte() != 0);

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
                        if (anim.Header != null && anim.Header.Flavor == 0)
                        {
                            var channel = ReadTimeCodedChannel(reader, chunk.ChunkEnd);
                            if (channel != null)
                                anim.TimeCodedChannels.Add(channel);
                        }
                        break;
                    case W3DChunkTypes.COMPRESSED_BIT_CHANNEL:
                        // Similar to regular bit channel
                        break;
                    default:
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return anim;
        }

        private static W3DTimeCodedChannel ReadTimeCodedChannel(W3DBinaryReader reader, long end)
        {
            const int channelHeaderSize = 12;
            if (end - reader.Position < channelHeaderSize)
                return null;

            // Layout: u32 numTimeCodes, u16 pivot, u16 vectorLen, u16 flags (channel type),
            // u16 padding, then u16[numTimeCodes] timecodes (2-byte aligned),
            // then float[numTimeCodes * vectorLen] data.
            var ch = new W3DTimeCodedChannel();
            int numTimeCodes = reader.ReadInt32();
            ch.Pivot = reader.ReadUInt16();
            ch.VectorLen = reader.ReadUInt16();
            ch.ChannelType = reader.ReadUInt16();
            reader.Skip(2); // padding/reserved

            if (numTimeCodes < 0 || ch.VectorLen <= 0)
                return null;

            long remaining = end - reader.Position;
            long timeCodeBytes = (long)numTimeCodes * 2L;
            long alignmentBytes = (numTimeCodes & 1) != 0 ? 2L : 0L;
            long dataBytes = (long)numTimeCodes * ch.VectorLen * 4L;
            long totalBytes = timeCodeBytes + alignmentBytes + dataBytes;

            if (totalBytes < 0 || totalBytes > remaining)
                return null;

            for (int i = 0; i < numTimeCodes; i++)
                ch.TimeCodes.Add(reader.ReadUInt16());

            // Pad timecode array to 4-byte boundary so floats are aligned.
            if ((numTimeCodes & 1) != 0)
                reader.Skip(2);

            int numValues = numTimeCodes * ch.VectorLen;
            for (int i = 0; i < numValues; i++)
                ch.Data.Add(reader.ReadFloat());

            return ch;
        }
    }
}
