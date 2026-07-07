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
                    case W3DChunkTypes.EMITTER_ROTATION_KEYFRAMES:
                        emitter.RotationData = ReadRotationKeyframes(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.EMITTER_FRAME_KEYFRAMES:
                        emitter.FrameData = ReadFrameKeyframes(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.EMITTER_LINE_PROPERTIES:
                        emitter.LineProperties = ReadLineProperties(reader);
                        break;
                    case W3DChunkTypes.EMITTER_BLUR_TIME_KEYFRAMES:
                        emitter.BlurTimeData = ReadBlurTimeKeyframes(reader, chunk.ChunkEnd);
                        break;
                    case W3DChunkTypes.EMITTER_EXTRA_KEYFRAMES:
                        emitter.ExtraData = ReadExtraKeyframes(reader, chunk.ChunkEnd);
                        break;
                    default:
                        // Anything else is genuinely unrecognized.
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

        private static W3DEmitterRotationData ReadRotationKeyframes(W3DBinaryReader reader, long end)
        {
            var keyframeCount = reader.ReadUInt32();
            var data = new W3DEmitterRotationData
            {
                Random = reader.ReadFloat(),
                OrientationRandom = reader.ReadFloat()
            };
            reader.Skip(4); // reserved

            int maxKeysFromChunk = (int)((end - reader.Position) / 8);
            int keysToRead = keyframeCount > 0 ? System.Math.Min((int)keyframeCount + 1, maxKeysFromChunk) : maxKeysFromChunk;
            for (int i = 0; i < keysToRead; i++)
            {
                data.Keyframes.Add(new W3DEmitterRotationKeyframe
                {
                    Time = reader.ReadFloat(),
                    Rotation = reader.ReadFloat()
                });
            }

            return data;
        }

        private static W3DEmitterFrameData ReadFrameKeyframes(W3DBinaryReader reader, long end)
        {
            var keyframeCount = reader.ReadUInt32();
            var data = new W3DEmitterFrameData
            {
                Random = reader.ReadFloat()
            };
            reader.Skip(8); // reserved

            int maxKeysFromChunk = (int)((end - reader.Position) / 8);
            int keysToRead = keyframeCount > 0 ? System.Math.Min((int)keyframeCount + 1, maxKeysFromChunk) : maxKeysFromChunk;
            for (int i = 0; i < keysToRead; i++)
            {
                data.Keyframes.Add(new W3DEmitterFrameKeyframe
                {
                    Time = reader.ReadFloat(),
                    Frame = reader.ReadFloat()
                });
            }

            return data;
        }

        // EMITTER_LINE_PROPERTIES (0x509) — fixed 64-byte struct.
        // Layout (per io_mesh_w3d/w3d/structs/emitter.py EmitterLineProperties.read):
        //   u32 flags, u32 subdivision_level,
        //   f32 noise_amplitude, f32 merge_abort_factor, f32 texture_tile_factor,
        //   f32 u_per_sec, f32 v_per_sec, then 36 bytes reserved.
        private static W3DEmitterLineProperties ReadLineProperties(W3DBinaryReader reader)
        {
            var lp = new W3DEmitterLineProperties
            {
                Flags = reader.ReadUInt32(),
                SubdivisionLevel = reader.ReadUInt32(),
                NoiseAmplitude = reader.ReadFloat(),
                MergeAbortFactor = reader.ReadFloat(),
                TextureTileFactor = reader.ReadFloat(),
                UPerSec = reader.ReadFloat(),
                VPerSec = reader.ReadFloat()
            };
            reader.Skip(36); // reserved
            return lp;
        }

        // EMITTER_BLUR_TIME_KEYFRAMES (0x50C).
        // Layout: u32 count, f32 random, 4 bytes reserved, then count × {f32 time, f32 value}.
        // (Mirrors RotationKeyframes' shape but with one-float header instead of two.)
        private static W3DEmitterBlurTimeData ReadBlurTimeKeyframes(W3DBinaryReader reader, long end)
        {
            var keyframeCount = reader.ReadUInt32();
            var data = new W3DEmitterBlurTimeData
            {
                Random = reader.ReadFloat()
            };
            reader.Skip(4); // reserved

            int maxKeysFromChunk = (int)((end - reader.Position) / 8);
            int keysToRead = keyframeCount > 0 ? System.Math.Min((int)keyframeCount + 1, maxKeysFromChunk) : maxKeysFromChunk;
            for (int i = 0; i < keysToRead; i++)
            {
                data.Keyframes.Add(new W3DFloatKeyframe
                {
                    Time = reader.ReadFloat(),
                    Value = reader.ReadFloat()
                });
            }
            return data;
        }

        // EMITTER_EXTRA_KEYFRAMES (0x50D, EnB-specific).
        // Same shape as Frame keyframes — count + random + 8-byte pad + key list.
        private static W3DEmitterExtraData ReadExtraKeyframes(W3DBinaryReader reader, long end)
        {
            var keyframeCount = reader.ReadUInt32();
            var data = new W3DEmitterExtraData
            {
                Random = reader.ReadFloat()
            };
            reader.Skip(8); // reserved

            int maxKeysFromChunk = (int)((end - reader.Position) / 8);
            int keysToRead = keyframeCount > 0 ? System.Math.Min((int)keyframeCount + 1, maxKeysFromChunk) : maxKeysFromChunk;
            for (int i = 0; i < keysToRead; i++)
            {
                data.Keyframes.Add(new W3DFloatKeyframe
                {
                    Time = reader.ReadFloat(),
                    Value = reader.ReadFloat()
                });
            }
            return data;
        }
    }
}
