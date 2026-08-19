// -----------------------------------------------------------------------------
//  NEBULA NINE - runtime performance guards for mobile.
//
//  Two cheap but high impact systems:
//    * LightCuller  - the station has ~60 point lights; only the handful near the
//                     camera stay enabled, which keeps URP's per-object light
//                     evaluation and the additional-light loop tiny.
//    * PerformanceTuner - watches the frame time and steps the quality tier down
//                     (once) if the device cannot hold the target frame rate.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;
using Nebula.Map;

namespace Nebula.Gameplay
{
    public class LightCuller : MonoBehaviour
    {
        public float Radius = 34f;
        public int MaxActive = 14;

        private readonly List<Light> _lights = new List<Light>();
        private Transform _focus;
        private float _timer;

        public void Collect(Transform root, Transform focus)
        {
            _focus = focus;
            _lights.Clear();
            root.GetComponentsInChildren(true, _lights);
            for (int i = _lights.Count - 1; i >= 0; i--)
                if (_lights[i] == null || _lights[i].type == LightType.Directional) _lights.RemoveAt(i);
        }

        public void SetFocus(Transform focus) => _focus = focus;

        private void LateUpdate()
        {
            if (_lights.Count == 0) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.25f;

            Vector3 centre;
            if (_focus != null) centre = _focus.position;
            else if (CameraRig.Instance != null && CameraRig.Instance.Cam != null)
                centre = CameraRig.Instance.Cam.transform.position;
            else return;

            float r2 = Radius * Radius;
            int active = 0;
            for (int i = 0; i < _lights.Count; i++)
            {
                var l = _lights[i];
                if (l == null) continue;
                bool near = (l.transform.position - centre).sqrMagnitude < r2 && active < MaxActive;
                if (near) active++;
                if (l.enabled != near) l.enabled = near;
            }
        }
    }

    public class PerformanceTuner : MonoBehaviour
    {
        private float _accumulated;
        private int _frames;
        private int _downgrades;
        private float _grace = 6f;

        private void Update()
        {
            if (_grace > 0f) { _grace -= Time.unscaledDeltaTime; return; }
            if (_downgrades >= 2) { enabled = false; return; }

            _accumulated += Time.unscaledDeltaTime;
            _frames++;
            if (_accumulated < 4f) return;

            float fps = _frames / _accumulated;
            _accumulated = 0f;
            _frames = 0;

            float target = Mathf.Max(24f, GameSettings.Profile.TargetFps * 0.72f);
            if (fps >= target) return;

            var tier = GameSettings.Profile.Quality;
            if (tier == QualityTier.Low) { enabled = false; return; }

            GameSettings.Profile.Quality = (QualityTier)((int)tier - 1);
            GameSettings.Save();
            QualityManager.Apply(GameSettings.Profile.Quality);
            _downgrades++;
            _grace = 8f;
            GameEvents.RaiseAnnounce("Качество снижено до " + GameSettings.Profile.Quality + " для стабильного FPS", 3.5f);
        }
    }
}
