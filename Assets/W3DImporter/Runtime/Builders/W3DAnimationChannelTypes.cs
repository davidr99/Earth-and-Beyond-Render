namespace W3DImporter.Builders
{
    // Uncompressed channels (chunk 0x202) — the "Flags" field is overloaded as channel type.
    // 0/1/2 are scalar XYZ translation (VectorLen = 1). 3/4/5 are legacy Euler rotations
    // (rarely used). 6 is the full quaternion (VectorLen = 4, xyzw per frame).
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

    // Compressed TimeCoded channels (chunk 0x282, flavor = 0) use a different enum:
    // 0/1/2 scalar XYZ translation; 3 is the full quaternion (VectorLen = 4).
    // Flavor = 1 (AdaptiveDelta) uses types 4-7 for the same components.
    public static class W3DTimeCodedChannelTypes
    {
        public const int TranslationX = 0;
        public const int TranslationY = 1;
        public const int TranslationZ = 2;
        public const int Quaternion = 3;
    }

    public static class W3DBitChannelTypes
    {
        public const int Visibility = 15;
    }
}
