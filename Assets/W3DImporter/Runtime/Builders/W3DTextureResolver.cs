using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace W3DImporter.Builders
{
    public class W3DTextureResolver
    {
        // AI-upscaled .png files sit alongside the original .dds/.tga game textures.
        // When true they take priority; when false the originals load. Textures are
        // cached per load, so callers must reload the model after flipping this.
        public static bool UseUpscaledPngs = true;

        private static readonly string[] ExtensionsWithPng = { ".png", ".jpg", ".jpeg", ".dds", ".tga", ".bmp" };
        private static readonly string[] ExtensionsWithoutPng = { ".jpg", ".jpeg", ".dds", ".tga", ".bmp" };
        private static string[] Extensions => UseUpscaledPngs ? ExtensionsWithPng : ExtensionsWithoutPng;
        private static readonly Regex TrailingDigitsRegex = new Regex(@"^(.*?)(\d+)$", RegexOptions.Compiled);
        private readonly string _baseDirectory;
        private readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Texture2D[]> _animationCache = new Dictionary<string, Texture2D[]>();
        private readonly HashSet<string> _missingTextures = new HashSet<string>();

        // Case-insensitive file name -> path of each texture directory, rebuilt when the
        // directory changes. The art folder holds ~15,000 files and every texture is tried with
        // several extensions; listing it on each miss made model loads slow.
        private sealed class DirectoryIndex
        {
            public System.DateTime Stamp;
            public Dictionary<string, string> Files;
        }

        private static readonly Dictionary<string, DirectoryIndex> DirectoryIndexes =
            new Dictionary<string, DirectoryIndex>(System.StringComparer.OrdinalIgnoreCase);

        // png path -> (size, write time) of upscaled pngs whose colors were checked and need no repair
        private static readonly Dictionary<string, (long, System.DateTime)> CheckedUpscaledPngs =
            new Dictionary<string, (long, System.DateTime)>(System.StringComparer.OrdinalIgnoreCase);

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
                        // LoadImage fills rows bottom-up (image displays upright), but the
                        // raw DDS loader uploads rows top-down and the mesh V-flip is
                        // calibrated to THAT orientation — flip so both paths match.
                        var pixels = tex.GetPixels32();
                        FlipRows(pixels, tex.width, tex.height);
                        if (ext == ".png")
                            RepairUpscaledColors(pixels, tex.width, tex.height, path);
                        tex.SetPixels32(pixels);
                        tex.Apply(true);
                        tex.name = Path.GetFileNameWithoutExtension(path);
                        tex.wrapMode = TextureWrapMode.Repeat;
                        // The DDS path has no mip chain and always samples sharp mip 0;
                        // PNGs have mips, so without trilinear + high aniso the upscaled
                        // detail is filtered away at any distance or viewing angle.
                        tex.filterMode = FilterMode.Trilinear;
                        tex.anisoLevel = 16;
                        if (W3DLog.Verbose) Debug.Log($"[W3D] Loaded: {path}");
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

        private static void FlipRows(Color32[] pixels, int w, int h)
        {
            for (int y = 0; y < h / 2; y++)
            {
                int top = y * w;
                int bottom = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    (pixels[top + x], pixels[bottom + x]) = (pixels[bottom + x], pixels[top + x]);
                }
            }
        }

        // The AI-upscaled pngs lost (or darkened) their color wherever the original texture is
        // transparent: ~30 of them are black there, ~90 more noticeably darker (e.g. g_lng1,
        // the wall texture of nbar01). W3D shaders that ignore alpha show that color, so where
        // the png is transparent its color is blended back toward the original .dds / .tga.
        // pixels: rows in DDS order (first row = top), as the loaders hand them to Unity.
        private static void RepairUpscaledColors(Color32[] pixels, int width, int height, string pngPath)
        {
            var info = new FileInfo(pngPath);
            var stamp = (info.Length, info.LastWriteTimeUtc);
            if (CheckedUpscaledPngs.TryGetValue(pngPath, out var checkedStamp) && checkedStamp == stamp)
                return;

            int lowAlpha = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 128)
                    lowAlpha++;
            }

            int ow = 0, oh = 0;
            var original = lowAlpha >= pixels.Length / 100
                ? LoadOriginalPixels(Path.GetDirectoryName(pngPath), Path.GetFileNameWithoutExtension(pngPath), out ow, out oh)
                : null;
            if (original == null || !IsDarkenedUnderAlpha(pixels, width, height, original, ow, oh))
            {
                CheckedUpscaledPngs[pngPath] = stamp;
                return;
            }

            for (int y = 0; y < height; y++)
            {
                float fy = Mathf.Clamp((y + 0.5f) * oh / height - 0.5f, 0f, oh - 1);
                int y0 = (int)fy, y1 = Mathf.Min(y0 + 1, oh - 1);
                float ty = fy - y0;
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    var p = pixels[i];
                    if (p.a == 255)
                        continue;

                    float fx = Mathf.Clamp((x + 0.5f) * ow / width - 0.5f, 0f, ow - 1);
                    int x0 = (int)fx, x1 = Mathf.Min(x0 + 1, ow - 1);
                    float tx = fx - x0;
                    var c00 = original[y0 * ow + x0];
                    var c10 = original[y0 * ow + x1];
                    var c01 = original[y1 * ow + x0];
                    var c11 = original[y1 * ow + x1];
                    // trust the png's color in proportion to its opacity
                    float t = p.a / 255f;
                    p.r = Blend(Bilinear(c00.r, c10.r, c01.r, c11.r, tx, ty), p.r, t);
                    p.g = Blend(Bilinear(c00.g, c10.g, c01.g, c11.g, tx, ty), p.g, t);
                    p.b = Blend(Bilinear(c00.b, c10.b, c01.b, c11.b, tx, ty), p.b, t);
                    pixels[i] = p;
                }
            }

            if (W3DLog.Verbose) Debug.Log($"[W3D] Restored the color of transparent pixels in {Path.GetFileName(pngPath)} from the original texture");
        }

        // True when the png's color under the original's transparent pixels is clearly darker
        // than the original's.
        private static bool IsDarkenedUnderAlpha(Color32[] png, int width, int height, Color32[] original, int ow, int oh)
        {
            long pngSum = 0, originalSum = 0, count = 0;
            for (int y = 0; y < oh; y++)
            {
                int py = y * height / oh;
                for (int x = 0; x < ow; x++)
                {
                    var o = original[y * ow + x];
                    if (o.a >= 128)
                        continue;
                    var p = png[py * width + x * width / ow];
                    originalSum += o.r + o.g + o.b;
                    pngSum += p.r + p.g + p.b;
                    count++;
                }
            }

            return count > 0 && originalSum > count * 3 * 8 && pngSum < originalSum * 0.75;
        }

        private static float Bilinear(byte c00, byte c10, byte c01, byte c11, float tx, float ty)
        {
            float top = c00 + (c10 - c00) * tx;
            float bottom = c01 + (c11 - c01) * tx;
            return top + (bottom - top) * ty;
        }

        private static byte Blend(float original, byte png, float t)
        {
            return (byte)Mathf.Clamp(Mathf.Round(original + (png - original) * t), 0f, 255f);
        }

        // Pixels of the original game texture (.dds or .tga) in DDS row order.
        private static Color32[] LoadOriginalPixels(string directory, string baseName, out int width, out int height)
        {
            width = height = 0;
            string dds = FindCaseInsensitive(directory, baseName + ".dds");
            if (dds != null)
                return DecodeDds(File.ReadAllBytes(dds), out width, out height);

            string tga = FindCaseInsensitive(directory, baseName + ".tga");
            if (tga == null)
                return null;

            var tex = LoadTGA(tga);
            if (tex == null)
                return null;
            width = tex.width;
            height = tex.height;
            var pixels = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return pixels;
        }

        private static Color32[] DecodeDds(byte[] data, out int width, out int height)
        {
            width = height = 0;
            if (data.Length < 128 || data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ')
                return null;

            height = ReadInt(data, 12);
            width = ReadInt(data, 16);
            uint pfFlags = (uint)ReadInt(data, 80);
            string fourCC = "" + (char)data[84] + (char)data[85] + (char)data[86] + (char)data[87];
            int bpp = ReadInt(data, 88);
            if ((pfFlags & 0x4) != 0)
            {
                switch (fourCC)
                {
                    case "DXT1": return DecompressDXT1(data, 128, width, height);
                    case "DXT3": return DecompressDXT3(data, 128, width, height);
                    case "DXT5": return DecompressDXT5(data, 128, width, height);
                    default: return null;
                }
            }

            return bpp == 24 || bpp == 32 ? ReadUncompressed(data, 128, width, height, bpp) : null;
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
            else if (compressed && fourCC == "DXT3")
            {
                // Unity has no DXT3 format (its alpha blocks differ from DXT5): decode it
                return LoadDDS_Software(data, headerSize, width, height, fourCC, bpp, path);
            }
            else if (compressed && fourCC == "DXT5")
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
                if (W3DLog.Verbose) Debug.Log($"[W3D] Loaded DDS: {path} ({width}x{height} {fourCC} bpp={bpp} size={mip0Size})");
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
            if (W3DLog.Verbose) Debug.Log($"[W3D] Loaded DDS (24-bit): {path} ({width}x{height})");
            return tex;
        }

        private Texture2D LoadDDS_Software(byte[] data, int offset, int width, int height, string fourCC, int bpp, string path)
        {
            Color32[] pixels = null;
            if (fourCC == "DXT1")
                pixels = DecompressDXT1(data, offset, width, height);
            else if (fourCC == "DXT3")
                pixels = DecompressDXT3(data, offset, width, height);
            else if (fourCC == "DXT5")
                pixels = DecompressDXT5(data, offset, width, height);

            if (pixels == null) return null;

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.name = Path.GetFileNameWithoutExtension(path);
            tex.SetPixels32(pixels);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            if (W3DLog.Verbose) Debug.Log($"[W3D] Loaded DDS (software): {path} ({width}x{height} {fourCC})");
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
                            pixels[y * width + x] = c;
                        }
                    }
                }
            }
            return pixels;
        }

        // DXT3: 4-bit explicit alpha per pixel, then a 4-color DXT1 color block.
        private static Color32[] DecompressDXT3(byte[] data, int offset, int width, int height)
        {
            var pixels = new Color32[width * height];
            int blocksW = (width + 3) / 4;
            int blocksH = (height + 3) / 4;
            var palette = new Color32[4];

            for (int by = 0; by < blocksH; by++)
            {
                for (int bx = 0; bx < blocksW; bx++)
                {
                    if (offset + 16 > data.Length) break;

                    int alphaOffset = offset;
                    offset += 8;

                    ushort c0 = (ushort)(data[offset] | (data[offset + 1] << 8));
                    ushort c1 = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
                    uint lookup = (uint)(data[offset + 4] | (data[offset + 5] << 8) |
                                         (data[offset + 6] << 16) | (data[offset + 7] << 24));
                    offset += 8;

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

                            int pixel = py * 4 + px;
                            int alpha4 = (data[alphaOffset + pixel / 2] >> ((pixel & 1) * 4)) & 0xF;
                            var c = palette[(int)((lookup >> (py * 8 + px * 2)) & 0x3)];
                            c.a = (byte)(alpha4 * 17);
                            pixels[y * width + x] = c;
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

        private static Texture2D LoadTGA(string path)
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
            if (W3DLog.Verbose) Debug.Log($"[W3D] Loaded TGA: {path} ({width}x{height} type={imageType} bpp={bpp})");
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

        private static string FindCaseInsensitive(string directory, string filename)
        {
            var files = GetDirectoryIndex(directory);
            return files != null && files.TryGetValue(filename, out var path) ? path : null;
        }

        private static Dictionary<string, string> GetDirectoryIndex(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return null;

            // a directory's write time changes when entries are added, removed or renamed
            var stamp = Directory.GetLastWriteTimeUtc(directory);
            if (DirectoryIndexes.TryGetValue(directory, out var cached) && cached.Stamp == stamp)
                return cached.Files;

            var files = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(directory))
                files[Path.GetFileName(file)] = file;
            DirectoryIndexes[directory] = new DirectoryIndex { Stamp = stamp, Files = files };
            return files;
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
