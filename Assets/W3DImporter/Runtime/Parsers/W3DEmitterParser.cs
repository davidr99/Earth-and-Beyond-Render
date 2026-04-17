using W3DImporter.IO;
using W3DImporter.Structs.Emitter;

namespace W3DImporter.Parsers
{
    public static class W3DEmitterParser
    {
        public static W3DEmitter Read(W3DBinaryReader reader, W3DChunkReader chunks, long chunkEnd)
        {
            var emitter = new W3DEmitter();

            foreach (var chunk in chunks.EnumerateChunks(chunkEnd))
            {
                switch (chunk.ChunkType)
                {
                    case W3DChunkTypes.EMITTER_HEADER:
                        emitter.Header = new W3DEmitterHeader
                        {
                            Version = reader.ReadUInt32(),
                            Name = reader.ReadFixedString()
                        };
                        break;
                    case W3DChunkTypes.EMITTER_USER_DATA:
                        uint type = reader.ReadUInt32();
                        int strLen = reader.ReadInt32();
                        if (strLen > 0)
                            emitter.UserData = reader.ReadString(strLen);
                        break;
                    case W3DChunkTypes.EMITTER_INFO:
                        emitter.Info = ReadEmitterInfo(reader);
                        break;
                    case W3DChunkTypes.EMITTER_INFOV2:
                        emitter.InfoV2 = ReadEmitterInfoV2(reader);
                        break;
                    case W3DChunkTypes.EMITTER_PROPS:
                        emitter.Props = ReadEmitterProps(reader);
                        break;
                    default:
                        // Skip line properties, keyframes, etc. for now
                        break;
                }
                reader.Seek(chunk.ChunkEnd);
            }
            return emitter;
        }

        private static W3DEmitterInfo ReadEmitterInfo(W3DBinaryReader reader)
        {
            var info = new W3DEmitterInfo();
            info.TextureFilename = reader.ReadString(260);
            info.StartSize = reader.ReadFloat();
            info.EndSize = reader.ReadFloat();
            info.Lifetime = reader.ReadFloat();
            info.EmissionRate = reader.ReadFloat();
            info.MaxEmissions = reader.ReadFloat();
            info.VelocityRandom = reader.ReadFloat();
            info.PositionRandom = reader.ReadFloat();
            info.FadeTime = reader.ReadFloat();
            info.Gravity = reader.ReadFloat();
            info.Elasticity = reader.ReadFloat();
            info.Velocity = reader.ReadVector3();
            info.Acceleration = reader.ReadVector3();
            info.StartColor = reader.ReadRGBA();
            info.EndColor = reader.ReadRGBA();
            return info;
        }

        private static W3DEmitterInfoV2 ReadEmitterInfoV2(W3DBinaryReader reader)
        {
            var v2 = new W3DEmitterInfoV2();
            v2.BurstSize = reader.ReadUInt32();
            reader.Skip(32); // creation volume
            reader.Skip(32); // vel random
            v2.OutwardVel = reader.ReadFloat();
            v2.VelInherit = reader.ReadFloat();
            reader.Skip(16); // shader
            v2.RenderMode = reader.ReadUInt32();
            v2.FrameMode = reader.ReadUInt32();
            reader.Skip(24); // reserved
            return v2;
        }

        private static W3DEmitterProps ReadEmitterProps(W3DBinaryReader reader)
        {
            var props = new W3DEmitterProps();
            int colorCount = reader.ReadInt32();
            int opacityCount = reader.ReadInt32();
            int sizeCount = reader.ReadInt32();
            props.ColorRandom = reader.ReadRGBA();
            props.OpacityRandom = reader.ReadFloat();
            props.SizeRandom = reader.ReadFloat();
            reader.Skip(16); // reserved

            for (int i = 0; i < colorCount; i++)
            {
                props.ColorKeyframes.Add(new W3DColorKeyframe
                {
                    Time = reader.ReadFloat(),
                    Color = reader.ReadRGBA()
                });
            }

            for (int i = 0; i < opacityCount; i++)
            {
                props.OpacityKeyframes.Add(new W3DFloatKeyframe
                {
                    Time = reader.ReadFloat(),
                    Value = reader.ReadFloat()
                });
            }

            for (int i = 0; i < sizeCount; i++)
            {
                props.SizeKeyframes.Add(new W3DFloatKeyframe
                {
                    Time = reader.ReadFloat(),
                    Value = reader.ReadFloat()
                });
            }

            return props;
        }
    }
}
