// -----------------------------------------------------------------------------
//  NEBULA NINE - camera rig, vision culling and the darkness overlay.
//
//  Vision uses exactly the same MatchManager.CanSee call the AI uses, so what the
//  player sees and what an NPC could have seen are guaranteed to be consistent.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Characters;
using Nebula.Core;
using Nebula.Fx;
using Nebula.Map;

namespace Nebula.Gameplay
{
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam;
        public Transform Target;

        private float _height = 26f;
        private float _pitch = 62f;
        private float _targetHeight = 26f;
        private Vector3 _velocity;
        private MatchManager _match;

        public static CameraRig Instance { get; private set; }

        public void Setup(MatchManager match)
        {
            Instance = this;
            _match = match;

            var go = new GameObject("MainCamera");
            go.transform.SetParent(transform, false);
            go.tag = "MainCamera";
            Cam = go.AddComponent<Camera>();
            Cam.fieldOfView = 44f;
            Cam.nearClipPlane = 1f;
            Cam.farClipPlane = 220f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.016f, 0.02f, 0.035f);
            Cam.allowHDR = true;
            Cam.allowMSAA = true;
            go.AddComponent<AudioListener>();
        }

        public void SetTarget(Transform target) => Target = target;

        private void LateUpdate()
        {
            if (Cam == null) return;

            Vector3 focus;
            // В комнате ожидания камера должна ходить за игроком, как в раунде,
            // а не улетать в центр стола для собраний.
            bool followsPlayer = _match == null
                                 || _match.Phase == MatchPhase.Roaming
                                 || _match.Phase == MatchPhase.RoleReveal
                                 || _match.Phase == MatchPhase.Lobby;
            if (!followsPlayer && StationView.Instance != null)
            {
                focus = StationView.Instance.MeetingCenter;
                _targetHeight = 34f;
            }
            else if (Target != null)
            {
                focus = Target.position;
                float vision = _match != null && _match.Local != null ? _match.VisionRadiusFor(_match.Local) : 14f;
                _targetHeight = Mathf.Clamp(vision * 1.55f, 18f, 34f);
            }
            else return;

            _height = Mathf.Lerp(_height, _targetHeight, Time.deltaTime * 2.2f);
            float rad = _pitch * Mathf.Deg2Rad;
            var offset = new Vector3(0f, Mathf.Sin(rad), -Mathf.Cos(rad)) * _height;
            var desired = focus + offset;

            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _velocity, 0.14f);
            Cam.transform.localPosition = Vector3.zero;
            Cam.transform.rotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        public void Shake(float amount)
        {
            if (!GameSettings.Profile.ScreenShake) return;
            StartCoroutine(ShakeRoutine(amount));
        }

        private System.Collections.IEnumerator ShakeRoutine(float amount)
        {
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                float k = (1f - t / 0.35f) * amount;
                Cam.transform.localPosition = new Vector3(Random.Range(-k, k), Random.Range(-k, k), 0f);
                yield return null;
            }
            Cam.transform.localPosition = Vector3.zero;
        }
    }

    /// <summary>Hides everything the local player cannot see, and dims the screen edges.</summary>
    public class VisionController : MonoBehaviour
    {
        private MatchManager _match;
        private Image _vignette;
        private float _timer;

        public void Setup(MatchManager match, Transform uiRoot)
        {
            _match = match;

            var go = new GameObject("Vignette", typeof(RectTransform));
            go.transform.SetParent(uiRoot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-600f, -600f);
            rt.offsetMax = new Vector2(600f, 600f);

            _vignette = go.AddComponent<Image>();
            _vignette.sprite = Art.RadialGlow(256, 2.6f);
            _vignette.color = new Color(0f, 0f, 0f, 0f);
            _vignette.raycastTarget = false;
            _vignette.material = null;
            go.transform.SetAsFirstSibling();
        }

        private void LateUpdate()
        {
            if (_match == null) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.08f;

            var local = _match.Local;
            if (local == null) return;
            bool ghostView = local.IsGhost;

            foreach (var p in _match.Players)
            {
                var actor = p.View as Actor;
                if (actor == null) continue;
                if (p.Id == local.Id) { actor.SetVisible(true); continue; }

                bool visible;
                // правило «призраки видят призраков»: если оно выключено, мёртвый
                // видит только живых и бродит по станции в одиночестве
                if (ghostView && p.IsGhost) visible = _match.Settings == null || _match.Settings.GhostsSeeGhosts;
                else if (ghostView) visible = true;
                // фантом: живые не видят его вовсе, мёртвым он виден — как и всё
                // остальное, что живым знать не положено
                else if (p.IsPhantomHidden) visible = false;
                else if (p.InVent) visible = false;
                else if (p.IsGhost) visible = false;                     // living players never see ghosts
                else visible = _match.CanSeePlayer(local, p);

                actor.SetVisible(visible);
                if (actor.Visual != null) actor.Visual.SetTagVisible(visible && (ghostView || IsNear(local, p, 12f)));
            }

            foreach (var body in _match.Bodies)
            {
                if (body == null) continue;
                bool visible = ghostView || _match.CanSee(local, body.transform.position, body.Deck);
                body.SetVisible(visible);
            }

            if (_vignette != null)
            {
                float darkness = ghostView ? 0f : Mathf.Clamp01(1f - _match.VisionFactor) * 0.85f;
                if (_match.Phase != MatchPhase.Roaming) darkness = 0f;
                var c = _vignette.color;
                c.a = Mathf.Lerp(c.a, darkness, 0.25f);
                _vignette.color = c;
            }
        }

        private static bool IsNear(PlayerState a, PlayerState b, float range)
        {
            return (a.Position - b.Position).sqrMagnitude < range * range;
        }
    }

    /// <summary>Applies the Low/Medium/High/Ultra presets at runtime.</summary>
    public static class QualityManager
    {
        public static void Apply(QualityTier tier)
        {
            var asset = Resources.Load<UnityEngine.Rendering.RenderPipelineAsset>("Quality/URP_" + tier);
            if (asset != null)
            {
                QualitySettings.renderPipeline = asset;
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = asset;
            }

            switch (tier)
            {
                case QualityTier.Low:
                    QualitySettings.globalTextureMipmapLimit = 2;
                    QualitySettings.shadows = ShadowQuality.Disable;
                    QualitySettings.lodBias = 0.6f;
                    QualitySettings.particleRaycastBudget = 16;
                    QualitySettings.softParticles = false;
                    break;
                case QualityTier.Medium:
                    QualitySettings.globalTextureMipmapLimit = 1;
                    QualitySettings.shadows = ShadowQuality.HardOnly;
                    QualitySettings.lodBias = 0.9f;
                    QualitySettings.particleRaycastBudget = 32;
                    QualitySettings.softParticles = false;
                    break;
                case QualityTier.High:
                    QualitySettings.globalTextureMipmapLimit = 0;
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.lodBias = 1.2f;
                    QualitySettings.particleRaycastBudget = 64;
                    QualitySettings.softParticles = true;
                    break;
                default:
                    QualitySettings.globalTextureMipmapLimit = 0;
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.lodBias = 1.8f;
                    QualitySettings.particleRaycastBudget = 128;
                    QualitySettings.softParticles = true;
                    break;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = GameSettings.Profile.TargetFps;
        }

        /// <summary>Picks a sane starting tier from the device's reported specs.</summary>
        public static QualityTier Autodetect()
        {
            int mem = SystemInfo.systemMemorySize;
            int gpu = SystemInfo.graphicsMemorySize;
            if (Application.isMobilePlatform)
            {
                if (mem < 3000 || gpu < 1000) return QualityTier.Low;
                if (mem < 6000) return QualityTier.Medium;
                return QualityTier.High;
            }
            return QualityTier.High;
        }
    }
}
