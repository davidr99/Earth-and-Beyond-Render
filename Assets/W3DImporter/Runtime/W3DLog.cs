namespace W3DImporter
{
    // Per-mesh / per-material / per-texture progress messages. Logging them on every load is
    // slow in the editor (each entry captures a stack trace), so they are off unless needed.
    public static class W3DLog
    {
        public static bool Verbose = false;
    }
}
