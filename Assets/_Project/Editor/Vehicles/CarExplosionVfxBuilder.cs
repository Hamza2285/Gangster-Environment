using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Builds the layered car explosion (Assets/_Project/Prefabs/Vehicles/Effects/VFX_CarExplosion.prefab) and tunes the engine fire
    /// variant. Sources: HQ Explosions Pack FREE (fireball flipbook, re-materialed for URP) and Vefects Free Fire VFX URP (glow
    /// texture, smoke through our VFX_CarSmokeHeavy variant). Neither package is edited.
    /// Layers, all one-shot bursts, ~90 particles at most:
    /// - Flash: one big additive glow, 0.18 s. Shockwave: a ground-level additive ring growing to ~20 m in 0.4 s.
    /// - Fire core: 6 additive orange glows that swell and rise for ~0.7 s (reads as a solid fireball under LDR).
    /// - Fireballs: 6 flipbook particles with random rotation, growth and a slight rise; orange to dark over life.
    /// - Sparks (stretched, gravity) and embers (slow, rising); debris: 10 dark cubes thrown up, colliding with the world.
    /// - Smoke column: a nested, scaled-up VFX_CarSmokeHeavy that rises while the effect plays, then fades.
    /// - One point light flashed by <see cref="PooledVehicleEffect"/> (0.25 s) and the explosion sound source.
    /// Re-runnable: rebuilds the prefab in place (same asset, same GUID). Menu: Tools > OpenworldCrime > Vehicles > Build Car Explosion VFX.
    /// </summary>
    public static class CarExplosionVfxBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs/Vehicles/Effects/";
        private const string MaterialFolder = "Assets/_Project/Materials/Vehicles/Effects/";
        private const string ExplosionPath = PrefabFolder + "VFX_CarExplosion.prefab";
        private const string FirePath = PrefabFolder + "VFX_CarEngineFire.prefab";
        private const string HeavySmokePath = PrefabFolder + "VFX_CarSmokeHeavy.prefab";
        private const string FireballTexture = "Assets/HQ Explosions Pack FREE/Textures/Explosion01_Tex.png";
        private const string GlowTexture = "Assets/Vefects/Free Fire VFX URP/Textures/T_VFX_Glow_01.tga";

        [MenuItem("Tools/OpenworldCrime/Vehicles/Build Car Explosion VFX")]
        public static void Build()
        {
            Material fireball = ParticleMaterial("M_CarExplosion_Fireball_URP", FireballTexture, false, Color.white);
            Material glow = ParticleMaterial("M_CarFX_Glow_Additive", GlowTexture, true, Color.white);
            Material debris = ParticleMaterial("M_CarFX_Debris", null, false, new Color(0.05f, 0.045f, 0.04f, 1f), true);

            // Built in a preview scene so the open scene is never touched.
            var preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("VFX_CarExplosion");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                BuildFlash(root.transform, glow);
                BuildShockwave(root.transform, glow);
                BuildFireCore(root.transform, glow);
                BuildFireballs(root.transform, fireball);
                BuildSparks(root.transform, glow);
                BuildEmbers(root.transform, glow);
                BuildDebris(root.transform, debris);
                BuildSmokeColumn(root.transform);

                var lightObject = new GameObject("FlashLight");
                lightObject.transform.SetParent(root.transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 2f, 0f);
                Light flash = lightObject.AddComponent<Light>();
                flash.type = LightType.Point;
                flash.range = 22f;
                flash.color = new Color(1f, 0.62f, 0.3f);
                flash.shadows = LightShadows.None;
                flash.renderMode = LightRenderMode.ForcePixel;
                flash.enabled = false;

                AudioSource audio = root.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.loop = false;
                audio.spatialBlend = 1f;
                audio.minDistance = 8f;
                audio.maxDistance = 150f;
                audio.rolloffMode = AudioRolloffMode.Logarithmic;
                audio.dopplerLevel = 0f;

                PooledVehicleEffect pooled = root.AddComponent<PooledVehicleEffect>();
                var so = new SerializedObject(pooled);
                so.FindProperty("oneShotSource").objectReferenceValue = audio;
                so.FindProperty("flashLight").objectReferenceValue = flash;
                so.FindProperty("flashIntensity").floatValue = 12f;
                so.FindProperty("flashDuration").floatValue = 0.25f;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, ExplosionPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }

            TuneEngineFire();
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(CarExplosionVfxBuilder)}] Built {ExplosionPath} and tuned {FirePath}.");
        }

        private static void BuildFlash(Transform parent, Material glow)
        {
            ParticleSystem ps = NewSystem(parent, "Flash", glow, new Vector3(0f, 1.2f, 0f), 2);
            var main = ps.main;
            main.startLifetime = 0.18f;
            main.startSize = new ParticleSystem.MinMaxCurve(14f, 18f);
            main.startColor = new Color(1f, 0.9f, 0.7f, 1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Burst(ps, 2);
            SizeOverLife(ps, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.2f));
            ColorOverLife(ps, new[] { Key(new Color(1f, 0.95f, 0.8f), 0f), Key(new Color(1f, 0.55f, 0.2f), 1f) }, new[] { Alpha(1f, 0f), Alpha(0f, 1f) });
            ps.GetComponent<ParticleSystemRenderer>().sortingFudge = -20f;
        }

        private static void BuildShockwave(Transform parent, Material glow)
        {
            ParticleSystem ps = NewSystem(parent, "Shockwave", glow, new Vector3(0f, 0.25f, 0f), 1);
            var main = ps.main;
            main.startLifetime = 0.45f;
            main.startSize = 20f;
            main.startColor = new Color(1f, 0.6f, 0.3f, 0.7f);
            Burst(ps, 1);
            SizeOverLife(ps, new AnimationCurve(new Keyframe(0f, 0.1f, 0f, 3f), new Keyframe(1f, 1f)));
            ColorOverLife(ps, new[] { Key(Color.white, 0f), Key(new Color(0.6f, 0.4f, 0.3f), 1f) }, new[] { Alpha(0.8f, 0f), Alpha(0f, 1f) });
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.sortingFudge = -5f;
        }

        private static void BuildFireCore(Transform parent, Material glow)
        {
            ParticleSystem ps = NewSystem(parent, "FireCore", glow, new Vector3(0f, 1.2f, 0f), 8);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Additive over a bright LDR scene: keep green low or orange turns yellow-white.
            main.startColor = new Color(1f, 0.4f, 0.08f, 1f);
            main.gravityModifier = -0.2f;
            Burst(ps, 6);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 1.2f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            SizeOverLife(ps, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.4f));
            ColorOverLife(ps, new[] { Key(new Color(1f, 0.65f, 0.3f), 0f), Key(new Color(0.9f, 0.3f, 0.05f), 0.4f), Key(new Color(0.4f, 0.06f, 0f), 1f) },
                          new[] { Alpha(1f, 0f), Alpha(0.9f, 0.5f), Alpha(0f, 1f) });
            ps.GetComponent<ParticleSystemRenderer>().sortingFudge = -12f;
        }

        private static void BuildFireballs(Transform parent, Material fireball)
        {
            ParticleSystem ps = NewSystem(parent, "Fireballs", fireball, new Vector3(0f, 1.3f, 0f), 8);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(18f, 24f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.12f;
            Burst(ps, 6);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 1.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            SizeOverLife(ps, AnimationCurve.EaseInOut(0f, 0.75f, 1f, 1.2f));
            // Neutral dark at the end: the flipbook's last smoke frames are pinkish brown under Gamma / LDR.
            ColorOverLife(ps, new[] { Key(new Color(1f, 0.85f, 0.6f), 0f), Key(new Color(0.8f, 0.7f, 0.6f), 0.35f), Key(new Color(0.28f, 0.27f, 0.26f), 1f) },
                          new[] { Alpha(1f, 0f), Alpha(1f, 0.6f), Alpha(0f, 1f) });
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 8;
            sheet.numTilesY = 8;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.cycleCount = 1;
            // Skip the first row (tiny sparks) and the washed-out last rows: fire -> dark smoke only.
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.12f, 1f, 0.72f));
            ps.GetComponent<ParticleSystemRenderer>().sortingFudge = -10f;
        }

        private static void BuildSparks(Transform parent, Material glow)
        {
            ParticleSystem ps = NewSystem(parent, "Sparks", glow, new Vector3(0f, 1f, 0f), 45);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 16f);
            main.startColor = new Color(1f, 0.8f, 0.45f, 1f);
            main.gravityModifier = 1.2f;
            Burst(ps, 40);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            ColorOverLife(ps, new[] { Key(new Color(1f, 0.9f, 0.6f), 0f), Key(new Color(1f, 0.35f, 0.08f), 1f) }, new[] { Alpha(1f, 0f), Alpha(1f, 0.7f), Alpha(0f, 1f) });
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 1.5f;
            renderer.sortingFudge = -15f;
        }

        private static void BuildEmbers(Transform parent, Material glow)
        {
            ParticleSystem ps = NewSystem(parent, "Embers", glow, new Vector3(0f, 1.5f, 0f), 25);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
            main.startColor = new Color(1f, 0.55f, 0.15f, 1f);
            main.gravityModifier = -0.08f;
            Burst(ps, 22);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.5f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 0.6f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            ColorOverLife(ps, new[] { Key(Color.white, 0f), Key(new Color(1f, 0.3f, 0.05f), 1f) }, new[] { Alpha(1f, 0f), Alpha(0.8f, 0.6f), Alpha(0f, 1f) });
        }

        private static void BuildDebris(Transform parent, Material debris)
        {
            ParticleSystem ps = NewSystem(parent, "Debris", debris, new Vector3(0f, 1f, 0f), 12);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 3.5f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.12f, 0.45f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.12f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 12f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 2f;
            Burst(ps, 10);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = 0.8f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
            spin.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
            spin.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Low;
            collision.bounce = 0.3f;
            collision.dampen = 0.4f;
            collision.lifetimeLoss = 0f;
            collision.collidesWith = 1; // Default layer: roads, pavements, buildings
            collision.maxCollisionShapes = 64;
            SizeOverLife(ps, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f)));
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            renderer.alignment = ParticleSystemRenderSpace.Local;
        }

        private static void BuildSmokeColumn(Transform parent)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(HeavySmokePath);
            var smoke = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            smoke.name = "SmokeColumn";
            smoke.transform.localPosition = new Vector3(0f, 1f, 0f);
            smoke.transform.localScale = Vector3.one * 2.2f;
            // The nested smoke is part of this effect, not an effect of its own.
            PooledVehicleEffect nested = smoke.GetComponent<PooledVehicleEffect>();
            if (nested != null) Object.DestroyImmediate(nested);
            // Rises after the fireball, from nothing (the car smoke prewarms; the blast must not).
            foreach (ParticleSystem part in smoke.GetComponentsInChildren<ParticleSystem>(true))
            {
                var partMain = part.main;
                partMain.prewarm = false;
                partMain.startDelay = 0.3f;
            }
            Transform column = smoke.transform.Find("Smoke");
            if (column != null && column.TryGetComponent(out ParticleSystem ps))
            {
                var emission = ps.emission;
                emission.rateOverTime = 12f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
                var main = ps.main;
                main.maxParticles = 50;
                main.startColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            }
            Transform billow = smoke.transform.Find("Burning");
            if (billow != null && billow.TryGetComponent(out ParticleSystem bp))
            {
                var emission = bp.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });
            }
        }

        // Engine fire: bigger, redder, wider at the base so it rises from the whole hood.
        private static void TuneEngineFire()
        {
            GameObject fire = PrefabUtility.LoadPrefabContents(FirePath);
            try
            {
                fire.transform.localScale = Vector3.one * 1.7f;
                foreach (string name in new[] { "Flames", "Flames Secondary" })
                {
                    Transform child = fire.transform.Find(name);
                    if (child == null || !child.TryGetComponent(out ParticleSystem ps)) continue;
                    var shape = ps.shape;
                    shape.radius = 0.32f;
                    var emission = ps.emission;
                    emission.rateOverTime = name == "Flames" ? 24f : 20f;
                }
                PrefabUtility.SaveAsPrefabAsset(fire, FirePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(fire);
            }

            // LDR / Gamma: a high emission clips the orange gradient to yellow. Keep it low and lean red.
            SetFireMaterial("M_CarFX_Fire_01", 1.8f);
            SetFireMaterial("M_CarFX_Fire_02", 1.5f);
        }

        private static void SetFireMaterial(string name, float emission)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + name + ".mat");
            if (material == null) return;
            material.SetFloat("_EmissionIntensity", emission);
            material.SetColor("_Color", new Color(1f, 0.62f, 0.38f, 0f));
            EditorUtility.SetDirty(material);
        }

        private static ParticleSystem NewSystem(Transform parent, string name, Material material, Vector3 position, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = true;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSpeed = 0f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 10f;
            return ps;
        }

        private static void Burst(ParticleSystem ps, int count)
        {
            var emission = ps.emission;
            emission.enabled = true;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        private static void SizeOverLife(ParticleSystem ps, AnimationCurve curve)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static void ColorOverLife(ParticleSystem ps, GradientColorKey[] colors, GradientAlphaKey[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(colors, alphas);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static GradientColorKey Key(Color color, float time) => new GradientColorKey(color, time);
        private static GradientAlphaKey Alpha(float alpha, float time) => new GradientAlphaKey(alpha, time);

        // URP Particles/Unlit: alpha blended or additive (black = nothing, so RGB-only glow textures work), or opaque (debris).
        private static Material ParticleMaterial(string name, string texturePath, bool additive, Color color, bool opaque = false)
        {
            string path = MaterialFolder + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", texturePath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) : null);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_ColorMode", 0f);
            material.SetFloat("_SoftParticlesEnabled", 0f);
            if (opaque)
            {
                material.SetFloat("_Surface", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.SetFloat("_ZWrite", 1f);
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = (int)RenderQueue.Geometry;
            }
            else
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", additive ? 2f : 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                material.SetFloat("_SrcBlendAlpha", 1f);
                material.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
