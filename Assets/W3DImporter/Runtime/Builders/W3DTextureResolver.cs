using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace W3DImporter.Builders
{
    public class W3DTextureResolver
    {
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".dds", ".tga", ".bmp" };
        private static readonly Regex TrailingDigitsRegex = new Regex(@"^(.*?)(\d+)$", RegexOptions.Compiled);
        private readonly string _baseDirectory;
        private readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Texture2D[]> _animationCache = new Dictionary<string, Texture2D[]>();
        private readonly HashSet<string> _missingTextures = new HashSet<string>();

        public W3DTextureResolver(string baseDirectory)
        {
            _baseDirectory = baseDirectory;
        }

        public Texture2D Resolve(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return null;

            string baseName = Path.GetFileNameWithoutExtension(filename).ToLower();

            if (_cache.TryGetValue(baseName, out var cached))
                return cached;

            foreach (var ext in Extensions)
            {
                string fullPath = FindCaseInsensitive(_baseDirectory, baseName + ext);
                if (fullPath != null)
                {
                    var tex = LoadTexture(fullPath);
                    if (tex != null)
                    {
                        _missingTextures.Remove(baseName);
                        _cache[baseName] = tex;
                        return tex;
                    }
                }
            }

            Debug.LogWarning($"[W3D] Texture not found: {baseName} in {_baseDirectory}");
            _missingTextures.Add(baseName);
            var fallback = CreateFallbackTexture(baseName);
            _cache[baseName] = fallback;
            return fallback;
        }

        public Texture2D[] ResolveAnimation(string filename, uint frameCount)
        {
            if (string.IsNullOrWhiteSpace(filename) || frameCount <= 1)
                return null;

            string baseName = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
            string cacheKey = baseName + "|" + frameCount;
            if (_animationCache.TryGetValue(cacheKey, out var cached))
                return cached;

            if (!TrailingDigitsRegex.IsMatch(baseName))
            {
                _animationCache[cacheKey] = null;
                return null;
            }

            var frames = new List<Texture2D>();
            for (int i = 0; i < frameCount; i++)
            {
                string frameName = BuildFrameName(baseName, i);
                var frame = Resolve(frameName);
                if (frame == null || _missingTextures.Contains(frameName))
                    break;

                frames.Add(frame);
            }

            if (frames.Count <= 1)
            {
                _animationCache[cacheKey] = null;
                return null;
            }

            var result = frames.ToArray();
            _animationCache[cacheKey] = result;
            return result;
        }

        private static string BuildFrameName(string baseName, int offset)
        {
            var match = TrailingDigitsRegex.Match(baseName);
            if (!match.Success)
                return baseName;

            string prefix = match.Groups[1].Value;
            string digits = match.Groups[2].Value;
            int startValue = int.Parse(digits);
            return prefix + (startValue + offset).ToString("D" + digits.Length);
        }

        private Texture2D LoadTexture(string path)
        {
            string ext = Path.GetExtension(path).ToLower();

            try
            {
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                {
                    byte[] data = File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (tex.LoadImage(data))
                    {
                        tex.name = Path.GetFileNameWithoutExtension(path);
                        tex.wrapMode = TextureWrapMode.Repeat;
                        tex.filterMode = FilterMode.Bilinear;
                        Debug.Log($"[W3D] Loaded: {path}");
                        return tex;
                    }
                }
                else if (ext == ".tga")
                {
                    return LoadTGA(path);
                }
                else if (ext == ".dds")
                {
                    return LoadDDS(path);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[W3D] Failed to load {path}: {e.Message}");
            }

            return null;
        }

        private Texture2D LoadDDS(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 128) return null;

            if (data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ')
                return null;

            int height = ReadInt(data, 12);
            int width = ReadInt(data, 16);
            int mipCount = ReadInt(data, 28);
            uint pfFlags = (uint)ReadInt(data, 80);
            string fourCC = "" + (char)data[84] + (char)data[85] + (char)data[86] + (char)data[87];
            int bpp = ReadInt(data, 88);

            int headerSize = 128;

            // Determine format and mip0 data size
            TextureFormat format;
            int mip0Size;
            bool compressed = (pfFlags & 0x4) != 0; // DDPF_FOURCC

            if (compressed && fourCC == "DXT1")
            {
                format = TextureFormat.DXT1;
                mip0Size = Mathf.Max(1, (width + 3) / 4) * Mathf.Max(1, (height + 3) / 4) * 8;
            }
            else if (compressed && (fourCC == "DXT5" || fourCC == "DXT3"))
            {
                format = TextureFormat.DXT5;
                mip0Size = Mathf.Max(1, (width + 3) / 4) * Mathf.Max(1, (height + 3) / 4) * 16;
            }
            else
            {
                // Uncompressed - read as BGRA32 or RGBA32
                if (bpp == 32)
                {
                    format = TextureFormat.BGRA32;
                    mip0Size = width * height * 4;
                }
                else if (bpp == 24)
                {
                    // Unity doesn't support 24-bit well, convert to RGBA
                    return LoadDDS_Uncompressed24(data, headerSize, width, height, path);
                }
                else
                {
                    format = TextureFormat.RGBA32;
                    mip0Size = width * height * 4;
                }
            }

            int available = data.Length - headerSize;
            if (mip0Size > available) mip0Size = available;

            // Use Unity's native texture loading (handles DXT natively on GPU)
            var tex = new Texture2D(width, height, format, false);
            tex.name = Path.GetFileNameWithoutExtension(path);

            try
            {
                byte[] texData = new byte[mip0Size];
                System.Array.Copy(data, headerSize, texData, 0, mip0Size);
                tex.LoadRawTextureData(texData);
                tex.Apply(true);
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;
                Debug.Log($"[W3D] Loaded DDS: {path} ({width}x{height} {fourCC} bpp={bpp} size={mip0Size})");
                return tex;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[W3D] Native DDS load failed for {path}: {e.Message}, trying software decode");
                Object.Destroy(tex);
                // Fallback to software decompression
                return LoadDDS_Software(data, headerSize, width, height, fourCC, bpp, path);
            }
        }

        private Texture2D LoadDDS_Uncompressed24(byte[] data, int offset, int width, int height, string path)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.name = Path.GetFileNameWithoutExtension(path);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int src = offset + (y * width + x) * 3;
                    if (src + 2 >= data.Length) break;
                    pixels[y * width + x] = new Color32(data[src + 2], data[src + 1], data[src], 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            Debug.Log($"[W3D] Loaded DDS (24-bit): {path} ({width}x{height})");
            return tex;
        }

        private Texture2D LoadDDS_Software(byte[] data, int offset, int width, int height, string fourCC, int bpp, string path)
        {
            Color32[] pixels = null;
            if (fourCC == "DXT1")
                pixels = DecompressDXT1(data, offset, width, height);
            else if (fourCC == "DXT5" || fourCC == "DXT3")
                pixels = DecompressDXT5(data, offset, width, height);

            if (pixels == null) return null;

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.name = Path.GetFileNameWithoutExtension(path);
            tex.SetPixels32(pixels);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            Debug.Log($"[W3D] Loaded DDS (software): {path} ({width}x{height} {fourCC})");
            return tex;
        }

        private static Color32[] DecompressDXT1(byte[] data, int offset, int width, int height)
        {
            var pixels = new Color32[width * height];
            int blocksW = (width + 3) / 4;
            int blocksH = (height + 3) / 4;

            for (int by = 0; by < blocksH; by++)
            {
                for (int bx = 0; bx < blocksW; bx++)
                {
                    if (offset + 8 > data.Length) break;

                    ushort c0 = (ushort)(data[offset] | (data[offset + 1] << 8));
                    ushort c1 = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
                    uint lookup = (uint)(data[offset + 4] | (data[offset + 5] << 8) |
                                         (data[offset + 6] << 16) | (data[offset + 7] << 24));
                    offset += 8;

                    Color32[] palette = new Color32[4];
                    palette[0] = RGB565ToColor(c0);
                    palette[1] = RGB565ToColor(c1);

                    if (c0 > c1)
                    {
                        palette[2] = LerpColor(palette[0], palette[1], 1, 3);
                        palette[3] = LerpColor(palette[0], palette[1], 2, 3);
                    }
                    else
                    {
                        palette[2] = LerpColor(palette[0], palette[1], 1, 2);
                        palette[3] = new Color32(0, 0, 0, 0);
                    }

                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            int y = by * 4 + py;
                            if (x >= width || y >= height) continue;

                            int idx = (int)((lookup >> (py * 8 + px * 2)) & 0x3);
                            pixels[y * width + x] = palette[idx];
                        }
                    }
                }
            }
            return pixels;
        }

        private static Color32[] DecompressDXT5(byte[] data, int offset, int width, int height)
        {
            var pixels = new Color32[width * height];
            int blocksW = (width + 3) / 4;
            int blocksH = (height + 3) / 4;

            for (int by = 0; by < blocksH; by++)
            {
                for (int bx = 0; bx < blocksW; bx++)
                {
                    if (offset + 16 > data.Length) break;

                    // Alpha block (8 bytes)
                    byte a0 = data[offset];
                    byte a1 = data[offset + 1];
                    ulong alphaBits = 0;
                    for (int i = 2; i < 8; i++)
                        alphaBits |= (ulong)data[offset + i] << ((i - 2) * 8);
                    offset += 8;

                    byte[] alphaPalette = new byte[8];
                    alphaPalette[0] = a0;
                    alphaPalette[1] = a1;
                    if (a0 > a1)
                    {
                        for (int i = 1; i < 7; i++)
                            alphaPalette[i + 1] = (byte)((a0 * (7 - i) + a1 * i) / 7);
                    }
                    else
                    {
                        for (int i = 1; i < 5; i++)
                            alphaPalette[i + 1] = (byte)((a0 * (5 - i) + a1 * i) / 5);
                        alphaPalette[6] = 0;
                        alphaPalette[7] = 255;
                    }

                    // Color block (8 bytes) - same as DXT1
                    ushort c0 = (ushort)(data[offset] | (data[offset + 1] << 8));
                    ushort c1 = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
                    uint lookup = (uint)(data[offset + 4] | (data[offset + 5] << 8) |
                                         (data[offset + 6] << 16) | (data[offset + 7] << 24));
                    offset += 8;

                    Color32[] palette = new Color32[4];
                    palette[0] = RGB565ToColor(c0);
                    palette[1] = RGB565ToColor(c1);
                    palette[2] = LerpColor(palette[0], palette[1], 1, 3);
                    palette[3] = LerpColor(palette[0], palette[1], 2, 3);

                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            int y = by * 4 + py;
                            if (x >= width || y >= height) continue;

                            int colorIdx = (int)((lookup >> (py * 8 + px * 2)) & 0x3);
                            int alphaIdx = (int)((alphaBits >> ((py * 4 + px) * 3)) & 0x7);

                            var c = palette[colorIdx];
                            c.a = alphaPalette[alphaIdx];
                            pixels[(height - 1 - y) * width + x] = c;
                        }
                    }
                }
            }
            return pixels;
        }

        private static Color32[] ReadUncompressed(byte[] data, int offset, int width, int height, int bpp)
        {
            var pixels = new Color32[width * height];
            int bytesPerPixel = bpp / 8;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = offset + (y * width + x) * bytesPerPixel;
                    if (srcIdx + bytesPerPixel > data.Length) break;

                    byte b = data[srcIdx];
                    byte g = data[srcIdx + 1];
                    byte r = data[srcIdx + 2];
                    byte a = bytesPerPixel >= 4 ? data[srcIdx + 3] : (byte)255;
                    pixels[y * width + x] = new Color32(r, g, b, a);
                }
            }
            return pixels;
        }

        private static Color32 RGB565ToColor(ushort c)
        {
            byte r = (byte)((c >> 11) * 255 / 31);
            byte g = (byte)(((c >> 5) & 0x3F) * 255 / 63);
            byte b = (byte)((c & 0x1F) * 255 / 31);
            return new Color32(r, g, b, 255);
        }

        private static Color32 LerpColor(Color32 a, Color32 b, int num, int denom)
        {
            return new Color32(
                (byte)((a.r * (denom - num) + b.r * num) / denom),
                (byte)((a.g * (denom - num) + b.g * num) / denom),
                (byte)((a.b * (denom - num) + b.b * num) / denom),
                255
            );
        }

        private static int ReadInt(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
        }

        private Texture2D LoadTGA(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 18) return null;

            int idLength = data[0];
            int imageType = data[2];
            int width = data[12] | (data[13] << 8);
            int height = data[14] | (data[15] << 8);
            int bpp = data[16];
            int descriptor = data[17];
            int bytesPerPixel = bpp / 8;
            int dataOffset = 18 + idLength;

            if (width <= 0 || height <= 0)
                return null;

            if ((imageType != 2 && imageType != 10) || (bytesPerPixel != 3 && bytesPerPixel != 4))
            {
                Debug.LogWarning($"[W3D] Unsupported TGA format: {path} (type={imageType} bpp={bpp})");
                return null;
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.name = Path.GetFileNameWithoutExtension(path);

            var pixels = new Color32[width * height];
            bool topOrigin = (descriptor & 0x20) != 0;
            bool rightOrigin = (descriptor & 0x10) != 0;
            int idx = dataOffset;
            int pixelIndex = 0;
            int pixelCount = width * height;

            while (pixelIndex < pixelCount && idx < data.Length)
            {
                int runLength = 1;
                bool isRlePacket = false;

                if (imageType == 10)
                {
                    byte packetHeader = data[idx++];
                    isRlePacket = (packetHeader & 0x80) != 0;
                    runLength = (packetHeader & 0x7F) + 1;
                }

                if (isRlePacket)
                {
                    if (idx + bytesPerPixel > data.Length)
                        break;

                    var color = ReadTgaColor(data, ref idx, bytesPerPixel);
                    for (int i = 0; i < runLength && pixelIndex < pixelCount; i++, pixelIndex++)
                        WriteTgaPixel(pixels, width, height, pixelIndex, color, topOrigin, rightOrigin);
                }
                else
                {
                    for (int i = 0; i < runLength && pixelIndex < pixelCount; i++, pixelIndex++)
                    {
                        if (idx + bytesPerPixel > data.Length)
                            break;

                        var color = ReadTgaColor(data, ref idx, bytesPerPixel);
                        WriteTgaPixel(pixels, width, height, pixelIndex, color, topOrigin, rightOrigin);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            Debug.Log($"[W3D] Loaded TGA: {path} ({width}x{height} type={imageType} bpp={bpp})");
            return tex;
        }

        private static Color32 ReadTgaColor(byte[] data, ref int idx, int bytesPerPixel)
        {
            byte b = data[idx++];
            byte g = data[idx++];
            byte r = data[idx++];
            byte a = bytesPerPixel >= 4 ? data[idx++] : (byte)255;
            return new Color32(r, g, b, a);
        }

        private static void WriteTgaPixel(
            Color32[] pixels,
            int width,
            int height,
            int sourceIndex,
            Color32 color,
            bool topOrigin,
            bool rightOrigin)
        {
            int x = sourceIndex % width;
            int y = sourceIndex / width;

            if (!topOrigin)
                y = (height - 1) - y;
            if (rightOrigin)
                x = (width - 1) - x;

            pixels[y * width + x] = color;
        }

        private string FindCaseInsensitive(string directory, string filename)
        {
            if (!Directory.Exists(directory)) return null;

            string direct = Path.Combine(directory, filename);
            if (File.Exists(direct)) return direct;

            foreach (var file in Directory.GetFiles(directory))
            {
                if (Path.GetFileName(file).Equals(filename, System.StringComparison.OrdinalIgnoreCase))
                    return file;
            }
            return null;
        }

        private Texture2D CreateFallbackTexture(string name)
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            tex.name = name;

            var colors = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    colors[y * 64 + x] = ((x / 8 + y / 8) % 2 == 0)
                        ? new Color32(255, 0, 255, 255)
                        : new Color32(0, 0, 0, 255);

            tex.SetPixels32(colors);
            tex.Apply();
            return tex;
        }
    }
}
