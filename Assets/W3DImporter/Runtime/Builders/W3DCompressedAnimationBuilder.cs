using System.Collections.Generic;
using UnityEngine;
using W3DImporter.Structs.Animation;
using W3DImporter.Structs.Hierarchy;

namespace W3DImporter.Builders
{
    // Converts W3D TimeCoded compressed animation (chunk 0x280, Flavor = 0) into a Unity
    // AnimationClip. TimeCoded channels each carry their own timeCodes[] + data[] arrays.
    // Channel types: 0/1/2 = scalar TX/TY/TZ, 3 = full quaternion (VectorLen = 4, xyzw per key).
    // The TX/TY/TZ channels don't share timecodes, so we build a raw AnimationCurve per axis
    // and then sample at the union of all keyframe times to apply the Z-up → Y-up basis
    // rotation before writing out the final Unity curves.
    //
    // Flavor = 1 (AdaptiveDelta) uses per-bit quantized deltas and is not yet decoded.
    public static class W3DCompressedAnimationBuilder
    {
        public static AnimationClip Build(W3DCompressedAnimation anim, W3DHierarchy hierarchy)
        {
            if (anim?.Header == null || hierarchy == null || hierarchy.Pivots.Count == 0)
                return null;

            if (anim.Header.Flavor != 0)
            {
                Debug.LogWarning($"[W3D] Compressed animation flavor {anim.Header.Flavor} (AdaptiveDelta) not yet supported; skipping '{anim.Header.Name}'.");
                return null;
            }

            float frameRate = anim.Header.FrameRate > 0f ? anim.Header.FrameRate : 30f;
            float frameDuration = 1f / frameRate;

            var clip = new AnimationClip
            {
                name = string.IsNullOrEmpty(anim.Header.Name) ? "W3DCompressedAnimation" : anim.Header.Name,
                frameRate = frameRate,
                legacy = true
            };

            var bonePaths = BuildBonePaths(hierarchy);

            var byPivot = new Dictionary<int, W3DTimeCodedChannel[]>();
            foreach (var ch in anim.TimeCodedChannels)
            {
                if (!byPivot.TryGetValue(ch.Pivot, out var slots))
                {
                    slots = new W3DTimeCodedChannel[4];
                    byPivot[ch.Pivot] = slots;
                }
                if (ch.ChannelType >= 0 && ch.ChannelType < slots.Length)
                    slots[ch.ChannelType] = ch;
            }

            foreach (var kvp in byPivot)
            {
                if (!bonePaths.TryGetValue(kvp.Key, out var path))
                    continue;

                var slots = kvp.Value;
                var pivot = hierarchy.Pivots[kvp.Key];

                var tx = slots[W3DTimeCodedChannelTypes.TranslationX];
                var ty = slots[W3DTimeCodedChannelTypes.TranslationY];
                var tz = slots[W3DTimeCodedChannelTypes.TranslationZ];
                if (tx != null || ty != null || tz != null)
                    BuildTranslationCurves(clip, path, tx, ty, tz, pivot.Translation, frameDuration);

                var quat = slots[W3DTimeCodedChannelTypes.Quaternion];
                if (quat != null)
                    BuildRotationCurves(clip, path, quat, frameDuration);
            }

            foreach (var bit in anim.BitChannels)
            {
                if (bit.ChannelType != W3DBitChannelTypes.Visibility)
                    continue;
                if (!bonePaths.TryGetValue(bit.Pivot, out var path))
                    continue;
                BuildVisibilityCurve(clip, path, bit, frameDuration);
            }

            clip.EnsureQuaternionContinuity();
            return clip;
        }

        private static Dictionary<int, string> BuildBonePaths(W3DHierarchy hierarchy)
        {
            var paths = new Dictionary<int, string>();
            paths[0] = "";
            for (int i = 1; i < hierarchy.Pivots.Count; i++)
            {
                var pivot = hierarchy.Pivots[i];
                string parentPath = paths.TryGetValue(pivot.ParentId, out var p) ? p : "";
                paths[i] = string.IsNullOrEmpty(parentPath) ? pivot.Name : parentPath + "/" + pivot.Name;
            }
            return paths;
        }

        private static void BuildTranslationCurves(AnimationClip clip, string path,
            W3DTimeCodedChannel tx, W3DTimeCodedChannel ty, W3DTimeCodedChannel tz,
            Vector3 basePoseW3D, float frameDuration)
        {
            var rawX = ToScalarCurve(tx, basePoseW3D.x, frameDuration);
            var rawY = ToScalarCurve(ty, basePoseW3D.y, frameDuration);
            var rawZ = ToScalarCurve(tz, basePoseW3D.z, frameDuration);

            var times = UnionTimes(rawX, rawY, rawZ);
            if (times.Count == 0) return;

            var cX = new AnimationCurve();
            var cY = new AnimationCurve();
            var cZ = new AnimationCurve();

            foreach (var t in times)
            {
                var unity = W3DCoordinateConverter.ConvertPosition(new Vector3(
                    rawX.Evaluate(t), rawY.Evaluate(t), rawZ.Evaluate(t)));
                cX.AddKey(t, unity.x);
                cY.AddKey(t, unity.y);
                cZ.AddKey(t, unity.z);
            }

            clip.SetCurve(path, typeof(Transform), "localPosition.x", cX);
            clip.SetCurve(path, typeof(Transform), "localPosition.y", cY);
            clip.SetCurve(path, typeof(Transform), "localPosition.z", cZ);
        }

        // Quaternion channel: VectorLen = 4, data[i*4 + 0..3] = (x, y, z, w) at timeCodes[i].
        private static void BuildRotationCurves(AnimationClip clip, string path,
            W3DTimeCodedChannel quat, float frameDuration)
        {
            if (quat.VectorLen != 4 || quat.TimeCodes.Count == 0)
                return;
            if (quat.Data.Count < quat.TimeCodes.Count * 4)
                return;

            var cX = new AnimationCurve();
            var cY = new AnimationCurve();
            var cZ = new AnimationCurve();
            var cW = new AnimationCurve();

            for (int i = 0; i < quat.TimeCodes.Count; i++)
            {
                int offset = i * 4;
                var w3d = new Quaternion(
                    quat.Data[offset + 0],
                    quat.Data[offset + 1],
                    quat.Data[offset + 2],
                    quat.Data[offset + 3]);

                var unity = W3DCoordinateConverter.ConvertRotation(w3d);
                float t = quat.TimeCodes[i] * frameDuration;

                cX.AddKey(t, unity.x);
                cY.AddKey(t, unity.y);
                cZ.AddKey(t, unity.z);
                cW.AddKey(t, unity.w);
            }

            clip.SetCurve(path, typeof(Transform), "localRotation.x", cX);
            clip.SetCurve(path, typeof(Transform), "localRotation.y", cY);
            clip.SetCurve(path, typeof(Transform), "localRotation.z", cZ);
            clip.SetCurve(path, typeof(Transform), "localRotation.w", cW);
        }

        private static void BuildVisibilityCurve(AnimationClip clip, string path,
            W3DAnimationBitChannel bit, float frameDuration)
        {
            var curve = new AnimationCurve();
            int frameCount = bit.LastFrame - bit.FirstFrame + 1;
            for (int f = 0; f < frameCount; f++)
            {
                bool visible = f < bit.Data.Count ? bit.Data[f] : bit.DefaultValue;
                var key = new Keyframe((bit.FirstFrame + f) * frameDuration, visible ? 1f : 0f);
                key.inTangent = float.PositiveInfinity;
                key.outTangent = float.PositiveInfinity;
                curve.AddKey(key);
            }
            clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
        }

        private static AnimationCurve ToScalarCurve(W3DTimeCodedChannel ch, float fallback, float frameDuration)
        {
            var curve = new AnimationCurve();
            if (ch == null || ch.TimeCodes.Count == 0)
            {
                curve.AddKey(0f, fallback);
                return curve;
            }

            int stride = ch.VectorLen > 0 ? ch.VectorLen : 1;
            for (int i = 0; i < ch.TimeCodes.Count; i++)
            {
                int dataIdx = i * stride;
                if (dataIdx >= ch.Data.Count) break;
                curve.AddKey(ch.TimeCodes[i] * frameDuration, ch.Data[dataIdx]);
            }
            return curve;
        }

        private static List<float> UnionTimes(params AnimationCurve[] curves)
        {
            var set = new HashSet<float>();
            foreach (var c in curves)
                if (c != null)
                    foreach (var k in c.keys)
                        set.Add(k.time);

            var list = new List<float>(set);
            list.Sort();
            return list;
        }
    }
}
