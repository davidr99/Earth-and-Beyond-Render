using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using W3DImporter.IO;

namespace W3DImporter.Parsers
{
    // Which .w3d files of a directory hold animations for which hierarchy. Finding the
    // animations of a model used to parse every animation of every file in the directory
    // (6,368 files / 400 MB in the game's art folder) for each new skeleton. The index reads
    // only the top-level chunk headers and the animation headers, and is kept on disk so later
    // sessions only rescan files whose size or write time changed.
    public static class W3DAnimationIndex
    {
        private const string CacheHeader = "W3DAnimationIndex\t1";

        private sealed class FileEntry
        {
            public long Length;
            public long WriteTicks;
            public string[] Hierarchies = Array.Empty<string>();
        }

        private sealed class DirectoryState
        {
            public Dictionary<string, FileEntry> Files;
            public Dictionary<string, List<string>> PathsByHierarchy;
        }

        private static readonly Dictionary<string, DirectoryState> States =
            new Dictionary<string, DirectoryState>(StringComparer.OrdinalIgnoreCase);

        // .w3d files of the directory with at least one animation for the hierarchy, by name.
        public static IReadOnlyList<string> FilesWithAnimationsFor(string directory, string hierarchyName)
        {
            if (string.IsNullOrWhiteSpace(hierarchyName))
                return Array.Empty<string>();

            var state = GetState(directory);
            if (state != null && state.PathsByHierarchy.TryGetValue(hierarchyName, out var paths))
                return paths;
            return Array.Empty<string>();
        }

        private static DirectoryState GetState(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return null;

            directory = Path.GetFullPath(directory);
            if (!States.TryGetValue(directory, out var state))
            {
                state = new DirectoryState { Files = LoadCache(directory) };
                States[directory] = state;
            }

            // file sizes and times come with the directory listing; only new or changed files are read
            bool changed = false;
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in new DirectoryInfo(directory).EnumerateFiles("*.w3d"))
            {
                // "*.w3d" also matches longer extensions on Windows
                if (!string.Equals(info.Extension, ".w3d", StringComparison.OrdinalIgnoreCase))
                    continue;

                present.Add(info.Name);
                long ticks = info.LastWriteTimeUtc.Ticks;
                if (state.Files.TryGetValue(info.Name, out var entry) && entry.Length == info.Length && entry.WriteTicks == ticks)
                    continue;

                state.Files[info.Name] = new FileEntry
                {
                    Length = info.Length,
                    WriteTicks = ticks,
                    Hierarchies = ScanFile(info.FullName)
                };
                changed = true;
            }

            var removed = new List<string>();
            foreach (var name in state.Files.Keys)
            {
                if (!present.Contains(name))
                    removed.Add(name);
            }
            foreach (var name in removed)
                state.Files.Remove(name);
            changed |= removed.Count > 0;

            if (changed || state.PathsByHierarchy == null)
            {
                var names = new List<string>(state.Files.Keys);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                var byHierarchy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in names)
                {
                    foreach (var hierarchy in state.Files[name].Hierarchies)
                    {
                        if (!byHierarchy.TryGetValue(hierarchy, out var paths))
                            byHierarchy[hierarchy] = paths = new List<string>();
                        string path = Path.Combine(directory, name);
                        if (paths.Count == 0 || paths[paths.Count - 1] != path)
                            paths.Add(path);
                    }
                }
                state.PathsByHierarchy = byHierarchy;
            }

            if (changed)
                SaveCache(directory, state.Files);
            return state;
        }

        // Hierarchy names of the ANIMATION / COMPRESSED_ANIMATION chunks of a file.
        private static string[] ScanFile(string path)
        {
            var hierarchies = new List<string>();
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 512);
                var buffer = new byte[44];
                long length = stream.Length;
                long position = 0;
                while (position + 8 <= length)
                {
                    stream.Position = position;
                    if (ReadFully(stream, buffer, 8) < 8)
                        break;

                    uint type = BitConverter.ToUInt32(buffer, 0);
                    long size = BitConverter.ToUInt32(buffer, 4) & 0x7FFFFFFF;
                    if ((type == W3DChunkTypes.ANIMATION || type == W3DChunkTypes.COMPRESSED_ANIMATION) &&
                        size >= 44 && ReadFully(stream, buffer, 44) == 44 &&
                        BitConverter.ToUInt32(buffer, 0) == type + 1)
                    {
                        // header sub chunk: version, name[16], hierarchy name[16], ...
                        string hierarchy = ReadName(buffer, 8 + 20);
                        if (hierarchy.Length > 0 && !hierarchies.Contains(hierarchy))
                            hierarchies.Add(hierarchy);
                    }

                    position += 8 + size;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[W3D] Animation index could not read '{path}': {e.Message}");
            }

            return hierarchies.ToArray();
        }

        private static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read <= 0)
                    break;
                total += read;
            }
            return total;
        }

        private static string ReadName(byte[] buffer, int offset)
        {
            int end = offset;
            while (end < offset + 16 && buffer[end] != 0)
                end++;
            // latin-1: every byte is the character of the same code
            var chars = new char[end - offset];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = (char)buffer[offset + i];
            return new string(chars).Trim();
        }

        private static string CachePath(string directory)
        {
            // FNV-1a of the directory: one cache file per art folder
            ulong hash = 14695981039346656037UL;
            foreach (char c in directory.ToLowerInvariant())
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return Path.Combine(Application.temporaryCachePath, "W3DAnimationIndex", hash.ToString("x16") + ".txt");
        }

        private static Dictionary<string, FileEntry> LoadCache(string directory)
        {
            var files = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = CachePath(directory);
                if (!File.Exists(path))
                    return files;

                var lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 2 || lines[0] != CacheHeader || !string.Equals(lines[1], directory, StringComparison.OrdinalIgnoreCase))
                    return files;

                for (int i = 2; i < lines.Length; i++)
                {
                    var fields = lines[i].Split('\t');
                    if (fields.Length != 4 || !long.TryParse(fields[1], out long length) || !long.TryParse(fields[2], out long ticks))
                        continue;
                    files[fields[0]] = new FileEntry
                    {
                        Length = length,
                        WriteTicks = ticks,
                        Hierarchies = fields[3].Length == 0 ? Array.Empty<string>() : fields[3].Split('|')
                    };
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                files.Clear();
            }
            return files;
        }

        private static void SaveCache(string directory, Dictionary<string, FileEntry> files)
        {
            try
            {
                string path = CachePath(directory);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var builder = new StringBuilder();
                builder.Append(CacheHeader).Append('\n').Append(directory).Append('\n');
                foreach (var pair in files)
                {
                    builder.Append(pair.Key).Append('\t').Append(pair.Value.Length).Append('\t')
                        .Append(pair.Value.WriteTicks).Append('\t').Append(string.Join("|", pair.Value.Hierarchies)).Append('\n');
                }

                // written next to the cache and swapped in, so a concurrent reader never sees half a file
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, builder.ToString(), new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[W3D] Animation index cache not saved: {e.Message}");
            }
        }
    }
}
