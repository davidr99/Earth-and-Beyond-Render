using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace W3DImporter.IO
{
    public class W3DBinaryReader : IDisposable
    {
        public const int NAME_LENGTH = 16;
        public const int LARGE_NAME_LENGTH = 32;

        private readonly BinaryReader _reader;

        public W3DBinaryReader(Stream stream)
        {
            _reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        }

        public long Position
        {
            get => _reader.BaseStream.Position;
            set => _reader.BaseStream.Position = value;
        }

        public long Length => _reader.BaseStream.Length;

        public void Seek(long position) => _reader.BaseStream.Seek(position, SeekOrigin.Begin);
        public void Skip(int count) => _reader.BaseStream.Seek(count, SeekOrigin.Current);

        // Primitives
        public byte ReadByte() => _reader.ReadByte();
        public sbyte ReadSByte() => _reader.ReadSByte();
        public ushort ReadUInt16() => _reader.ReadUInt16();
        public short ReadInt16() => _reader.ReadInt16();
        public uint ReadUInt32() => _reader.ReadUInt32();
        public int ReadInt32() => _reader.ReadInt32();
        public float ReadFloat() => _reader.ReadSingle();
        public byte[] ReadBytes(int count) => _reader.ReadBytes(count);

        // W3D-specific types
        public string ReadFixedString(int length = NAME_LENGTH)
        {
            byte[] bytes = _reader.ReadBytes(length);
            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) end = length;
            return Encoding.ASCII.GetString(bytes, 0, end);
        }

        public string ReadLongFixedString() => ReadFixedString(LARGE_NAME_LENGTH);

        public string ReadNullTerminatedString()
        {
            var sb = new StringBuilder();
            byte b;
            while ((b = _reader.ReadByte()) != 0)
                sb.Append((char)b);
            return sb.ToString();
        }

        public string ReadString(int length)
        {
            byte[] bytes = _reader.ReadBytes(length);
            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) end = length;
            return Encoding.ASCII.GetString(bytes, 0, end);
        }

        public Vector3 ReadVector3()
        {
            float x = _reader.ReadSingle();
            float y = _reader.ReadSingle();
            float z = _reader.ReadSingle();
            return new Vector3(x, y, z);
        }

        public Vector2 ReadVector2()
        {
            float x = _reader.ReadSingle();
            float y = _reader.ReadSingle();
            return new Vector2(x, y);
        }

        public Quaternion ReadQuaternion()
        {
            float x = _reader.ReadSingle();
            float y = _reader.ReadSingle();
            float z = _reader.ReadSingle();
            float w = _reader.ReadSingle();
            return new Quaternion(x, y, z, w);
        }

        public Quaternion ReadQuaternionWxyz()
        {
            float w = _reader.ReadSingle();
            float x = _reader.ReadSingle();
            float y = _reader.ReadSingle();
            float z = _reader.ReadSingle();
            return new Quaternion(x, y, z, w);
        }

        public Color32 ReadRGBA()
        {
            byte r = _reader.ReadByte();
            byte g = _reader.ReadByte();
            byte b = _reader.ReadByte();
            byte a = _reader.ReadByte();
            return new Color32(r, g, b, a);
        }

        public (uint major, uint minor) ReadVersion()
        {
            uint ver = _reader.ReadUInt32();
            return (ver >> 16, ver & 0xFFFF);
        }

        public void Dispose()
        {
            _reader?.Dispose();
        }
    }
}
