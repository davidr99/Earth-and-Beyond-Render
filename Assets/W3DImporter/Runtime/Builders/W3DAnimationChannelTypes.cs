namespace W3DImporter.Builders
{
    // Uncompressed channels (chunk 0x202) — the "Flags" field is overloaded as channel type.
    // 0/1/2 are scalar XYZ translation (VectorLen = 1). 3/4/5 are legacy Euler rotations
    // (parsed by the client but never evaluated). 6 is the full quaternion (VectorLen = 4,
    // xyzw per frame).
    public static class W3DUncompressedChannelTypes
    {
        public const int TranslationX = 0;
        public const int TranslationY = 1;
        public const int TranslationZ = 2;
        public const int EulerX = 3;
        public const int EulerY = 4;
        public const int EulerZ = 5;
        public const int Quaternion = 6;
    }

    // Compressed channels (chunk 0x282, both flavors) use the SAME channel-type enum as
    // the uncompressed family: 0/1/2 scalar XYZ translation, 6 quaternion (VectorLen = 4).
    // The client only keeps types 0/1/2/6 for compressed animations (W3D_FORMAT.md §10.2).
    public static class W3DTimeCodedChannelTypes
    {
        public const int TranslationX = 0;
        public const int TranslationY = 1;
        public const int TranslationZ = 2;
        public const int Quaternion = 6;
    }

    // Bit channels (0x203 / 0x283): type 0 = visibility, the only type the client keeps
    // (W3D_FORMAT.md §10.1 / §10.2).
    public static class W3DBitChannelTypes
    {
        public const int Visibility = 0;
    }
}
