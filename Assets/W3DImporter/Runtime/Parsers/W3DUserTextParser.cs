using System;
using System.Collections.Generic;
using System.Globalization;
using W3DImporter.Structs.Mesh;

namespace W3DImporter.Parsers
{
    public static class W3DUserTextParser
    {
        public static W3DUserTextData Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var data = new W3DUserTextData();
            Dictionary<string, string> currentSection = null;

            var lines = text.Replace("\r", string.Empty).Split('\n');
            foreach (var rawLine in lines)
            {
                var line = StripComment(rawLine).Trim();
                if (line.Length == 0)
                    continue;

                if (line[0] == '[' && line[^1] == ']')
                {
                    string sectionName = line.Substring(1, line.Length - 2).Trim();
                    if (!data.Sections.TryGetValue(sectionName, out currentSection))
                    {
                        currentSection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        data.Sections[sectionName] = currentSection;
                    }
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0 || currentSection == null)
                    continue;

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                currentSection[key] = value;
            }

            data.EffectPass = ParseEffectSection(data);
            data.Surface = ParseSurfaceSection(data);

            if (data.EffectPass == null && data.Surface == null && data.Sections.Count == 0)
                return null;

            return data;
        }

        private static string StripComment(string line)
        {
            int semicolon = line.IndexOf(';');
            int hash = line.IndexOf('#');
            int cut = -1;

            if (semicolon >= 0)
                cut = semicolon;
            if (hash >= 0 && (cut < 0 || hash < cut))
                cut = hash;

            return cut >= 0 ? line.Substring(0, cut) : line;
        }

        private static W3DEffectPassDescription ParseEffectSection(W3DUserTextData data)
        {
            if (!data.Sections.TryGetValue("Effect_Pass_Description", out var section))
                return null;

            var effect = new W3DEffectPassDescription
            {
                EffectPassType = GetInt(section, "EFFECT_PASS_TYPE", 0),
                Shader = GetString(section, "SHADER"),
                Texture = GetString(section, "TEXTURE"),
                TwoSided = GetBoolNullable(section, "TWO_SIDED"),
                Hidden = GetBoolNullable(section, "HIDDEN"),
                CycleTime = GetFloatNullable(section, "CYCLE_TIME"),
                StartVCoord = GetFloatNullable(section, "START_V_COORD"),
                EndVCoord = GetFloatNullable(section, "END_V_COORD")
            };

            return effect;
        }

        private static W3DSurfaceDescription ParseSurfaceSection(W3DUserTextData data)
        {
            Dictionary<string, string> section = null;
            if (!data.Sections.TryGetValue("Surface_Description", out section))
                data.Sections.TryGetValue("LIGHTMAP_CHUNK", out section);

            if (section == null)
                return null;

            return new W3DSurfaceDescription
            {
                LightmapChunk = GetIntNullable(section, "LIGHTMAP_CHUNK"),
                ColorMap = GetString(section, "COLOR_MAP"),
                FlipTga = GetBool(section, "FLIP_TGA", false),
                DetailMap = GetString(section, "DETAIL_MAP"),
                XDetailScale = GetFloatNullable(section, "XDETAIL_SCALE"),
                YDetailScale = GetFloatNullable(section, "YDETAIL_SCALE")
            };
        }

        private static string GetString(Dictionary<string, string> section, string key)
        {
            return section.TryGetValue(key, out var value) ? value.Trim().Trim('"') : string.Empty;
        }

        private static int GetInt(Dictionary<string, string> section, string key, int fallback)
        {
            return GetIntNullable(section, key) ?? fallback;
        }

        private static int? GetIntNullable(Dictionary<string, string> section, string key)
        {
            if (!section.TryGetValue(key, out var value))
                return null;

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return parsed;

            return null;
        }

        private static float? GetFloatNullable(Dictionary<string, string> section, string key)
        {
            if (!section.TryGetValue(key, out var value))
                return null;

            if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out float parsed))
            {
                return parsed;
            }

            return null;
        }

        private static bool GetBool(Dictionary<string, string> section, string key, bool fallback)
        {
            return GetBoolNullable(section, key) ?? fallback;
        }

        private static bool? GetBoolNullable(Dictionary<string, string> section, string key)
        {
            if (!section.TryGetValue(key, out var value))
                return null;

            value = value.Trim();
            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return null;
        }
    }
}
