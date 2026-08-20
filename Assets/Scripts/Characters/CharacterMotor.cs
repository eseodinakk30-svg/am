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

        /// <summary>Разгон и торможение, м/с². Мгновенная смена скорости выглядит роботом.</summary>
        public float Acceleration = 62f;
        public float Deceleration = 78f;

        private Rigidbody _rb;
        private CapsuleCollider _capsule;
        private Vector2 _input;
        private Vector3 _lastPosition;
        private float _measuredSpeed;
        private Vector3 _velocity;      // собственная скорость, без учёта отталкивания физикой
        private Vector3 _facing = Vector3.forward;

        public Vector2 Input => _input;
        public float MeasuredSpeed => _measuredSpeed;
        public float Speed01 => BaseSpeed <= 0.01f ? 0f : Mathf.Clamp01(_measuredSpeed / BaseSpeed);
        public Vector3 Velocity => _rb != null ? _rb.velocity : Vector3.zero;

        /// <summary>Сглаженное направление хода. Не сбрасывается в ноль на остановке —
        /// иначе персонаж дёргано разворачивался бы каждый раз, отпустив стик.</summary>
        public Vector3 Facing => _facing;

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
            _velocity = Vector3.zero;
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
                _velocity = Vector3.zero;
                _rb.velocity = Vector3.zero;
                return;
            }

            float dt = Time.fixedDeltaTime;
            float speed = BaseSpeed * SpeedMultiplier;
            var desired = new Vector3(_input.x, 0f, _input.y) * speed;

            // Разгон и торможение вместо мгновенной подстановки скорости: так шаг
            // начинается и заканчивается плавно, а не рывком.
            float rate = desired.sqrMagnitude > 0.0001f ? Acceleration : Deceleration;
            _velocity = Vector3.MoveTowards(_velocity, desired, rate * dt);

            // Скольжение вдоль стены: без этого персонаж намертво встаёт в угол,
            // хотя вдоль стены пройти может.
            if (!GhostMode && _velocity.sqrMagnitude > 0.0001f && _capsule != null)
            {
                var dir = _velocity.normalized;
                float dist = _velocity.magnitude * dt + _capsule.radius * 0.5f;
                var p0 = _rb.position + Vector3.up * (_capsule.center.y - _capsule.height * 0.5f + _capsule.radius);
                var p1 = _rb.position + Vector3.up * (_capsule.center.y + _capsule.height * 0.5f - _capsule.radius);
                if (Physics.CapsuleCast(p0, p1, _capsule.radius * 0.92f, dir, out var hit, dist,
                                        ~0, QueryTriggerInteraction.Ignore))
                {
                    var n = hit.normal; n.y = 0f;
                    if (n.sqrMagnitude > 0.0001f)
                    {
                        n.Normalize();
                        float into = Vector3.Dot(_velocity, n);
                        if (into < 0f) _velocity -= n * into;   // убираем только составляющую в стену
                    }
                }
            }

            _rb.velocity = _velocity;

            if (_velocity.sqrMagnitude > 0.25f)
            {
                var want = _velocity.normalized;
                _facing = Vector3.Slerp(_facing, want, 1f - Mathf.Exp(-16f * dt));
                if (_facing.sqrMagnitude > 0.0001f) _facing.Normalize();
            }

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
