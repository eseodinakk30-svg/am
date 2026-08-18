// -----------------------------------------------------------------------------
//  NEBULA NINE - runtime art factory.
//
//  The project ships zero binary assets: every mesh, material, texture and sprite
//  is generated here on first use and cached.  That keeps the repository tiny,
//  makes the whole look tweakable from code, and guarantees the project opens
//  cleanly in any Unity 2022.3 installation.
//
//  All materials are created with GPU instancing enabled and share the same
//  shader, so the station renders in a handful of draw calls (SRP batcher).
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Fx
{
    public static class Art
    {
        // ------------------------------------------------------------------ shaders
        private static Shader _lit, _unlit, _particle, _uiShader;

        public static Shader LitShader
        {
            get
            {
                if (_lit == null)
                {
                    _lit = Shader.Find("Universal Render Pipeline/Lit")
                           ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                           ?? Shader.Find("Standard")
                           ?? Shader.Find("Diffuse");
                }
                return _lit;
            }
        }

        public static Shader UnlitShader
        {
            get
            {
                if (_unlit == null)
                {
                    _unlit = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Unlit/Color")
                             ?? Shader.Find("Sprites/Default");
                }
                return _unlit;
            }
        }

        public static Shader ParticleShader
        {
            get
            {
                if (_particle == null)
                {
                    _particle = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                ?? Shader.Find("Particles/Standard Unlit")
                                ?? UnlitShader;
                }
                return _particle;
            }
        }

        public static Shader UiShader
        {
            get
            {
                if (_uiShader == null) _uiShader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
                return _uiShader;
            }
        }

        private static bool IsUrp => LitShader != null && LitShader.name.StartsWith("Universal");

        // ------------------------------------------------------------------ meshes
        private static Mesh _cube, _sphere, _capsule, _cylinder, _quad, _plane;

        private static Mesh Primitive(PrimitiveType type, ref Mesh cache)
        {
            if (cache != null) return cache;
            var go = GameObject.CreatePrimitive(type);
            var mf = go.GetComponent<MeshFilter>();
            cache = mf != null ? mf.sharedMesh : null;
            Object.Destroy(go);
            return cache;
        }

        public static Mesh Cube => Primitive(PrimitiveType.Cube, ref _cube);
        public static Mesh Sphere => Primitive(PrimitiveType.Sphere, ref _sphere);
        public static Mesh Capsule => Primitive(PrimitiveType.Capsule, ref _capsule);
        public static Mesh Cylinder => Primitive(PrimitiveType.Cylinder, ref _cylinder);
        public static Mesh Quad => Primitive(PrimitiveType.Quad, ref _quad);
        public static Mesh Plane => Primitive(PrimitiveType.Plane, ref _plane);

        // ------------------------------------------------------------------ materials
        private struct MatKey
        {
            public int Rgba;
            public int Metal;
            public int Smooth;
            public int Emission;
            public bool Unlit;
            public bool Transparent;
        }

        private static readonly Dictionary<int, Material> MatCache = new Dictionary<int, Material>();

        private static int HashKey(MatKey k)
        {
            unchecked
            {
                int h = k.Rgba;
                h = h * 397 ^ k.Metal;
                h = h * 397 ^ k.Smooth;
                h = h * 397 ^ k.Emission;
                h = h * 397 ^ (k.Unlit ? 1 : 0);
                h = h * 397 ^ (k.Transparent ? 2 : 0);
                return h;
            }
        }

        private static int Quantise(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255);
            return (r << 24) | (g << 16) | (b << 8) | a;
        }

        /// <summary>Opaque lit material. Cached per (colour, metallic, smoothness, emission).</summary>
        public static Material Lit(Color color, float metallic = 0f, float smoothness = 0.32f, float emission = 0f)
        {
            var key = new MatKey
            {
                Rgba = Quantise(color),
                Metal = Mathf.RoundToInt(metallic * 20f),
                Smooth = Mathf.RoundToInt(smoothness * 20f),
                Emission = Mathf.RoundToInt(emission * 20f),
            };
            int hash = HashKey(key);
            if (MatCache.TryGetValue(hash, out var cached) && cached != null) return cached;

            var m = new Material(LitShader) { name = "NB_Lit_" + hash, enableInstancing = true };
            SetColor(m, color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (emission > 0.001f)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", color * emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            MatCache[hash] = m;
            return m;
        }

        /// <summary>Unlit emissive material - panels, holograms, screens.</summary>
        public static Material Unlit(Color color)
        {
            var key = new MatKey { Rgba = Quantise(color), Unlit = true };
            int hash = HashKey(key);
            if (MatCache.TryGetValue(hash, out var cached) && cached != null) return cached;

            var m = new Material(UnlitShader) { name = "NB_Unlit_" + hash, enableInstancing = true };
            SetColor(m, color);
            MatCache[hash] = m;
            return m;
        }

        /// <summary>Additive-ish transparent material for glows and ghost bodies.</summary>
        public static Material Transparent(Color color)
        {
            var key = new MatKey { Rgba = Quantise(color), Unlit = true, Transparent = true };
            int hash = HashKey(key);
            if (MatCache.TryGetValue(hash, out var cached) && cached != null) return cached;

            var m = new Material(UnlitShader) { name = "NB_Fade_" + hash, enableInstancing = true };
            SetColor(m, color);
            MakeTransparent(m);
            MatCache[hash] = m;
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void MakeTransparent(Material m)
        {
            if (IsUrp)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>Property block helper so tinted instances still batch.</summary>
        public static void Tint(Renderer r, Color c, MaterialPropertyBlock block = null)
        {
            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }

        // ------------------------------------------------------------------ textures & sprites
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
        private static Texture2D _white;

        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color32[16];
                    for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
                    _white.SetPixels32(px);
                    _white.Apply();
                    _white.wrapMode = TextureWrapMode.Clamp;
                }
                return _white;
            }
        }

        public static Sprite SolidSprite()
        {
            const string key = "solid";
            if (SpriteCache.TryGetValue(key, out var s) && s != null) return s;
            s = Sprite.Create(White, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
            SpriteCache[key] = s;
            return s;
        }

        /// <summary>Rounded rectangle sprite with nine-slice borders - the UI workhorse.</summary>
        public static Sprite RoundedRect(int radius = 16, int size = 64, float border = 0f)
        {
            string key = "rr_" + radius + "_" + size + "_" + border.ToString("F2");
            if (SpriteCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float r = Mathf.Clamp(radius, 1, size / 2);

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(r - x - 0.5f, 0f, x + 0.5f - (size - r));
                float dy = Mathf.Max(r - y - 0.5f, 0f, y + 0.5f - (size - r));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d + 0.5f);
                if (r <= 1f) a = 1f;

                float value = 1f;
                if (border > 0.001f)
                {
                    float inner = r - border;
                    float dxi = Mathf.Max(inner - (x + 0.5f - border), 0f, (x + 0.5f - border) - (size - 2 * border - inner));
                    float dyi = Mathf.Max(inner - (y + 0.5f - border), 0f, (y + 0.5f - border) - (size - 2 * border - inner));
                    bool insideBox = x >= border && y >= border && x < size - border && y < size - border;
                    float di = Mathf.Sqrt(dxi * dxi + dyi * dyi);
                    float ia = insideBox ? Mathf.Clamp01(inner - di + 0.5f) : 0f;
                    value = Mathf.Clamp01(a - ia);
                }

                byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(a, value) * 255f), 0, 255);
                px[y * size + x] = new Color32(255, 255, 255, alpha);
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            int b = Mathf.RoundToInt(r + 2);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            SpriteCache[key] = sprite;
            return sprite;
        }

        public static Sprite Circle(int size = 64, float ringWidth = 0f)
        {
            string key = "circ_" + size + "_" + ringWidth.ToString("F2");
            if (SpriteCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float c = size * 0.5f, outer = size * 0.5f - 0.5f;
            float inner = ringWidth > 0.001f ? outer - ringWidth : -1f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                float a = Mathf.Clamp01(outer - d + 0.5f);
                if (inner > 0f) a = Mathf.Min(a, Mathf.Clamp01(d - inner + 0.5f));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            SpriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>Soft radial falloff used for glow, vision masks and light cookies.</summary>
        public static Sprite RadialGlow(int size = 128, float power = 2f)
        {
            string key = "glow_" + size + "_" + power.ToString("F2");
            if (SpriteCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a = Mathf.Pow(a, power);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            SpriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>Triangle / arrow sprite (joystick hints, vote arrows).</summary>
        public static Sprite Triangle(int size = 64)
        {
            const string key = "tri";
            if (SpriteCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float t = y / (float)(size - 1);
                float halfWidth = (1f - t) * 0.5f * size;
                bool inside = Mathf.Abs(x + 0.5f - size * 0.5f) <= halfWidth;
                px[y * size + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            SpriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>Star field / nebula backdrop generated once for the skybox quad.</summary>
        public static Texture2D StarField(int size = 512, int seed = 1337)
        {
            var rnd = new System.Random(seed);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)size, ny = y / (float)size;
                float neb = Mathf.PerlinNoise(nx * 3.1f + 11f, ny * 3.1f + 7f);
                neb = Mathf.Pow(Mathf.Clamp01(neb - 0.35f) * 1.6f, 2f);
                var col = Color.Lerp(new Color(0.02f, 0.025f, 0.05f), new Color(0.10f, 0.06f, 0.22f), neb);
                col = Color.Lerp(col, new Color(0.05f, 0.16f, 0.24f), Mathf.PerlinNoise(nx * 2.2f, ny * 2.2f + 30f) * neb);
                px[y * size + x] = col;
            }

            int stars = size * size / 900;
            for (int i = 0; i < stars; i++)
            {
                int x = rnd.Next(size), y = rnd.Next(size);
                float b = 0.35f + (float)rnd.NextDouble() * 0.65f;
                px[y * size + x] = new Color(b, b, b * (0.9f + (float)rnd.NextDouble() * 0.1f));
            }

            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        // ------------------------------------------------------------------ fonts
        private static Font _font;

        public static Font UiFont
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (_font == null)
                {
                    string[] candidates = { "Roboto", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Arial", "Segoe UI" };
                    _font = Font.CreateDynamicFontFromOSFont(candidates, 32);
                }
                if (_font == null)
                {
                    var os = Font.GetOSInstalledFontNames();
                    if (os != null && os.Length > 0) _font = Font.CreateDynamicFontFromOSFont(os[0], 32);
                }
                return _font;
            }
        }

        // ------------------------------------------------------------------ palette
        public static readonly Color Ink = new Color(0.055f, 0.063f, 0.090f);
        public static readonly Color Panel = new Color(0.098f, 0.114f, 0.157f, 0.96f);
        public static readonly Color PanelSoft = new Color(0.145f, 0.169f, 0.227f, 0.94f);
        public static readonly Color Accent = new Color(0.30f, 0.82f, 0.94f);
        public static readonly Color AccentWarm = new Color(0.98f, 0.72f, 0.30f);
        public static readonly Color Danger = new Color(0.92f, 0.29f, 0.33f);
        public static readonly Color Good = new Color(0.36f, 0.85f, 0.52f);
        public static readonly Color TextMain = new Color(0.92f, 0.94f, 0.98f);
        public static readonly Color TextDim = new Color(0.62f, 0.67f, 0.76f);
    }
}
