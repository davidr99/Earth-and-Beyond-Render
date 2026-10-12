using UnityEngine;
using W3DImporter.Structs.Emitter;

namespace W3DImporter.Builders
{
    public static class W3DEmitterBuilder
    {
        public static GameObject Build(W3DEmitter emitter, Transform parent, W3DTextureResolver textureResolver)
        {
            var go = new GameObject(emitter.Name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            var emission = ps.emission;
            var shape = ps.shape;
            var renderer = go.GetComponent<ParticleSystemRenderer>();

            // Ensure it plays
            main.playOnAwake = true;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Small point emission
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;

            // Create material with our particle shader
            var particleShader = Shader.Find("W3D/Particle")
                                 ?? Shader.Find("Sprites/Default");
            var mat = new Material(particleShader);
            mat.color = Color.white;
            renderer.material = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            float particleSize = 1f;
            float lifetime = 1f;
            float emissionRate = 10f;

            if (emitter.Info != null)
            {
                var info = emitter.Info;
                lifetime = Mathf.Max(info.Lifetime, 0.1f);
                emissionRate = info.EmissionRate > 0 ? info.EmissionRate : 10f;
                particleSize = info.StartSize;
                main.gravityModifier = info.Gravity;

                Color startCol = new Color(
                    info.StartColor.r / 255f,
                    info.StartColor.g / 255f,
                    info.StartColor.b / 255f,
                    info.StartColor.a / 255f);
                if (startCol.a > 0.01f)
                    main.startColor = startCol;
                else
                    main.startColor = Color.white;

                float velMag = info.Velocity.magnitude;
                main.startSpeed = velMag > 0 ? velMag : 0.5f;

                if (info.MaxEmissions > 0)
                    main.maxParticles = (int)info.MaxEmissions;

                // Load texture
                if (!string.IsNullOrEmpty(info.TextureFilename))
                {
                    var tex = textureResolver.Resolve(info.TextureFilename);
                    if (tex != null)
                    {
                        mat.mainTexture = tex;
                        ConfigureTextureSheetAnimation(ps, emitter, tex, lifetime);
                        if (W3DLog.Verbose) Debug.Log($"[W3D] Emitter '{emitter.Name}' texture: {info.TextureFilename} ({tex.width}x{tex.height})");
                    }
                    else
                    {
                        Debug.LogWarning($"[W3D] Emitter '{emitter.Name}' texture not found: {info.TextureFilename}");
                    }
                }
            }

            // Get size from keyframes if StartSize is 0
            if (particleSize <= 0 && emitter.Props != null && emitter.Props.SizeKeyframes.Count > 0)
            {
                foreach (var kf in emitter.Props.SizeKeyframes)
                    particleSize = Mathf.Max(particleSize, kf.Value);
                if (W3DLog.Verbose) Debug.Log($"[W3D] Emitter '{emitter.Name}' size from keyframes: {particleSize}");
            }
            if (particleSize <= 0) particleSize = 1f;

            main.startLifetime = lifetime;
            main.startSize = particleSize;
            emission.rateOverTime = emissionRate;

            // Render mode from V2 info
            if (emitter.InfoV2 != null)
            {
                if (emitter.InfoV2.RenderMode == 2)
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
            }

            // Color/opacity over lifetime
            if (emitter.Props != null && (emitter.Props.ColorKeyframes.Count > 0 || emitter.Props.OpacityKeyframes.Count > 0))
            {
                var colorModule = ps.colorOverLifetime;
                colorModule.enabled = true;

                float maxTime = lifetime;

                GradientColorKey[] colorKeys;
                if (emitter.Props.ColorKeyframes.Count > 0)
                {
                    int colorCount = Mathf.Min(emitter.Props.ColorKeyframes.Count, 8);
                    colorKeys = new GradientColorKey[colorCount];
                    for (int i = 0; i < colorCount; i++)
                    {
                        var kf = emitter.Props.ColorKeyframes[i];
                        float t = Mathf.Clamp01(kf.Time / maxTime);
                        Color c = new Color(kf.Color.r / 255f, kf.Color.g / 255f, kf.Color.b / 255f, kf.Color.a / 255f);
                        colorKeys[i] = new GradientColorKey(c, t);
                    }
                }
                else
                {
                    colorKeys = new GradientColorKey[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f)
                    };
                }

                GradientAlphaKey[] alphaKeys;
                if (emitter.Props.OpacityKeyframes.Count > 0)
                {
                    int alphaCount = Mathf.Min(emitter.Props.OpacityKeyframes.Count, 8);
                    alphaKeys = new GradientAlphaKey[alphaCount];
                    for (int i = 0; i < alphaCount; i++)
                    {
                        var kf = emitter.Props.OpacityKeyframes[i];
                        float t = Mathf.Clamp01(kf.Time / maxTime);
                        alphaKeys[i] = new GradientAlphaKey(Mathf.Clamp01(kf.Value), t);
                    }
                }
                else
                {
                    int colorCount = Mathf.Min(emitter.Props.ColorKeyframes.Count, 8);
                    alphaKeys = new GradientAlphaKey[colorCount];
                    for (int i = 0; i < colorCount; i++)
                    {
                        var kf = emitter.Props.ColorKeyframes[i];
                        float t = Mathf.Clamp01(kf.Time / maxTime);
                        alphaKeys[i] = new GradientAlphaKey(kf.Color.a / 255f, t);
                    }
                }

                var gradient = new Gradient();
                gradient.SetKeys(colorKeys, alphaKeys);
                colorModule.color = gradient;
            }

            // Size over lifetime
            if (emitter.Props != null && emitter.Props.SizeKeyframes.Count > 0)
            {
                var sizeModule = ps.sizeOverLifetime;
                sizeModule.enabled = true;

                var curve = new AnimationCurve();
                float maxSizeTime = lifetime;
                float maxSizeVal = particleSize;
                if (maxSizeVal <= 0) maxSizeVal = 1f;

                foreach (var kf in emitter.Props.SizeKeyframes)
                {
                    float t = Mathf.Clamp01(kf.Time / maxSizeTime);
                    float v = kf.Value / maxSizeVal; // normalize to 0-1 relative to startSize
                    curve.AddKey(t, Mathf.Max(0, v));
                }

                sizeModule.size = new ParticleSystem.MinMaxCurve(1f, curve);
            }

            // Force play
            ps.Play();

            if (W3DLog.Verbose) Debug.Log($"[W3D] Emitter '{emitter.Name}': size={particleSize}, lifetime={lifetime}, rate={emissionRate}, playing={ps.isPlaying}");
            return go;
        }

        private static void ConfigureTextureSheetAnimation(ParticleSystem ps, W3DEmitter emitter, Texture tex, float lifetime)
        {
            int tilesPerAxis = GetTilesPerAxis(emitter.InfoV2?.FrameMode ?? 0);
            if (tilesPerAxis <= 1 || tex == null)
                return;

            int maxFrame = Mathf.Max(1, tilesPerAxis * tilesPerAxis - 1);
            var frameData = emitter.FrameData;
            var module = ps.textureSheetAnimation;
            module.enabled = true;
            module.mode = ParticleSystemAnimationMode.Grid;
            module.animation = ParticleSystemAnimationType.WholeSheet;
            module.numTilesX = tilesPerAxis;
            module.numTilesY = tilesPerAxis;
            module.cycleCount = 1;

            if (frameData != null && frameData.Keyframes.Count > 0)
            {
                var curve = new AnimationCurve();
                for (int i = 0; i < frameData.Keyframes.Count; i++)
                {
                    var kf = frameData.Keyframes[i];
                    float normalizedTime = Mathf.Clamp01(kf.Time / Mathf.Max(0.0001f, lifetime));
                    float normalizedFrame = Mathf.Clamp01(kf.Frame / maxFrame);
                    curve.AddKey(normalizedTime, normalizedFrame);
                }
                module.frameOverTime = new ParticleSystem.MinMaxCurve(1f, curve);
            }
            else
            {
                module.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            }

            if (frameData != null && frameData.Random > 0f)
                module.startFrame = new ParticleSystem.MinMaxCurve(0f, Mathf.Clamp01(frameData.Random / maxFrame));
        }

        private static int GetTilesPerAxis(uint frameMode)
        {
            switch (frameMode)
            {
                case 1: return 2;
                case 2: return 4;
                case 3: return 8;
                case 4: return 16;
                default: return 1;
            }
        }
    }
}
