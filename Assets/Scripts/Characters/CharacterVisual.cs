// -----------------------------------------------------------------------------
//  NEBULA NINE - procedural crew member.
//
//  The character is assembled from primitives (capsule body, visor, boots,
//  backpack, cosmetics) and animated entirely in code: no rigs, no clips, no
//  imported assets - which keeps the silhouette crisp, the draw calls low and the
//  animation fully data driven (speed, task work, kill lunge, death, vent dive,
//  ghost float, meeting idle, win/lose poses).
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Core;
using Nebula.Fx;

namespace Nebula.Characters
{
    public enum AnimState
    {
        Idle = 0,
        Walk = 1,
        Work = 2,       // performing a task
        Repair = 3,
        Kill = 4,
        Die = 5,
        Vent = 6,
        Meeting = 7,
        Ejected = 8,
        Win = 9,
        Lose = 10,
        Ghost = 11,
        Use = 12,
    }

    public class CharacterVisual : MonoBehaviour
    {
        public Transform Body;
        public Transform Visor;
        public Transform Backpack;
        public Transform LegL, LegR;
        public Transform ArmL, ArmR;
        public Transform Hat;
        public Transform Accessory;
        public Transform Root;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private Renderer _bodyRenderer, _visorRenderer, _legLRenderer, _legRRenderer, _packRenderer;
        private Canvas _tagCanvas;
        private Text _tagText;
        private Image _tagBadge;
        private ParticleSystem _trail;
        private MaterialPropertyBlock _mpb;

        private AnimState _state = AnimState.Idle;
        private float _phase;
        private float _speed01;
        private float _stateTime;
        private float _facing = 1f;
        private Color _suit = Color.white;
        private bool _ghostMode;
        private bool _visible = true;
        private float _footstepTimer;

        public AnimState State => _state;
        public bool GhostMode => _ghostMode;

        // ------------------------------------------------------------------ build
        public void Build(int colorIndex, int hatIndex, int outfitIndex, int accessoryIndex, int trailIndex, string label)
        {
            _mpb = new MaterialPropertyBlock();
            _suit = ColorBank.Get(colorIndex);
            var shade = ColorBank.Shade(colorIndex, 0.55f);
            var outfitColor = OutfitColor(outfitIndex, _suit);

            Root = new GameObject("Rig").transform;
            Root.SetParent(transform, false);

            Body = MakePart("Body", Root, Art.Capsule, Art.Lit(_suit, 0.05f, 0.35f),
                new Vector3(0f, 0.86f, 0f), new Vector3(1.02f, 0.62f, 1.02f));
            _bodyRenderer = Body.GetComponent<Renderer>();

            // torso overlay = outfit
            var torso = MakePart("Torso", Body, Art.Capsule, Art.Lit(outfitColor, 0.05f, 0.4f),
                new Vector3(0f, -0.24f, 0f), new Vector3(1.015f, 0.52f, 1.015f));
            torso.localScale = new Vector3(1.02f, 0.5f, 1.02f);

            Visor = MakePart("Visor", Body, Art.Sphere, Art.Lit(new Color(0.62f, 0.86f, 0.98f), 0.1f, 0.92f, 0.55f),
                new Vector3(0f, 0.34f, 0.44f), new Vector3(0.72f, 0.44f, 0.30f));
            _visorRenderer = Visor.GetComponent<Renderer>();

            Backpack = MakePart("Backpack", Body, Art.Cube, Art.Lit(shade, 0.1f, 0.3f),
                new Vector3(0f, -0.05f, -0.56f), new Vector3(0.62f, 0.9f, 0.42f));
            _packRenderer = Backpack.GetComponent<Renderer>();

            LegL = MakePart("LegL", Root, Art.Cube, Art.Lit(shade, 0.05f, 0.3f),
                new Vector3(-0.26f, 0.20f, 0f), new Vector3(0.32f, 0.40f, 0.42f));
            LegR = MakePart("LegR", Root, Art.Cube, Art.Lit(shade, 0.05f, 0.3f),
                new Vector3(0.26f, 0.20f, 0f), new Vector3(0.32f, 0.40f, 0.42f));
            _legLRenderer = LegL.GetComponent<Renderer>();
            _legRRenderer = LegR.GetComponent<Renderer>();

            ArmL = MakePart("ArmL", Body, Art.Capsule, Art.Lit(_suit, 0.05f, 0.35f),
                new Vector3(-0.56f, -0.05f, 0.05f), new Vector3(0.30f, 0.30f, 0.30f));
            ArmR = MakePart("ArmR", Body, Art.Capsule, Art.Lit(_suit, 0.05f, 0.35f),
                new Vector3(0.56f, -0.05f, 0.05f), new Vector3(0.30f, 0.30f, 0.30f));

            BuildHat(hatIndex);
            BuildAccessory(accessoryIndex, shade);
            BuildTrail(trailIndex);
            BuildNameTag(label);

            CollectRenderers();
        }

        private static Color OutfitColor(int outfitIndex, Color suit)
        {
            switch (outfitIndex % 8)
            {
                case 1: return Color.Lerp(suit, new Color(0.85f, 0.62f, 0.18f), 0.55f);
                case 2: return Color.Lerp(suit, Color.white, 0.72f);
                case 3: return Color.Lerp(suit, new Color(0.72f, 0.84f, 0.95f), 0.6f);
                case 4: return Color.Lerp(suit, new Color(0.24f, 0.26f, 0.32f), 0.6f);
                case 5: return Color.Lerp(suit, new Color(0.45f, 0.32f, 0.20f), 0.55f);
                case 6: return Color.Lerp(suit, new Color(0.18f, 0.22f, 0.42f), 0.6f);
                case 7: return Color.Lerp(suit, new Color(0.35f, 0.40f, 0.28f), 0.6f);
                default: return Color.Lerp(suit, Color.black, 0.18f);
            }
        }

        private Transform MakePart(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }

        private void BuildHat(int hatIndex)
        {
            if (hatIndex % CosmeticBank.Hats.Length == 0) return;
            Hat = new GameObject("Hat").transform;
            Hat.SetParent(Body, false);
            Hat.localPosition = new Vector3(0f, 0.72f, 0f);

            switch (hatIndex % CosmeticBank.Hats.Length)
            {
                case 1: // survey cap
                    MakePart("Crown", Hat, Art.Cylinder, Art.Lit(new Color(0.85f, 0.28f, 0.30f), 0f, 0.3f), new Vector3(0, 0.06f, 0), new Vector3(0.78f, 0.16f, 0.78f));
                    MakePart("Brim", Hat, Art.Cube, Art.Lit(new Color(0.65f, 0.2f, 0.22f), 0f, 0.3f), new Vector3(0, 0.02f, 0.36f), new Vector3(0.72f, 0.06f, 0.36f));
                    break;
                case 2: // welding visor
                    MakePart("Shield", Hat, Art.Cube, Art.Lit(new Color(0.18f, 0.20f, 0.24f), 0.6f, 0.5f), new Vector3(0, 0.05f, 0.12f), new Vector3(0.88f, 0.3f, 0.7f));
                    break;
                case 3: // beacon lamp
                    MakePart("Base", Hat, Art.Cylinder, Art.Lit(new Color(0.3f, 0.32f, 0.36f), 0.5f, 0.5f), new Vector3(0, 0.05f, 0), new Vector3(0.4f, 0.1f, 0.4f));
                    MakePart("Bulb", Hat, Art.Sphere, Art.Lit(new Color(1f, 0.85f, 0.4f), 0f, 0.9f, 3f), new Vector3(0, 0.22f, 0), Vector3.one * 0.34f);
                    break;
                case 4: // comms headset
                    MakePart("Band", Hat, Art.Cube, Art.Lit(new Color(0.2f, 0.22f, 0.26f), 0.3f, 0.4f), new Vector3(0, 0.02f, 0), new Vector3(0.9f, 0.1f, 0.14f));
                    MakePart("Mic", Hat, Art.Cube, Art.Lit(new Color(0.2f, 0.22f, 0.26f), 0.3f, 0.4f), new Vector3(0.3f, -0.18f, 0.28f), new Vector3(0.09f, 0.09f, 0.42f));
                    break;
                case 5: // bio dome
                    MakePart("Dome", Hat, Art.Sphere, Art.Transparent(new Color(0.6f, 0.9f, 1f, 0.28f)), new Vector3(0, -0.12f, 0.05f), Vector3.one * 1.16f);
                    break;
                case 6: // antenna fin
                    MakePart("Fin", Hat, Art.Cube, Art.Lit(new Color(0.9f, 0.5f, 0.15f), 0.2f, 0.5f), new Vector3(0, 0.22f, 0), new Vector3(0.08f, 0.5f, 0.42f));
                    break;
                case 7: // command beret
                    MakePart("Beret", Hat, Art.Cylinder, Art.Lit(new Color(0.15f, 0.2f, 0.45f), 0f, 0.35f), new Vector3(0.08f, 0.04f, 0), new Vector3(0.86f, 0.1f, 0.86f));
                    break;
                case 8: // coolant halo
                    MakePart("Halo", Hat, Art.Cylinder, Art.Lit(new Color(0.4f, 0.95f, 1f), 0f, 0.9f, 2.4f), new Vector3(0, 0.3f, 0), new Vector3(0.95f, 0.03f, 0.95f));
                    break;
                default: // paper crown
                    MakePart("Crown", Hat, Art.Cylinder, Art.Lit(new Color(0.95f, 0.85f, 0.4f), 0.6f, 0.7f), new Vector3(0, 0.1f, 0), new Vector3(0.8f, 0.2f, 0.8f));
                    break;
            }
        }

        private void BuildAccessory(int index, Color shade)
        {
            if (index % CosmeticBank.Accessories.Length == 0) return;
            Accessory = new GameObject("Accessory").transform;
            Accessory.SetParent(Body, false);

            switch (index % CosmeticBank.Accessories.Length)
            {
                case 1:
                    MakePart("Belt", Accessory, Art.Cube, Art.Lit(new Color(0.35f, 0.28f, 0.16f), 0.2f, 0.3f), new Vector3(0, -0.34f, 0), new Vector3(1.06f, 0.16f, 1.06f));
                    break;
                case 2:
                    MakePart("Case", Accessory, Art.Cube, Art.Lit(new Color(0.75f, 0.78f, 0.82f), 0.5f, 0.5f), new Vector3(0.62f, -0.3f, 0.1f), new Vector3(0.3f, 0.34f, 0.22f));
                    break;
                case 3:
                    MakePart("Drone", Accessory, Art.Sphere, Art.Lit(new Color(0.5f, 0.85f, 1f), 0.2f, 0.85f, 1.6f), new Vector3(0.72f, 0.5f, -0.2f), Vector3.one * 0.3f);
                    break;
                case 4:
                    MakePart("Slate", Accessory, Art.Cube, Art.Lit(new Color(0.25f, 0.85f, 0.7f), 0f, 0.8f, 1.4f), new Vector3(-0.6f, -0.16f, 0.28f), new Vector3(0.06f, 0.34f, 0.26f));
                    break;
                case 5:
                    MakePart("Tank", Accessory, Art.Cylinder, Art.Lit(new Color(0.85f, 0.75f, 0.25f), 0.4f, 0.5f), new Vector3(0.2f, -0.05f, -0.68f), new Vector3(0.3f, 0.42f, 0.3f));
                    break;
                case 6:
                    MakePart("Cable", Accessory, Art.Cylinder, Art.Lit(shade, 0.1f, 0.3f), new Vector3(-0.5f, -0.4f, -0.4f), new Vector3(0.12f, 0.3f, 0.12f));
                    break;
                default:
                    MakePart("BootL", Accessory, Art.Cube, Art.Lit(new Color(0.75f, 0.55f, 0.15f), 0.4f, 0.5f), new Vector3(-0.26f, -0.86f, 0.05f), new Vector3(0.36f, 0.12f, 0.5f));
                    MakePart("BootR", Accessory, Art.Cube, Art.Lit(new Color(0.75f, 0.55f, 0.15f), 0.4f, 0.5f), new Vector3(0.26f, -0.86f, 0.05f), new Vector3(0.36f, 0.12f, 0.5f));
                    break;
            }
        }

        private void BuildTrail(int index)
        {
            if (index % CosmeticBank.Trails.Length == 0) return;
            var go = new GameObject("Trail");
            go.transform.SetParent(Root, false);
            go.transform.localPosition = new Vector3(0f, 0.25f, -0.35f);

            _trail = go.AddComponent<ParticleSystem>();
            var main = _trail.main;
            main.startLifetime = 0.85f;
            main.startSpeed = 0.4f;
            main.startSize = 0.22f;
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = TrailColor(index);
            var emission = _trail.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 3.5f;
            var shape = _trail.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.22f;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Art.Transparent(TrailColor(index));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private static Color TrailColor(int index)
        {
            switch (index % CosmeticBank.Trails.Length)
            {
                case 1: return new Color(1f, 0.75f, 0.3f, 0.75f);
                case 2: return new Color(0.7f, 0.92f, 1f, 0.7f);
                case 3: return new Color(1f, 0.45f, 0.2f, 0.75f);
                case 4: return new Color(0.55f, 0.85f, 1f, 0.7f);
                default: return new Color(0.75f, 0.55f, 1f, 0.7f);
            }
        }

        private void BuildNameTag(string label)
        {
            var go = new GameObject("NameTag");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.55f, 0f);

            _tagCanvas = go.AddComponent<Canvas>();
            _tagCanvas.renderMode = RenderMode.WorldSpace;
            var rt = _tagCanvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(320f, 60f);
            rt.localScale = Vector3.one * 0.012f;

            var badgeGo = new GameObject("Badge");
            badgeGo.transform.SetParent(go.transform, false);
            _tagBadge = badgeGo.AddComponent<Image>();
            _tagBadge.sprite = Art.RoundedRect(14, 48);
            _tagBadge.type = Image.Type.Sliced;
            _tagBadge.color = new Color(0f, 0f, 0f, 0.45f);
            var brt = _tagBadge.rectTransform;
            brt.sizeDelta = new Vector2(320f, 52f);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            _tagText = textGo.AddComponent<Text>();
            _tagText.font = Art.UiFont;
            _tagText.fontSize = 34;
            _tagText.alignment = TextAnchor.MiddleCenter;
            _tagText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _tagText.verticalOverflow = VerticalWrapMode.Overflow;
            _tagText.color = Color.white;
            _tagText.text = label;
            var trt = _tagText.rectTransform;
            trt.sizeDelta = new Vector2(320f, 52f);
        }

        private void CollectRenderers()
        {
            _renderers.Clear();
            GetComponentsInChildren(true, _renderers);
        }

        // ------------------------------------------------------------------ api
        public void SetLabel(string text, Color color)
        {
            if (_tagText == null) return;
            _tagText.text = text;
            _tagText.color = color;
        }

        public void SetTagVisible(bool on)
        {
            if (_tagCanvas != null) _tagCanvas.enabled = on;
        }

        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            for (int i = 0; i < _renderers.Count; i++)
                if (_renderers[i] != null) _renderers[i].enabled = visible;
            if (_tagCanvas != null) _tagCanvas.enabled = visible;
        }

        public bool IsVisible => _visible;

        public void SetGhost(bool ghost)
        {
            if (_ghostMode == ghost) return;
            _ghostMode = ghost;
            var col = ghost ? new Color(_suit.r, _suit.g, _suit.b, 0.42f) : _suit;
            if (ghost)
            {
                foreach (var r in _renderers)
                {
                    if (r == null || r is ParticleSystemRenderer) continue;
                    r.sharedMaterial = Art.Transparent(new Color(col.r, col.g, col.b, 0.4f));
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                SetState(AnimState.Ghost);
            }
        }

        public void SetSpeed(float speed01) => _speed01 = Mathf.Clamp01(speed01);

        public void SetFacing(float dirX)
        {
            if (Mathf.Abs(dirX) > 0.06f) _facing = Mathf.Sign(dirX);
        }

        public void SetState(AnimState state)
        {
            if (_state == state) return;
            _state = state;
            _stateTime = 0f;
        }

        public void FlashVisor(Color c, float seconds = 0.4f)
        {
            if (_visorRenderer == null) return;
            Art.Tint(_visorRenderer, c, _mpb);
            CancelInvoke(nameof(ResetVisor));
            Invoke(nameof(ResetVisor), seconds);
        }

        private void ResetVisor()
        {
            if (_visorRenderer != null) Art.Tint(_visorRenderer, new Color(0.62f, 0.86f, 0.98f), _mpb);
        }

        /// <summary>True on the frame a footstep should be played.</summary>
        public bool ConsumeFootstep()
        {
            if (_state != AnimState.Walk || _speed01 < 0.15f) return false;
            _footstepTimer -= Time.deltaTime * (0.6f + _speed01);
            if (_footstepTimer <= 0f)
            {
                _footstepTimer = 0.42f;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ animation
        private void LateUpdate()
        {
            if (!_visible || Root == null) return;
            float dt = Time.deltaTime;
            _stateTime += dt;

            // face left/right without spinning the whole transform (top-down readability)
            float targetYaw = _facing >= 0f ? 22f : -22f;
            Root.localRotation = Quaternion.Slerp(Root.localRotation, Quaternion.Euler(0f, targetYaw, 0f), dt * 10f);

            switch (_state)
            {
                case AnimState.Walk: AnimWalk(dt); break;
                case AnimState.Work: AnimWork(dt); break;
                case AnimState.Repair: AnimRepair(dt); break;
                case AnimState.Kill: AnimKill(dt); break;
                case AnimState.Die: AnimDie(dt); break;
                case AnimState.Vent: AnimVent(dt); break;
                case AnimState.Meeting: AnimMeeting(dt); break;
                case AnimState.Ejected: AnimEjected(dt); break;
                case AnimState.Win: AnimWin(dt); break;
                case AnimState.Lose: AnimLose(dt); break;
                case AnimState.Ghost: AnimGhost(dt); break;
                case AnimState.Use: AnimWork(dt); break;
                default: AnimIdle(dt); break;
            }
        }

        private void AnimIdle(float dt)
        {
            _phase += dt * 1.6f;
            float bob = Mathf.Sin(_phase) * 0.022f;
            Root.localPosition = new Vector3(0f, bob, 0f);
            Root.localScale = Vector3.one;
            if (LegL != null) LegL.localPosition = new Vector3(-0.26f, 0.20f, 0f);
            if (LegR != null) LegR.localPosition = new Vector3(0.26f, 0.20f, 0f);
            if (ArmL != null) ArmL.localRotation = Quaternion.identity;
            if (ArmR != null) ArmR.localRotation = Quaternion.identity;
            if (Body != null) Body.localRotation = Quaternion.identity;
        }

        private void AnimWalk(float dt)
        {
            _phase += dt * (7.5f + _speed01 * 5.5f);
            float swing = Mathf.Sin(_phase);
            float bob = Mathf.Abs(Mathf.Cos(_phase)) * 0.055f * _speed01;

            Root.localPosition = new Vector3(0f, bob, 0f);
            Root.localScale = new Vector3(1f, 1f - bob * 0.35f, 1f);
            if (LegL != null) LegL.localPosition = new Vector3(-0.26f, 0.20f, swing * 0.28f * _speed01);
            if (LegR != null) LegR.localPosition = new Vector3(0.26f, 0.20f, -swing * 0.28f * _speed01);
            if (ArmL != null) ArmL.localRotation = Quaternion.Euler(-swing * 26f * _speed01, 0f, 0f);
            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(swing * 26f * _speed01, 0f, 0f);
            if (Body != null) Body.localRotation = Quaternion.Euler(4f * _speed01, 0f, swing * 3f * _speed01);
        }

        private void AnimWork(float dt)
        {
            _phase += dt * 6.5f;
            float s = Mathf.Sin(_phase);
            Root.localPosition = new Vector3(0f, Mathf.Abs(s) * 0.02f, 0f);
            if (ArmL != null) ArmL.localRotation = Quaternion.Euler(-55f + s * 16f, 0f, 0f);
            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(-55f - s * 16f, 0f, 0f);
            if (Body != null) Body.localRotation = Quaternion.Euler(9f, 0f, s * 2.5f);
        }

        private void AnimRepair(float dt)
        {
            _phase += dt * 11f;
            float s = Mathf.Sin(_phase);
            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(-70f + s * 34f, 0f, 0f);
            if (ArmL != null) ArmL.localRotation = Quaternion.Euler(-38f, 0f, 0f);
            if (Body != null) Body.localRotation = Quaternion.Euler(13f, s * 5f, 0f);
        }

        private void AnimKill(float dt)
        {
            float t = Mathf.Clamp01(_stateTime / 0.55f);
            float lunge = Mathf.Sin(t * Mathf.PI);
            Root.localPosition = new Vector3(0f, lunge * 0.12f, lunge * 0.55f);
            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(-120f * lunge, 0f, 0f);
            if (Body != null) Body.localRotation = Quaternion.Euler(18f * lunge, 0f, 0f);
            if (t >= 1f) SetState(AnimState.Idle);
        }

        private void AnimDie(float dt)
        {
            float t = Mathf.Clamp01(_stateTime / 0.7f);
            float e = 1f - (1f - t) * (1f - t);
            Root.localRotation = Quaternion.Euler(0f, _facing >= 0f ? 22f : -22f, e * 82f);
            Root.localPosition = new Vector3(0f, -e * 0.42f, 0f);
            Root.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.72f, e), 1f);
        }

        private void AnimVent(float dt)
        {
            float t = Mathf.Clamp01(_stateTime / 0.45f);
            Root.localScale = new Vector3(1f - t * 0.6f, Mathf.Max(0.02f, 1f - t), 1f - t * 0.6f);
            Root.localPosition = new Vector3(0f, -t * 0.55f, 0f);
            Root.localRotation = Quaternion.Euler(0f, t * 540f, 0f);
        }

        private void AnimMeeting(float dt)
        {
            _phase += dt * 2.4f;
            Root.localPosition = new Vector3(0f, Mathf.Sin(_phase) * 0.035f, 0f);
            if (ArmL != null) ArmL.localRotation = Quaternion.Euler(Mathf.Sin(_phase * 1.7f) * 12f, 0f, 0f);
            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(Mathf.Sin(_phase * 1.7f + 1.1f) * 12f, 0f, 0f);
        }

        private void AnimEjected(float dt)
        {
            _phase += dt * 2.2f;
            Root.localRotation = Quaternion.Euler(_phase * 40f, _phase * 62f, _phase * 25f);
        }

        private void AnimWin(float dt)
        {
            _phase += dt * 6f;
            float jump = Mathf.Abs(Mathf.Sin(_phase)) * 0.42f;
            Root.localPosition = new Vector3(0f, jump, 0f);
            if (ArmL != null) ArmL.localRotation = Quaternion.Euler(-150f, 0f, 0f);
            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(-150f, 0f, 0f);
        }

        private void AnimLose(float dt)
        {
            float t = Mathf.Clamp01(_stateTime / 1.2f);
            Root.localPosition = new Vector3(0f, -t * 0.14f, 0f);
            if (Body != null) Body.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 26f, t), 0f, 0f);
            if (ArmL != null) ArmL.localRotation = Quaternion.Euler(18f, 0f, 0f);
            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(18f, 0f, 0f);
        }

        private void AnimGhost(float dt)
        {
            _phase += dt * 1.9f;
            Root.localPosition = new Vector3(Mathf.Sin(_phase * 0.6f) * 0.06f, 0.42f + Mathf.Sin(_phase) * 0.12f, 0f);
            Root.localRotation = Quaternion.Euler(0f, (_facing >= 0f ? 22f : -22f) + Mathf.Sin(_phase * 0.8f) * 7f, Mathf.Sin(_phase * 0.5f) * 5f);
            if (LegL != null) LegL.localPosition = new Vector3(-0.26f, 0.16f + Mathf.Sin(_phase * 1.3f) * 0.05f, 0f);
            if (LegR != null) LegR.localPosition = new Vector3(0.26f, 0.16f + Mathf.Sin(_phase * 1.3f + 1f) * 0.05f, 0f);
        }
    }
}
