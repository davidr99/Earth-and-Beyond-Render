using System.Collections.Generic;
using UnityEngine;
using W3DImporter.Structs.Animation;
using W3DImporter.Structs.Hierarchy;

namespace W3DImporter.Builders
{
    // Converts W3D uncompressed animation (chunk 0x200) into a Unity AnimationClip.
    // Uncompressed layout: channels 0/1/2 are scalar TX/TY/TZ; channel 6 with VectorLen=4
    // carries a full quaternion per frame. Visibility rides on W3DAnimationBitChannel
    // (channel 15). Coordinate conversion is applied per frame so the resulting Unity
    // curves line up with the already-converted base pose written by BuildHierarchy.
    //
    // Channel values are DELTAS applied on top of the pivot's base pose, as in WW3D
    // (pivot = parent * base * Translate(t) * Rotate(q)); a frame outside a channel's
    // range contributes zero translation / identity rotation. In the game files the
    // first-frame translation is ~0 for pivots whose base translation is not.
    public static class W3DAnimationBuilder
    {
        // Animations for pre-3.0 hierarchies index pivots without the implicit root pivot
        // that the client (and W3DHierarchyParser) inserts.
        internal static int PivotShift(uint animationVersion)
        {
            return animationVersion < 0x30000 ? 1 : 0;
        }

        internal static Vector3 ComposeTranslation(W3DHierarchyPivot pivot, Vector3 delta)
        {
            return W3DCoordinateConverter.ConvertPosition(pivot.Translation + pivot.Rotation * delta);
        }

        internal static Quaternion ComposeRotation(W3DHierarchyPivot pivot, Quaternion delta)
        {
            return W3DCoordinateConverter.ConvertRotation(pivot.Rotation * delta);
        }

        // WW3D interpolates linearly (slerp for rotations) between keys.
        internal static void MakeLinear(AnimationCurve curve)
        {
            if (curve == null || curve.length < 2)
                return;

            var keys = curve.keys;
            for (int i = 0; i + 1 < keys.Length; i++)
            {
                float dt = keys[i + 1].time - keys[i].time;
                float slope = dt > 0f ? (keys[i + 1].value - keys[i].value) / dt : 0f;
                keys[i].outTangent = slope;
                keys[i + 1].inTangent = slope;
            }
            keys[0].inTangent = keys[0].outTangent;
            keys[keys.Length - 1].outTangent = keys[keys.Length - 1].inTangent;
            curve.keys = keys;
        }
        public static AnimationClip Build(W3DAnimation anim, W3DHierarchy hierarchy)
        {
            if (anim?.Header == null || hierarchy == null || hierarchy.Pivots.Count == 0)
                return null;

            float frameRate = anim.Header.FrameRate > 0f ? anim.Header.FrameRate : 30f;
            float frameDuration = 1f / frameRate;

            var clip = new AnimationClip
            {
                name = string.IsNullOrEmpty(anim.Header.Name) ? "W3DAnimation" : anim.Header.Name,
                frameRate = frameRate,
                legacy = true
            };

            var bonePaths = BuildBonePaths(hierarchy);
            int pivotShift = PivotShift(anim.Header.Version);

            var byPivot = new Dictionary<int, List<W3DAnimationChannel>>();
            foreach (var ch in anim.Channels)
            {
                int pivotIndex = ch.Pivot + pivotShift;
                if (!byPivot.TryGetValue(pivotIndex, out var list))
                {
                    list = new List<W3DAnimationChannel>();
                    byPivot[pivotIndex] = list;
                }
                list.Add(ch);
            }

            foreach (var kvp in byPivot)
            {
                if (!bonePaths.TryGetValue(kvp.Key, out var path))
                    continue;

                var pivot = hierarchy.Pivots[kvp.Key];
                W3DAnimationChannel tx = null, ty = null, tz = null, quat = null;

                foreach (var c in kvp.Value)
                {
                    switch (c.ChannelType)
                    {
                        case W3DUncompressedChannelTypes.TranslationX: tx = c; break;
                        case W3DUncompressedChannelTypes.TranslationY: ty = c; break;
                        case W3DUncompressedChannelTypes.TranslationZ: tz = c; break;
                        case W3DUncompressedChannelTypes.Quaternion:   quat = c; break;
                    }
                }

                if (tx != null || ty != null || tz != null)
                    BuildTranslationCurves(clip, path, tx, ty, tz, pivot, frameDuration);

                if (quat != null)
                    BuildRotationCurves(clip, path, quat, pivot, frameDuration);
            }

            foreach (var bit in anim.BitChannels)
            {
                if (bit.ChannelType != W3DBitChannelTypes.Visibility)
                    continue;
                if (!bonePaths.TryGetValue(bit.Pivot + pivotShift, out var path))
                    continue;
                BuildVisibilityCurve(clip, path, bit, frameDuration);
            }

            clip.EnsureQuaternionContinuity();
            return clip;
        }

        private static Dictionary<int, string> BuildBonePaths(W3DHierarchy hierarchy)
        {
            var paths = new Dictionary<int, string>();
            paths[0] = ""; // pivot 0 drives the root GameObject itself

            for (int i = 1; i < hierarchy.Pivots.Count; i++)
            {
                var pivot = hierarchy.Pivots[i];
                string parentPath = paths.TryGetValue(pivot.ParentId, out var p) ? p : "";
                paths[i] = string.IsNullOrEmpty(parentPath) ? pivot.Name : parentPath + "/" + pivot.Name;
            }

            return paths;
        }

        private static void BuildTranslationCurves(AnimationClip clip, string path,
            W3DAnimationChannel tx, W3DAnimationChannel ty, W3DAnimationChannel tz,
            W3DHierarchyPivot pivot, float frameDuration)
        {
            int first = int.MaxValue, last = int.MinValue;
            Accumulate(tx, ref first, ref last);
            Accumulate(ty, ref first, ref last);
            Accumulate(tz, ref first, ref last);
            if (first > last) return;

            var curveX = new AnimationCurve();
            var curveY = new AnimationCurve();
            var curveZ = new AnimationCurve();

            for (int frame = first; frame <= last; frame++)
            {
                var delta = new Vector3(
                    SampleScalar(tx, frame, 0f),
                    SampleScalar(ty, frame, 0f),
                    SampleScalar(tz, frame, 0f));

                Vector3 unity = ComposeTranslation(pivot, delta);
                float time = frame * frameDuration;
                curveX.AddKey(time, unity.x);
                curveY.AddKey(time, unity.y);
                curveZ.AddKey(time, unity.z);
            }

            MakeLinear(curveX);
            MakeLinear(curveY);
            MakeLinear(curveZ);
            clip.SetCurve(path, typeof(Transform), "localPosition.x", curveX);
            clip.SetCurve(path, typeof(Transform), "localPosition.y", curveY);
            clip.SetCurve(path, typeof(Transform), "localPosition.z", curveZ);
        }

        private static void BuildRotationCurves(AnimationClip clip, string path,
            W3DAnimationChannel quat, W3DHierarchyPivot pivot, float frameDuration)
        {
            if (quat.VectorLen != 4 || quat.Data.Count < 4)
                return;

            int frameCount = quat.LastFrame - quat.FirstFrame + 1;
            var curveX = new AnimationCurve();
            var curveY = new AnimationCurve();
            var curveZ = new AnimationCurve();
            var curveW = new AnimationCurve();

            for (int f = 0; f < frameCount; f++)
            {
                int offset = f * 4;
                if (offset + 3 >= quat.Data.Count) break;

                var w3d = new Quaternion(
                    quat.Data[offset + 0],
                    quat.Data[offset + 1],
                    quat.Data[offset + 2],
                    quat.Data[offset + 3]);

                var unity = ComposeRotation(pivot, w3d);
                float time = (quat.FirstFrame + f) * frameDuration;

                curveX.AddKey(time, unity.x);
                curveY.AddKey(time, unity.y);
                curveZ.AddKey(time, unity.z);
                curveW.AddKey(time, unity.w);
            }

            MakeLinear(curveX);
            MakeLinear(curveY);
            MakeLinear(curveZ);
            MakeLinear(curveW);
            clip.SetCurve(path, typeof(Transform), "localRotation.x", curveX);
            clip.SetCurve(path, typeof(Transform), "localRotation.y", curveY);
            clip.SetCurve(path, typeof(Transform), "localRotation.z", curveZ);
            clip.SetCurve(path, typeof(Transform), "localRotation.w", curveW);
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

        private static void Accumulate(W3DAnimationChannel ch, ref int first, ref int last)
        {
            if (ch == null) return;
            if (ch.FirstFrame < first) first = ch.FirstFrame;
            if (ch.LastFrame > last) last = ch.LastFrame;
        }

        private static float SampleScalar(W3DAnimationChannel ch, int frame, float fallback)
        {
            if (ch == null) return fallback;
            if (frame < ch.FirstFrame || frame > ch.LastFrame) return fallback;
            int idx = frame - ch.FirstFrame;
            if (idx < 0 || idx >= ch.Data.Count) return fallback;
            return ch.Data[idx];
        }
    }
}
