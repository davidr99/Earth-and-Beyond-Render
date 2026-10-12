using System.Collections.Generic;
using UnityEngine;
using W3DImporter.Structs.Animation;
using W3DImporter.Structs.Hierarchy;

namespace W3DImporter.Builders
{
    // Converts W3D compressed animation (chunk 0x280) into a Unity AnimationClip.
    // Both flavors arrive as W3DTimeCodedChannel keyframes: flavor 0 (TimeCoded) directly,
    // flavor 1 (AdaptiveDelta) pre-decoded to one key per frame by the parser.
    // Channel types match the uncompressed family: 0/1/2 = scalar TX/TY/TZ, 6 = full
    // quaternion (VectorLen = 4, xyzw per key). A key whose NoInterp flag is set holds
    // the previous key's value (stepped segment) instead of interpolating.
    // The TX/TY/TZ channels don't share timecodes, so we build a raw AnimationCurve per
    // axis and then sample at the union of all keyframe times to apply the Z-up → Y-up
    // basis rotation before writing out the final Unity curves.
    public static class W3DCompressedAnimationBuilder
    {
        public static AnimationClip Build(W3DCompressedAnimation anim, W3DHierarchy hierarchy)
        {
            if (anim?.Header == null || hierarchy == null || hierarchy.Pivots.Count == 0)
                return null;

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

                // Only types 0/1/2/6 are kept for compressed animations; Euler types
                // 3/4/5 are discarded (W3D_FORMAT.md §10.2).
                switch (ch.ChannelType)
                {
                    case W3DTimeCodedChannelTypes.TranslationX: slots[0] = ch; break;
                    case W3DTimeCodedChannelTypes.TranslationY: slots[1] = ch; break;
                    case W3DTimeCodedChannelTypes.TranslationZ: slots[2] = ch; break;
                    case W3DTimeCodedChannelTypes.Quaternion:   slots[3] = ch; break;
                }
            }

            foreach (var kvp in byPivot)
            {
                if (!bonePaths.TryGetValue(kvp.Key, out var path))
                    continue;

                var slots = kvp.Value;
                var pivot = hierarchy.Pivots[kvp.Key];

                var tx = slots[0];
                var ty = slots[1];
                var tz = slots[2];
                if (tx != null || ty != null || tz != null)
                    BuildTranslationCurves(clip, path, tx, ty, tz, pivot, frameDuration);

                var quat = slots[3];
                if (quat != null)
                    BuildRotationCurves(clip, path, quat, pivot, frameDuration);
            }

            foreach (var bit in anim.CompressedBitChannels)
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

        // Channel values are deltas on top of the pivot's base pose (see W3DAnimationBuilder).
        private static void BuildTranslationCurves(AnimationClip clip, string path,
            W3DTimeCodedChannel tx, W3DTimeCodedChannel ty, W3DTimeCodedChannel tz,
            W3DHierarchyPivot pivot, float frameDuration)
        {
            var rawX = ToScalarCurve(tx, 0f, frameDuration);
            var rawY = ToScalarCurve(ty, 0f, frameDuration);
            var rawZ = ToScalarCurve(tz, 0f, frameDuration);

            var times = UnionTimes(rawX, rawY, rawZ);
            if (times.Count == 0) return;

            // A stepped key jumps at its own time; sample just before it as well so the merged,
            // linearly interpolated curves still hold the previous value up to the jump.
            var holdTimes = new HashSet<float>();
            AddHoldTimes(tx, frameDuration, holdTimes);
            AddHoldTimes(ty, frameDuration, holdTimes);
            AddHoldTimes(tz, frameDuration, holdTimes);
            float epsilon = frameDuration * 0.001f;
            var sampleTimes = new List<float>(times);
            foreach (var t in holdTimes)
            {
                if (t - epsilon > 0f)
                    sampleTimes.Add(t - epsilon);
            }
            sampleTimes.Sort();

            var cX = new AnimationCurve();
            var cY = new AnimationCurve();
            var cZ = new AnimationCurve();

            foreach (var t in sampleTimes)
            {
                var unity = W3DAnimationBuilder.ComposeTranslation(pivot, new Vector3(
                    rawX.Evaluate(t), rawY.Evaluate(t), rawZ.Evaluate(t)));
                cX.AddKey(t, unity.x);
                cY.AddKey(t, unity.y);
                cZ.AddKey(t, unity.z);
            }

            W3DAnimationBuilder.MakeLinear(cX);
            W3DAnimationBuilder.MakeLinear(cY);
            W3DAnimationBuilder.MakeLinear(cZ);
            clip.SetCurve(path, typeof(Transform), "localPosition.x", cX);
            clip.SetCurve(path, typeof(Transform), "localPosition.y", cY);
            clip.SetCurve(path, typeof(Transform), "localPosition.z", cZ);
        }

        private static void AddHoldTimes(W3DTimeCodedChannel ch, float frameDuration, HashSet<float> holdTimes)
        {
            if (ch == null)
                return;
            int count = Mathf.Min(ch.TimeCodes.Count, ch.NoInterp.Count);
            for (int i = 1; i < count; i++)
            {
                if (ch.NoInterp[i])
                    holdTimes.Add(ch.TimeCodes[i] * frameDuration);
            }
        }

        // Quaternion channel: VectorLen = 4, data[i*4 + 0..3] = (x, y, z, w) at timeCodes[i].
        private static void BuildRotationCurves(AnimationClip clip, string path,
            W3DTimeCodedChannel quat, W3DHierarchyPivot pivot, float frameDuration)
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

                // AdaptiveDelta decoding accumulates error; keep quats unit-length.
                var unity = W3DAnimationBuilder.ComposeRotation(pivot, w3d.normalized);
                float t = quat.TimeCodes[i] * frameDuration;

                cX.AddKey(t, unity.x);
                cY.AddKey(t, unity.y);
                cZ.AddKey(t, unity.z);
                cW.AddKey(t, unity.w);
            }

            W3DAnimationBuilder.MakeLinear(cX);
            W3DAnimationBuilder.MakeLinear(cY);
            W3DAnimationBuilder.MakeLinear(cZ);
            W3DAnimationBuilder.MakeLinear(cW);
            ApplyHoldSegments(quat, cX, cY, cZ, cW);

            clip.SetCurve(path, typeof(Transform), "localRotation.x", cX);
            clip.SetCurve(path, typeof(Transform), "localRotation.y", cY);
            clip.SetCurve(path, typeof(Transform), "localRotation.z", cZ);
            clip.SetCurve(path, typeof(Transform), "localRotation.w", cW);
        }

        // Compressed bit channel: value at each timecode holds until the next timecode
        // (client eval: "last entry with frame <= current"; W3D_FORMAT.md §10.2).
        private static void BuildVisibilityCurve(AnimationClip clip, string path,
            W3DCompressedBitChannel bit, float frameDuration)
        {
            if (bit.TimeCodes.Count == 0)
                return;

            var curve = new AnimationCurve();
            foreach (var datum in bit.TimeCodes)
            {
                var key = new Keyframe(datum.TimeCode * frameDuration, datum.Value ? 1f : 0f);
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

            W3DAnimationBuilder.MakeLinear(curve);
            ApplyHoldSegments(ch, curve);
            return curve;
        }

        // A key with NoInterp set means "hold the previous key until this one": make the
        // preceding segment stepped by setting infinite tangents across it.
        private static void ApplyHoldSegments(W3DTimeCodedChannel ch, params AnimationCurve[] curves)
        {
            if (ch.NoInterp.Count == 0)
                return;

            foreach (var curve in curves)
            {
                if (curve == null || curve.length < 2)
                    continue;

                var keys = curve.keys;
                int count = Mathf.Min(keys.Length, ch.NoInterp.Count);
                bool changed = false;
                for (int i = 1; i < count; i++)
                {
                    if (!ch.NoInterp[i])
                        continue;
                    keys[i - 1].outTangent = float.PositiveInfinity;
                    keys[i].inTangent = float.PositiveInfinity;
                    changed = true;
                }

                if (changed)
                    curve.keys = keys;
            }
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
