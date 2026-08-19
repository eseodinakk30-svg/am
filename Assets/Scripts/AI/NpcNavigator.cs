// -----------------------------------------------------------------------------
//  NEBULA NINE - NPC path following.
//
//  Paths come from the station grid A*, are string pulled, and are consumed with
//  a little steering noise so two agents walking the same corridor do not look
//  like a conga line.  Cross deck destinations are automatically routed through
//  the nearest elevator.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;
using Nebula.Map;

namespace Nebula.AI
{
    public class NpcNavigator
    {
        private readonly List<Vector3> _path = new List<Vector3>();
        private int _index;
        private Vector3 _destination;
        private DeckId _destinationDeck;
        private float _repathTimer;
        private float _stuckTimer;
        private Vector3 _lastPosition;
        private bool _needsElevator;
        private ElevatorPad _elevatorTarget;
        private float _wobblePhase;

        public bool HasPath => _index < _path.Count;
        public Vector3 Destination => _destination;
        public DeckId DestinationDeck => _destinationDeck;
        public bool Arrived { get; private set; } = true;
        public float Wander;

        public void Stop()
        {
            _path.Clear();
            _index = 0;
            Arrived = true;
            _elevatorTarget = null;
            _needsElevator = false;
        }

        public void SetDestination(Vector3 position, DeckId deck, PlayerState owner)
        {
            _destination = position;
            _destinationDeck = deck;
            Arrived = false;
            _repathTimer = 0f;
            _needsElevator = owner != null && owner.Deck != deck;
            Repath(owner);
        }

        private void Repath(PlayerState owner)
        {
            _path.Clear();
            _index = 0;
            if (owner == null) return;

            var grid = StationGrid.Instance;
            var view = StationView.Instance;
            if (grid == null) return;

            Vector3 goal = _destination;
            DeckId deck = owner.Deck;

            if (_needsElevator && view != null)
            {
                _elevatorTarget = view.NearestElevator(owner.Position, owner.Deck, 400f);
                if (_elevatorTarget != null) goal = _elevatorTarget.transform.position;
            }
            else _elevatorTarget = null;

            grid.FindPath(deck, owner.Position, goal, _path);
            _wobblePhase = Random.Range(0f, 6.28f);
        }

        /// <summary>Returns the desired movement input for the motor.</summary>
        public Vector2 Tick(float dt, PlayerState owner)
        {
            if (owner == null) return Vector2.zero;

            _repathTimer -= dt;
            if (_repathTimer <= 0f)
            {
                _repathTimer = 1.35f + Random.value * 0.5f;
                if (!Arrived) Repath(owner);
            }

            // stuck detection
            if ((owner.Position - _lastPosition).sqrMagnitude < 0.0025f && !Arrived)
            {
                _stuckTimer += dt;
                if (_stuckTimer > 0.9f)
                {
                    _stuckTimer = 0f;
                    Repath(owner);
                    return new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized * 0.7f;
                }
            }
            else _stuckTimer = 0f;
            _lastPosition = owner.Position;

            if (_index >= _path.Count)
            {
                // reached the local goal
                if (_elevatorTarget != null)
                {
                    var match = Gameplay.MatchManager.Instance;
                    if (match != null && match.TryElevator(owner))
                    {
                        _needsElevator = false;
                        _elevatorTarget = null;
                        Repath(owner);
                        return Vector2.zero;
                    }
                }
                Arrived = true;
                return Vector2.zero;
            }

            var waypoint = _path[_index];
            var delta = waypoint - owner.Position;
            delta.y = 0f;
            float dist = delta.magnitude;

            if (dist < 0.85f)
            {
                _index++;
                if (_index >= _path.Count)
                {
                    if (_elevatorTarget == null) Arrived = true;
                    return Vector2.zero;
                }
                waypoint = _path[_index];
                delta = waypoint - owner.Position;
                delta.y = 0f;
                dist = delta.magnitude;
            }

            var dir = dist > 0.001f ? delta / dist : Vector3.zero;

            // slight lateral wobble so movement reads as organic
            if (Wander > 0.001f)
            {
                _wobblePhase += dt * 2.1f;
                var side = new Vector3(-dir.z, 0f, dir.x);
                dir += side * (Mathf.Sin(_wobblePhase) * Wander);
                dir.Normalize();
            }

            return new Vector2(dir.x, dir.z);
        }

        public float RemainingDistance(PlayerState owner)
        {
            if (owner == null || _index >= _path.Count) return 0f;
            float total = Vector3.Distance(owner.Position, _path[_index]);
            for (int i = _index; i < _path.Count - 1; i++)
                total += Vector3.Distance(_path[i], _path[i + 1]);
            return total;
        }
    }
}
