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
                            FrameRate = reader.ReadFloat()
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
                            Flavor = reader.ReadUInt16(),
                            FrameRate = reader.ReadFloat()
                        };
                        break;
                    case W3DChunkTypes.COMPRESSED_ANIMATION_CHANNEL:
                        anim.TimeCodedChannels.Add(ReadTimeCodedChannel(reader, chunk.ChunkEnd));
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
            var ch = new W3DTimeCodedChannel();
            int numTimeCodes = reader.ReadInt32();
            ch.Pivot = reader.ReadUInt16();
            ch.VectorLen = reader.ReadByte();
            ch.ChannelType = reader.ReadByte();

            for (int i = 0; i < numTimeCodes; i++)
            {
                var datum = new W3DTimeCodedDatum();
                uint rawTimeCode = reader.ReadUInt32();
                datum.NonInterpolated = (rawTimeCode & 0x80000000) != 0;
                datum.TimeCode = rawTimeCode & 0x7FFFFFFF;
                datum.Value = reader.ReadFloat();
                ch.Data.Add(datum);
            }

            return ch;
        }
    }
}
