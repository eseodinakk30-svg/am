// -----------------------------------------------------------------------------
//  NEBULA NINE - the view/controller pair for one participant.
//  Owns the visual rig and the motor, keeps PlayerState's spatial fields fresh
//  and exposes the physical verbs the gameplay systems call (kill, die, vent...).
// -----------------------------------------------------------------------------

using UnityEngine;
using Nebula.Core;
using Nebula.Fx;
using Nebula.Map;

namespace Nebula.Characters
{
    public class Actor : MonoBehaviour
    {
        public PlayerState State;
        public CharacterMotor Motor;
        public CharacterVisual Visual;

        private AnimState _activity = AnimState.Idle;
        private float _activityHold;
        private AudioSource _audio;
        private float _roomCheckTimer;

        public bool InVent => State != null && State.InVent;

        public static Actor Spawn(PlayerState state, Transform parent, Vector3 position)
        {
            var go = new GameObject("Actor_" + state.Id + "_" + state.Label);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.layer = 9;

            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;

            var actor = go.AddComponent<Actor>();
            actor.State = state;
            actor.Motor = go.AddComponent<CharacterMotor>();
            actor.Motor.BaseSpeed = GameSettings.Match.MoveSpeed;

            var visualGo = new GameObject("Visual");
            visualGo.transform.SetParent(go.transform, false);
            actor.Visual = visualGo.AddComponent<CharacterVisual>();
            actor.Visual.Build(state.ColorIndex, state.HatIndex, state.OutfitIndex,
                state.AccessoryIndex, state.TrailIndex, state.Label);

            actor._audio = go.AddComponent<AudioSource>();
            actor._audio.spatialBlend = 1f;
            actor._audio.minDistance = 3f;
            actor._audio.maxDistance = 24f;
            actor._audio.rolloffMode = AudioRolloffMode.Linear;
            actor._audio.playOnAwake = false;

            state.View = actor;
            state.Position = position;
            return actor;
        }

        private void Update()
        {
            if (State == null) return;

            State.Position = transform.position;
            State.Velocity = Motor != null ? Motor.Velocity : Vector3.zero;

            _roomCheckTimer -= Time.deltaTime;
            if (_roomCheckTimer <= 0f)
            {
                _roomCheckTimer = 0.12f;
                var grid = StationGrid.Instance;
                if (grid != null)
                {
                    State.Deck = StationLayout.DeckOfWorld(transform.position);
                    int room = grid.RoomAtWorld(transform.position);
                    if (room >= 0) State.RoomId = room;
                }
            }

            if (Motor != null)
            {
                Visual.SetSpeed(Motor.Speed01);
                Visual.SetFacing(Motor.Facing);
                if (Motor.MeasuredSpeed > 0.2f) State.LastMovedTime = Time.time;
            }

            if (_activityHold > 0f)
            {
                _activityHold -= Time.deltaTime;
                if (_activityHold <= 0f) _activity = AnimState.Idle;
            }

            UpdateAnimState();

            if (Visual.ConsumeFootstep() && _audio != null)
                Audio.SoundBank.PlayAt(_audio, Audio.Sfx.Footstep, 0.35f);
        }

        private void UpdateAnimState()
        {
            if (State.IsGhost)
            {
                Visual.SetState(AnimState.Ghost);
                return;
            }
            if (State.InVent)
            {
                Visual.SetState(AnimState.Vent);
                return;
            }
            if (Visual.State == AnimState.Kill || Visual.State == AnimState.Die) return;

            if (_activity != AnimState.Idle)
            {
                Visual.SetState(_activity);
                return;
            }

            bool moving = Motor != null && Motor.MeasuredSpeed > 0.35f;
            Visual.SetState(moving ? AnimState.Walk : AnimState.Idle);
        }

        // ------------------------------------------------------------------ verbs
        public void SetActivity(AnimState activity, float hold = 0.4f)
        {
            _activity = activity;
            _activityHold = hold;
        }

        public void ClearActivity()
        {
            _activity = AnimState.Idle;
            _activityHold = 0f;
        }

        public void PlayKillAnimation()
        {
            Visual.SetState(AnimState.Kill);
            if (_audio != null) Audio.SoundBank.PlayAt(_audio, Audio.Sfx.Kill);
        }

        public void PlayDeath()
        {
            Visual.SetState(AnimState.Die);
        }

        public void BecomeGhost()
        {
            Visual.SetGhost(true);
            if (Motor != null)
            {
                Motor.SetGhost(true);
                Motor.SpeedMultiplier = 1.35f;
            }
        }

        public void EnterVent(VentPoint vent)
        {
            if (State == null || vent == null) return;
            State.InVent = true;
            State.VentId = vent.Def.Id;
            vent.PlayOpen();
            Motor.Teleport(vent.transform.position);
            Motor.Frozen = true;
            Visual.SetState(AnimState.Vent);
            if (_audio != null) Audio.SoundBank.PlayAt(_audio, Audio.Sfx.Vent);
            GameEvents.RaiseVentEntered(State, vent.Def.Id);
        }

        public void MoveThroughVent(VentPoint vent)
        {
            if (State == null || vent == null) return;
            State.VentId = vent.Def.Id;
            Motor.Teleport(vent.transform.position);
            State.Position = vent.transform.position;
        }

        public void ExitVent(VentPoint vent)
        {
            if (State == null) return;
            int id = State.VentId;
            State.InVent = false;
            State.VentId = -1;
            if (vent != null)
            {
                vent.PlayOpen();
                Motor.Teleport(vent.transform.position);
            }
            Motor.Frozen = false;
            Visual.SetState(AnimState.Idle);
            Visual.Root.localScale = Vector3.one;
            if (_audio != null) Audio.SoundBank.PlayAt(_audio, Audio.Sfx.Vent);
            GameEvents.RaiseVentExited(State, id);
        }

        public void PlaySound(Audio.Sfx sfx, float volume = 1f)
        {
            if (_audio != null) Audio.SoundBank.PlayAt(_audio, sfx, volume);
        }

        public void SetVisible(bool visible)
        {
            if (Visual != null) Visual.SetVisible(visible);
        }
    }

    /// <summary>The corpse left behind by a kill - reportable until a meeting happens.</summary>
    public class DeadBody : MonoBehaviour
    {
        public PlayerState Victim;
        public int RoomId = -1;
        public DeckId Deck;
        public float SpawnTime;

        public static DeadBody Spawn(PlayerState victim, Transform parent)
        {
            var go = new GameObject("Body_" + victim.Label);
            go.transform.SetParent(parent, false);
            go.transform.position = victim.BodyPosition;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            var body = go.AddComponent<DeadBody>();
            body.Victim = victim;
            body.RoomId = victim.BodyRoomId;
            body.Deck = victim.BodyDeck;
            body.SpawnTime = Time.time;

            var suit = ColorBank.Get(victim.ColorIndex);
            var shade = ColorBank.Shade(victim.ColorIndex, 0.5f);

            var torso = MakePiece(go.transform, Art.Capsule, Art.Lit(suit, 0.05f, 0.3f),
                new Vector3(0f, 0.42f, 0f), new Vector3(1.05f, 0.5f, 1.05f), new Vector3(0f, 0f, 84f));
            MakePiece(go.transform, Art.Sphere, Art.Lit(new Color(0.45f, 0.62f, 0.72f), 0.1f, 0.85f),
                new Vector3(0.34f, 0.34f, 0.3f), new Vector3(0.6f, 0.38f, 0.26f), Vector3.zero);
            MakePiece(go.transform, Art.Cube, Art.Lit(shade, 0.05f, 0.3f),
                new Vector3(-0.45f, 0.22f, 0.18f), new Vector3(0.3f, 0.34f, 0.36f), new Vector3(0f, 0f, 30f));
            // the stylised "bone" marker so a body reads instantly from above
            MakePiece(go.transform, Art.Cylinder, Art.Lit(new Color(0.92f, 0.9f, 0.84f), 0f, 0.4f),
                new Vector3(0.1f, 0.75f, -0.2f), new Vector3(0.12f, 0.34f, 0.12f), new Vector3(18f, 0f, 12f));

            var pool = new GameObject("Pool");
            pool.transform.SetParent(go.transform, false);
            pool.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pool.transform.localScale = Vector3.one * 2.1f;
            var pmf = pool.AddComponent<MeshFilter>();
            pmf.sharedMesh = Art.Quad;
            var pmr = pool.AddComponent<MeshRenderer>();
            pmr.sharedMaterial = Art.Transparent(new Color(0.35f, 0.03f, 0.06f, 0.75f));
            pmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (torso != null) { }
            return body;
        }

        private static Transform MakePiece(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var go = new GameObject("Piece");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(euler);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        public void SetVisible(bool visible)
        {
            var rs = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++) rs[i].enabled = visible;
        }
    }
}
