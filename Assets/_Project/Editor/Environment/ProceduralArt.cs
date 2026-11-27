using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Small shared helpers for the setup tools (parachute, rooftops, helicopter, airport): URP materials saved as assets,
    /// generated textures / icons saved as PNGs, folders. Re-running a tool updates the same assets (same GUIDs).
    /// </summary>
    public static class ProceduralArt
    {
        public const string LitShader = "Universal Render Pipeline/Lit";
        public const string UnlitShader = "Universal Render Pipeline/Unlit";
        public const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Loads or creates a material asset with the shader (kept if it already exists).</summary>
        public static Material Material(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader) material.shader = shader;
            return material;
        }

        /// <summary>Opaque URP Lit (optionally textured / double sided / emissive).</summary>
        public static Material Lit(string path, Color colour, Texture2D baseMap = null, float smoothness = 0.35f, float metallic = 0f,
            Texture2D normalMap = null, bool doubleSided = false, Color? emission = null)
        {
            Material m = Material(path, LitShader);
            m.SetColor("_BaseColor", colour);
            m.SetTexture("_BaseMap", baseMap);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetTexture("_BumpMap", normalMap);
            if (normalMap != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            m.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            if (emission.HasValue)
            {
                m.SetColor("_EmissionColor", emission.Value);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                m.SetColor("_EmissionColor", Color.black);
                m.DisableKeyword("_EMISSION");
            }
            m.enableInstancing = true;
            SetOpaque(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Transparent URP Lit (alpha blended), e.g. a fading copy of an opaque material.</summary>
        public static Material LitTransparent(string path, Color colour, bool doubleSided = true)
        {
            Material m = Material(path, LitShader);
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", 0.2f);
            m.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            SetTransparent(m, false);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>URP Unlit: opaque emissive-looking cards (runway lights, signs) or transparent / additive glows.</summary>
        public static Material Unlit(string path, Color colour, Texture2D baseMap = null, bool transparent = false, bool additive = false, bool doubleSided = true)
        {
            Material m = Material(path, UnlitShader);
            m.SetColor("_BaseColor", colour);
            m.SetTexture("_BaseMap", baseMap);
            m.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            if (transparent || additive) SetTransparent(m, additive); else SetOpaque(m);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>URP Particles/Unlit, alpha blended or additive.</summary>
        public static Material Particle(string path, Color colour, Texture2D texture, bool additive)
        {
            Material m = Material(path, ParticleShader);
            m.SetColor("_BaseColor", colour);
            m.SetTexture("_BaseMap", texture);
            SetTransparent(m, additive);
            m.SetFloat("_ColorMode", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void SetOpaque(Material m)
        {
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = (int)RenderQueue.Geometry;
        }

        private static void SetTransparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        // ---------------------------------------------------------------- textures

        public delegate Color PixelFunction(float u, float v);

        /// <summary>Draws a texture from a pixel function and saves it as a PNG asset (sprite or texture, repeat or clamp).</summary>
        public static Texture2D SaveTexture(string path, int width, int height, PixelFunction pixel, bool sprite = false, bool repeat = false, int maxSize = 512)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = pixel((x + 0.5f) / width, (y + 0.5f) / height);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool changed = false;
            TextureImporterType type = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (importer.textureType != type) { importer.textureType = type; changed = true; }
            if (sprite && importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }
            TextureWrapMode wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (importer.wrapMode != wrap) { importer.wrapMode = wrap; changed = true; }
            if (importer.maxTextureSize != maxSize) { importer.maxTextureSize = maxSize; changed = true; }
            if (importer.mipmapEnabled != repeat) { importer.mipmapEnabled = repeat; changed = true; }
            if (changed) importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Soft round dot (particles, glows).</summary>
        public static Texture2D SoftDot(string path)
        {
            return SaveTexture(path, 64, 64, (u, v) =>
            {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                float a = Mathf.Clamp01(1f - d);
                return new Color(1f, 1f, 1f, a * a);
            });
        }

        /// <summary>Round button / map icon: a dark disc with a white rim and a white glyph drawn by <paramref name="glyph"/> (u, v in -1..1).</summary>
        public static Texture2D Icon(string path, System.Func<float, float, float> glyph, Color disc, int size = 256)
        {
            return SaveTexture(path, size, size, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y);
                float aa = 2.5f / size;
                float inside = Mathf.Clamp01((0.98f - r) / aa);
                if (inside <= 0f) return new Color(0f, 0f, 0f, 0f);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.9f) / 0.04f);
                float g = Mathf.Clamp01(glyph(x, y));
                float white = Mathf.Max(rim, g);
                Color c = Color.Lerp(disc, Color.white, white);
                c.a = Mathf.Lerp(disc.a, 1f, white) * inside;
                return c;
            }, sprite: true);
        }

        /// <summary>Glyph helper: 1 inside a segment of the given half width (u, v in -1..1), soft edged.</summary>
        public static float Segment(float x, float y, Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 p = new Vector2(x, y), ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            float d = Vector2.Distance(p, a + ab * t);
            return Mathf.Clamp01((halfWidth - d) / 0.012f);
        }
    }
}
