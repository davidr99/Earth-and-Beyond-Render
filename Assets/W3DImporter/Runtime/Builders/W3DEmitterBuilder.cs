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
                        Debug.Log($"[W3D] Emitter '{emitter.Name}' texture: {info.TextureFilename} ({tex.width}x{tex.height})");
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
                Debug.Log($"[W3D] Emitter '{emitter.Name}' size from keyframes: {particleSize}");
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

            // Color over lifetime
            if (emitter.Props != null && emitter.Props.ColorKeyframes.Count > 0)
            {
                var colorModule = ps.colorOverLifetime;
                colorModule.enabled = true;

                int count = Mathf.Min(emitter.Props.ColorKeyframes.Count, 8);
                var colorKeys = new GradientColorKey[count];
                var alphaKeys = new GradientAlphaKey[count];

                float maxTime = lifetime;
                for (int i = 0; i < count; i++)
                {
                    var kf = emitter.Props.ColorKeyframes[i];
                    float t = Mathf.Clamp01(kf.Time / maxTime);
                    Color c = new Color(kf.Color.r / 255f, kf.Color.g / 255f, kf.Color.b / 255f, kf.Color.a / 255f);
                    colorKeys[i] = new GradientColorKey(c, t);
                    alphaKeys[i] = new GradientAlphaKey(c.a, t);
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

            Debug.Log($"[W3D] Emitter '{emitter.Name}': size={particleSize}, lifetime={lifetime}, rate={emissionRate}, playing={ps.isPlaying}");
            return go;
        }
    }
}
