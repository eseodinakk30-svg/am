// -----------------------------------------------------------------------------
//  NEBULA NINE - automatic project configuration.
//
//  This runs once when the project is first opened (and can be re-run from the
//  "Nebula Nine/Setup Project" menu).  It creates the URP pipeline assets for the
//  four quality tiers, wires them into Graphics/Quality settings, configures the
//  Android player settings and registers the Boot scene in the build settings.
//
//  Everything the game needs at runtime is generated procedurally, so this is the
//  only editor-side setup required: open the project, press Play.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nebula.EditorTools
{
    [InitializeOnLoad]
    public static class NebulaProjectSetup
    {
        public const string QualityFolder = "Assets/Resources/Quality";
        public const string MaterialFolder = "Assets/Resources/Materials";
        public const string SettingsFolder = "Assets/Settings";
        public const string BootScenePath = "Assets/Scenes/Boot.unity";
        private const string SetupVersionKey = "Nebula.SetupVersion";
        private const int SetupVersion = 5;

        static NebulaProjectSetup()
        {
            // Delay so the asset database is fully ready.
            EditorApplication.delayCall += MaybeRunAutoSetup;
        }

        private static void MaybeRunAutoSetup()
        {
            if (EditorPrefs.GetInt(ProjectKey(SetupVersionKey), 0) >= SetupVersion &&
                GraphicsSettings.defaultRenderPipeline != null)
                return;

            RunSetup(false);
        }

        [MenuItem("Nebula Nine/Setup Project", false, 0)]
        public static void RunSetupMenu() => RunSetup(true);

        public static void RunSetup(bool verbose)
        {
            try
            {
                EnsureFolders();
                var assets = EnsurePipelineAssets();
                AssignPipeline(assets);
                EnsureRuntimeMaterials();
                EnsureAlwaysIncludedShaders();
                ConfigurePlayerSettings();
                ConfigureQualityDefaults();
                EnsureBuildSettings();
                EnsureTagsAndLayers();

                AssetDatabase.SaveAssets();
                EditorPrefs.SetInt(ProjectKey(SetupVersionKey), SetupVersion);
                Debug.Log("[Nebula Nine] Project setup complete. Open Assets/Scenes/Boot.unity and press Play.");
            }
            catch (Exception e)
            {
                Debug.LogError("[Nebula Nine] Automatic setup failed: " + e +
                               "\nThe game still runs (it falls back to the built-in render pipeline shaders), " +
                               "but for the intended look assign a URP asset in Project Settings > Graphics.");
            }
        }

        private static string ProjectKey(string key) => key + "." + Application.dataPath.GetHashCode();

        // ------------------------------------------------------------------ folders
        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder(QualityFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(SettingsFolder);
            EnsureFolder("Assets/Scenes");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ------------------------------------------------------------------ pipeline
        private struct Tier
        {
            public string Name;
            public int Msaa;
            public float RenderScale;
            public float ShadowDistance;
            public int Cascades;
            public bool Hdr;
            public bool SoftShadows;
            public int ShadowRes;
            public int AdditionalLights;
            public bool DepthTexture;
        }

        private static readonly Tier[] Tiers =
        {
            new Tier { Name = "URP_Low",    Msaa = 1, RenderScale = 0.75f, ShadowDistance = 18f, Cascades = 1, Hdr = false, SoftShadows = false, ShadowRes = 512,  AdditionalLights = 4,  DepthTexture = false },
            new Tier { Name = "URP_Medium", Msaa = 2, RenderScale = 0.9f,  ShadowDistance = 28f, Cascades = 1, Hdr = false, SoftShadows = false, ShadowRes = 1024, AdditionalLights = 8,  DepthTexture = false },
            new Tier { Name = "URP_High",   Msaa = 4, RenderScale = 1.0f,  ShadowDistance = 42f, Cascades = 2, Hdr = true,  SoftShadows = true,  ShadowRes = 2048, AdditionalLights = 16, DepthTexture = true  },
            new Tier { Name = "URP_Ultra",  Msaa = 4, RenderScale = 1.0f,  ShadowDistance = 60f, Cascades = 4, Hdr = true,  SoftShadows = true,  ShadowRes = 2048, AdditionalLights = 32, DepthTexture = true  },
        };

        private static List<RenderPipelineAsset> EnsurePipelineAssets()
        {
            var result = new List<RenderPipelineAsset>();
            foreach (var tier in Tiers)
            {
                var path = QualityFolder + "/" + tier.Name + ".asset";
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null)
                {
                    var rendererPath = SettingsFolder + "/" + tier.Name + "_Renderer.asset";
                    var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
                    if (rendererData == null)
                    {
                        rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                        AssetDatabase.CreateAsset(rendererData, rendererPath);
                    }

                    asset = UniversalRenderPipelineAsset.Create(rendererData);
                    asset.name = tier.Name;
                    AssetDatabase.CreateAsset(asset, path);
                }

                TuneAsset(asset, tier);
                EditorUtility.SetDirty(asset);
                result.Add(asset);
            }

            AssetDatabase.SaveAssets();
            return result;
        }

        /// <summary>
        /// Field names are addressed through SerializedObject so the script keeps
        /// compiling across URP minor versions where public setters come and go.
        /// </summary>
        private static void TuneAsset(UniversalRenderPipelineAsset asset, Tier tier)
        {
            var so = new SerializedObject(asset);
            SetInt(so, "m_MSAA", tier.Msaa);
            SetFloat(so, "m_RenderScale", tier.RenderScale);
            SetFloat(so, "m_ShadowDistance", tier.ShadowDistance);
            SetInt(so, "m_ShadowCascadeCount", tier.Cascades);
            SetBool(so, "m_SupportsHDR", tier.Hdr);
            SetBool(so, "m_SoftShadowsSupported", tier.SoftShadows);
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetBool(so, "m_AdditionalLightShadowsSupported", tier.ShadowRes >= 1024);
            SetInt(so, "m_MainLightShadowmapResolution", tier.ShadowRes);
            SetInt(so, "m_AdditionalLightsShadowmapResolution", Mathf.Max(512, tier.ShadowRes / 2));
            SetInt(so, "m_AdditionalLightsPerObjectLimit", Mathf.Min(8, tier.AdditionalLights));
            SetInt(so, "m_MaxAdditionalLights", tier.AdditionalLights);
            SetBool(so, "m_RequireDepthTexture", tier.DepthTexture);
            SetBool(so, "m_RequireOpaqueTexture", false);
            SetBool(so, "m_UseSRPBatcher", true);
            SetBool(so, "m_SupportsDynamicBatching", false);
            SetInt(so, "m_ColorGradingMode", tier.Hdr ? 1 : 0);
            SetInt(so, "m_ColorGradingLutSize", tier.Hdr ? 32 : 16);
            SetFloat(so, "m_ShadowDepthBias", 1f);
            SetFloat(so, "m_ShadowNormalBias", 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(SerializedObject so, string prop, int v)
        {
            var p = so.FindProperty(prop);
            if (p != null && p.propertyType == SerializedPropertyType.Integer) p.intValue = v;
            else if (p != null && p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = v;
        }

        private static void SetFloat(SerializedObject so, string prop, float v)
        {
            var p = so.FindProperty(prop);
            if (p != null && p.propertyType == SerializedPropertyType.Float) p.floatValue = v;
        }

        private static void SetBool(SerializedObject so, string prop, bool v)
        {
            var p = so.FindProperty(prop);
            if (p != null && p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v;
        }

        private static void AssignPipeline(List<RenderPipelineAsset> assets)
        {
            if (assets == null || assets.Count == 0) return;
            // Index 2 == URP_High is the editor default; the runtime QualityManager
            // swaps assets on the fly according to the player's choice.
            var chosen = assets[Mathf.Min(2, assets.Count - 1)];
            GraphicsSettings.defaultRenderPipeline = chosen;
            QualitySettings.renderPipeline = null; // per-level override off -> use default
        }

        // ------------------------------------------------------------------ materials
        // Игра не хранит бинарных ассетов и лепит материалы в рантайме. Но чтобы
        // нужные варианты шейдеров вообще попали в сборку, где-то должен лежать
        // материал-образец: Art клонирует его вместо того, чтобы собирать материал
        // из голого Shader.Find.
        private static void EnsureRuntimeMaterials()
        {
            EnsureFolder(MaterialFolder);

            MakeMaterial("NB_Lit", "Universal Render Pipeline/Simple Lit",
                         "Universal Render Pipeline/Lit", "Standard", "Diffuse");

            var emissive = MakeMaterial("NB_LitEmissive", "Universal Render Pipeline/Simple Lit",
                                        "Universal Render Pipeline/Lit", "Standard", "Diffuse");
            if (emissive != null)
            {
                emissive.EnableKeyword("_EMISSION");
                emissive.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (emissive.HasProperty("_EmissionColor")) emissive.SetColor("_EmissionColor", Color.white);
                EditorUtility.SetDirty(emissive);
            }

            MakeMaterial("NB_Unlit", "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default");

            var fade = MakeMaterial("NB_UnlitFade", "Universal Render Pipeline/Unlit",
                                    "Unlit/Color", "Sprites/Default");
            if (fade != null)
            {
                if (fade.HasProperty("_Surface")) fade.SetFloat("_Surface", 1f);
                if (fade.HasProperty("_Blend")) fade.SetFloat("_Blend", 0f);
                if (fade.HasProperty("_ZWrite")) fade.SetFloat("_ZWrite", 0f);
                fade.SetOverrideTag("RenderType", "Transparent");
                fade.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (fade.HasProperty("_SrcBlend")) fade.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (fade.HasProperty("_DstBlend")) fade.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                fade.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                EditorUtility.SetDirty(fade);
            }

            MakeMaterial("NB_Particle", "Universal Render Pipeline/Particles/Unlit",
                         "Particles/Standard Unlit", "Sprites/Default");
        }

        /// <summary>Создаёт (или чинит) материал-образец на первом найденном шейдере из списка.</summary>
        private static Material MakeMaterial(string name, params string[] shaderNames)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            Shader shader = null;
            foreach (var n in shaderNames)
            {
                shader = Shader.Find(n);
                if (shader != null) break;
            }
            if (shader == null) return existing;

            if (existing != null)
            {
                if (existing.shader != shader)
                {
                    existing.shader = shader;
                    EditorUtility.SetDirty(existing);
                }
                return existing;
            }

            var created = new Material(shader) { name = name, enableInstancing = true };
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void EnsureAlwaysIncludedShaders()
        {
            // Шейдер из этого списка попадает в сборку СО ВСЕМИ вариантами.
            // У Universal Render Pipeline/Lit их 1 179 648, и плеер отказывается
            // собираться. Поэтому здесь остаются только дешёвые шейдеры, а
            // остальные приходят в сборку через материалы из Resources, которые
            // тянут за собой лишь те варианты, что действительно используются.
            var names = new[]
            {
                "Sprites/Default",
                "UI/Default",
            };

            var graphicsSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettings == null || graphicsSettings.Length == 0) return;
            var so = new SerializedObject(graphicsSettings[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) return;

            var existing = new HashSet<string>();
            for (int i = 0; i < arr.arraySize; i++)
            {
                var s = arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (s != null) existing.Add(s.name);
            }

            foreach (var n in names)
            {
                if (existing.Contains(n)) continue;
                var shader = Shader.Find(n);
                if (shader == null) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ player
        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "Nebula Interactive";
            PlayerSettings.productName = "Nebula Nine";
            PlayerSettings.bundleVersion = "1.0.0";

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.runInBackground = true;

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.nebulainteractive.nebulanine");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            // Фиксированный, а не Auto: Auto берёт самый свежий установленный
            // уровень, и сборка начинает зависеть от того, что именно докачал
            // Unity Hub на раннере. 34 — то, под что рассчитан 2022.3 LTS.
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;   // UDP multiplayer
            PlayerSettings.Android.androidIsGame = true;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3,
            });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        }

        private static void ConfigureQualityDefaults()
        {
            QualitySettings.vSyncCount = 0;
            QualitySettings.skinWeights = SkinWeights.TwoBones;
            QualitySettings.lodBias = 1.2f;
            QualitySettings.particleRaycastBudget = 64;
            QualitySettings.asyncUploadTimeSlice = 4;
        }

        private static void EnsureBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool found = scenes.Exists(s => s.path == BootScenePath);
            if (!found && File.Exists(BootScenePath))
                scenes.Insert(0, new EditorBuildSettingsScene(BootScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureTagsAndLayers()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (asset == null || asset.Length == 0) return;
            var so = new SerializedObject(asset[0]);
            var layers = so.FindProperty("layers");
            if (layers == null) return;

            AssignLayer(layers, 8, "Walls");
            AssignLayer(layers, 9, "Actors");
            AssignLayer(layers, 10, "Interactables");
            AssignLayer(layers, 11, "Vision");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignLayer(SerializedProperty layers, int index, string name)
        {
            if (index >= layers.arraySize) return;
            var element = layers.GetArrayElementAtIndex(index);
            if (element != null && string.IsNullOrEmpty(element.stringValue)) element.stringValue = name;
        }
    }
}
