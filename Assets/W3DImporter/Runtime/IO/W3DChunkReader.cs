using System.Collections.Generic;

namespace W3DImporter.IO
{
    public struct W3DChunkHeader
    {
        public uint ChunkType;
        public uint ChunkSize;
        public bool HasSubChunks;
        public long ChunkEnd;
    }

    public class W3DChunkReader
    {
        private readonly W3DBinaryReader _reader;

        public W3DChunkReader(W3DBinaryReader reader)
        {
            _reader = reader;
        }

        public W3DChunkHeader ReadChunkHeader()
        {
            uint chunkType = _reader.ReadUInt32();
            uint rawSize = _reader.ReadUInt32();
            bool hasSubChunks = (rawSize & 0x80000000) != 0;
            uint chunkSize = rawSize & 0x7FFFFFFF;
            long chunkEnd = _reader.Position + chunkSize;

            return new W3DChunkHeader
            {
                ChunkType = chunkType,
                ChunkSize = chunkSize,
                HasSubChunks = hasSubChunks,
                ChunkEnd = chunkEnd
            };
        }

        public void SkipChunk(W3DChunkHeader chunk)
        {
            _reader.Seek(chunk.ChunkEnd);
        }

        public IEnumerable<W3DChunkHeader> EnumerateChunks(long endPosition)
        {
            while (_reader.Position < endPosition)
            {
                yield return ReadChunkHeader();
            }
        }
    }
}
