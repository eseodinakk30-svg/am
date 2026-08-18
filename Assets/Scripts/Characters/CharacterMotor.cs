// -----------------------------------------------------------------------------
//  NEBULA NINE - shared locomotion for humans and NPCs.
//  Kinematic-feeling rigidbody movement on the XZ plane with wall sliding,
//  a speed modifier stack and a ghost mode that ignores geometry.
// -----------------------------------------------------------------------------

using UnityEngine;
using Nebula.Core;
using Nebula.Map;

namespace Nebula.Characters
{
    [RequireComponent(typeof(Rigidbody))]
    public class CharacterMotor : MonoBehaviour
    {
        public float BaseSpeed = 6.2f;
        public float SpeedMultiplier = 1f;
        public bool Frozen;
        public bool GhostMode;

        private Rigidbody _rb;
        private CapsuleCollider _capsule;
        private Vector2 _input;
        private Vector3 _lastPosition;
        private float _measuredSpeed;

        public Vector2 Input => _input;
        public float MeasuredSpeed => _measuredSpeed;
        public float Speed01 => BaseSpeed <= 0.01f ? 0f : Mathf.Clamp01(_measuredSpeed / BaseSpeed);
        public Vector3 Velocity => _rb != null ? _rb.velocity : Vector3.zero;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.isKinematic = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            _rb.drag = 0f;
            _rb.mass = 1f;

            _capsule = GetComponent<CapsuleCollider>();
            if (_capsule == null) _capsule = gameObject.AddComponent<CapsuleCollider>();
            _capsule.radius = 0.52f;
            _capsule.height = 1.9f;
            _capsule.center = new Vector3(0f, 0.95f, 0f);

            _lastPosition = transform.position;
        }

        public void SetInput(Vector2 input)
        {
            _input = Vector2.ClampMagnitude(input, 1f);
        }

        public void SetGhost(bool ghost)
        {
            GhostMode = ghost;
            if (_capsule != null) _capsule.enabled = !ghost;
            if (_rb != null) _rb.detectCollisions = !ghost;
        }

        public void Teleport(Vector3 position)
        {
            if (_rb != null)
            {
                _rb.velocity = Vector3.zero;
                _rb.position = position;
            }
            transform.position = position;
            _lastPosition = position;
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;

            if (Frozen)
            {
                _rb.velocity = Vector3.zero;
                return;
            }

            float speed = BaseSpeed * SpeedMultiplier;
            var desired = new Vector3(_input.x, 0f, _input.y) * speed;
            _rb.velocity = desired;

            if (GhostMode)
            {
                // ghosts float freely but stay inside the station footprint
                var p = _rb.position;
                float halfW = StationLayout.GridW * StationLayout.CellSize * 0.5f + 6f;
                float halfH = StationLayout.GridH * StationLayout.CellSize * 0.5f + 6f;
                p.x = Mathf.Clamp(p.x, -halfW, halfW);
                p.z = Mathf.Clamp(p.z, -halfH, halfH);
                _rb.position = p;
            }
        }

        private void Update()
        {
            var delta = transform.position - _lastPosition;
            delta.y = 0f;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            _measuredSpeed = Mathf.Lerp(_measuredSpeed, delta.magnitude / dt, 1f - Mathf.Exp(-14f * dt));
            _lastPosition = transform.position;
        }
    }
}
