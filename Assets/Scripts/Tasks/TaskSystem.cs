// -----------------------------------------------------------------------------
//  NEBULA NINE - task assignment, world consoles and the crew progress bar.
//
//  Infiltrators receive a task list too (flagged Fake) so their HUD, their walking
//  routes and the NPC "pretending to work" behaviour are indistinguishable from a
//  real crew member's - only the progress bar ignores them.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.Core;
using Nebula.Fx;
using Nebula.Map;

namespace Nebula.Tasks
{
    public class TaskStation : MonoBehaviour
    {
        public TaskId Task;
        public int RoomId;
        public Transform Marker;
        private float _spin;

        public void SetMarkerVisible(bool on)
        {
            if (Marker != null && Marker.gameObject.activeSelf != on) Marker.gameObject.SetActive(on);
        }

        private void Update()
        {
            if (Marker == null || !Marker.gameObject.activeSelf) return;
            _spin += Time.deltaTime;
            Marker.localRotation = Quaternion.Euler(0f, _spin * 90f, 0f);
            Marker.localPosition = new Vector3(0f, 2.6f + Mathf.Sin(_spin * 3f) * 0.15f, 0f);
        }
    }

    public class TaskSystem
    {
        private readonly Dictionary<long, TaskStation> _stations = new Dictionary<long, TaskStation>();
        private Transform _root;
        private float _crewProgress;    // истинный прогресс — от него зависит победа
        private float _shownProgress;   // то, что видят игроки на шкале
        private MatchSettings _settings;

        /// <summary>Значение шкалы, которое показывают всем: и HUD, и ИИ, и клиентам.</summary>
        public float CrewProgress => _shownProgress;
        public int TotalCrewStages { get; private set; }
        public int CompletedCrewStages { get; private set; }

        public void Init(Transform root)
        {
            _root = root;
        }

        // ------------------------------------------------------------------ assignment
        public void AssignAll(List<PlayerState> players, MatchSettings settings, NebulaRandom rng)
        {
            _settings = settings;
            _crewProgress = 0f;
            _shownProgress = 0f;

            var common = TaskCatalog.CommonPool();
            var chosenCommon = new List<TaskDefinition>();
            var commonCopy = new List<TaskDefinition>(common);
            rng.Shuffle(commonCopy);
            for (int i = 0; i < Mathf.Min(settings.CommonTasks, commonCopy.Count); i++)
                chosenCommon.Add(commonCopy[i]);

            foreach (var p in players)
            {
                p.Tasks.Clear();
                bool fake = p.Role == Role.Infiltrator;

                foreach (var def in chosenCommon)
                    p.Tasks.Add(MakeInstance(def, rng, fake));

                var longPool = TaskCatalog.LongPool();
                rng.Shuffle(longPool);
                for (int i = 0; i < Mathf.Min(settings.LongTasks, longPool.Count); i++)
                    p.Tasks.Add(MakeInstance(longPool[i], rng, fake));

                var shortPool = TaskCatalog.ShortPool(settings.VisualTasks);
                rng.Shuffle(shortPool);
                for (int i = 0; i < Mathf.Min(settings.ShortTasks, shortPool.Count); i++)
                    p.Tasks.Add(MakeInstance(shortPool[i], rng, fake));
            }

            BuildStations(players);
            Recalculate(players);
        }

        private TaskInstance MakeInstance(TaskDefinition def, NebulaRandom rng, bool fake)
        {
            var inst = new TaskInstance { Definition = def, Fake = fake };
            int stages = def.Stages;
            inst.StageRoomIds = new int[stages];

            if (def.StageRooms != null && def.StageRooms.Length > 0)
            {
                for (int i = 0; i < stages; i++)
                {
                    var area = StationLayout.Get(def.StageRooms[i]);
                    inst.StageRoomIds[i] = area?.Id ?? StationLayout.Get("cafeteria").Id;
                }
            }
            else
            {
                var pool = def.RoomPool;
                var key = pool != null && pool.Length > 0 ? pool[rng.NextInt(pool.Length)] : "cafeteria";
                var area = StationLayout.Get(key);
                inst.StageRoomIds[0] = area?.Id ?? 0;
            }
            return inst;
        }

        // ------------------------------------------------------------------ world consoles
        private void BuildStations(List<PlayerState> players)
        {
            foreach (var kv in _stations)
                if (kv.Value != null) Object.Destroy(kv.Value.gameObject);
            _stations.Clear();

            foreach (var p in players)
            foreach (var t in p.Tasks)
            foreach (var roomId in t.StageRoomIds)
                EnsureStation(roomId, t.Definition.Id);
        }

        private static long Key(int roomId, TaskId task) => ((long)roomId << 16) | (uint)(int)task;

        /// <summary>Deterministic console slot so every client puts the console in the same spot.</summary>
        public static int SlotFor(TaskId task) => Mathf.Abs((int)task * 7 + 3) % 8;

        public Vector3 StationPosition(int roomId, TaskId task)
        {
            var grid = StationGrid.Instance;
            if (grid == null) return Vector3.zero;
            return grid.StationPoint(roomId, SlotFor(task), out _);
        }

        private void EnsureStation(int roomId, TaskId task)
        {
            long key = Key(roomId, task);
            if (_stations.ContainsKey(key)) return;

            var grid = StationGrid.Instance;
            if (grid == null) return;
            var pos = grid.StationPoint(roomId, SlotFor(task), out float yaw);

            var go = new GameObject("Console_" + task + "_" + roomId);
            go.transform.SetParent(_root, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var pedestal = new GameObject("Pedestal");
            pedestal.transform.SetParent(go.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            pedestal.transform.localScale = new Vector3(1.25f, 1f, 0.75f);
            var pmf = pedestal.AddComponent<MeshFilter>();
            pmf.sharedMesh = Art.Cube;
            var pmr = pedestal.AddComponent<MeshRenderer>();
            pmr.sharedMaterial = Art.Lit(new Color(0.17f, 0.20f, 0.26f), 0.35f, 0.45f);
            pmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var screen = new GameObject("Screen");
            screen.transform.SetParent(go.transform, false);
            screen.transform.localPosition = new Vector3(0f, 1.05f, 0.18f);
            screen.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
            screen.transform.localScale = new Vector3(1.05f, 0.62f, 0.06f);
            var smf = screen.AddComponent<MeshFilter>();
            smf.sharedMesh = Art.Cube;
            var smr = screen.AddComponent<MeshRenderer>();
            smr.sharedMaterial = Art.Lit(new Color(0.30f, 0.72f, 0.88f), 0f, 0.85f, 2.2f);
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var marker = new GameObject("Marker");
            marker.transform.SetParent(go.transform, false);
            marker.transform.localPosition = new Vector3(0f, 2.6f, 0f);
            marker.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
            var mmf = marker.AddComponent<MeshFilter>();
            mmf.sharedMesh = Art.Cube;
            var mmr = marker.AddComponent<MeshRenderer>();
            mmr.sharedMaterial = Art.Lit(new Color(0.98f, 0.82f, 0.28f), 0f, 0.9f, 3f);
            mmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marker.SetActive(false);

            var station = go.AddComponent<TaskStation>();
            station.Task = task;
            station.RoomId = roomId;
            station.Marker = marker.transform;
            _stations[key] = station;
        }

        /// <summary>Highlights the consoles the local player still has to visit.</summary>
        public void RefreshMarkers(PlayerState local)
        {
            foreach (var kv in _stations)
                if (kv.Value != null) kv.Value.SetMarkerVisible(false);

            if (local == null) return;
            foreach (var t in local.Tasks)
            {
                if (t.IsComplete) continue;
                long key = Key(t.CurrentRoomId, t.Definition.Id);
                if (_stations.TryGetValue(key, out var st) && st != null) st.SetMarkerVisible(true);
            }
        }

        // ------------------------------------------------------------------ interaction
        public TaskInstance FindTaskInRange(PlayerState player, float range = 3.2f)
        {
            if (player == null || player.Tasks.Count == 0) return null;
            var grid = StationGrid.Instance;
            if (grid == null) return null;

            TaskInstance best = null;
            float bestDist = range * range;
            foreach (var t in player.Tasks)
            {
                if (t.IsComplete) continue;
                int roomId = t.CurrentRoomId;
                var area = StationLayout.Get(roomId);
                if (area == null || area.Deck != player.Deck) continue;
                var pos = StationPosition(roomId, t.Definition.Id);
                float d = (pos - player.Position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = t; }
            }
            return best;
        }

        /// <summary>Any player's nearest station position for a given task (used by NPC navigation).</summary>
        public Vector3 NextStationFor(TaskInstance task) => StationPosition(task.CurrentRoomId, task.Definition.Id);

        public void CompleteStage(PlayerState player, TaskInstance task, List<PlayerState> allPlayers)
        {
            if (player == null || task == null || task.IsComplete) return;
            int stage = task.StagesCompleted;
            task.StagesCompleted++;

            GameEvents.RaiseTaskStageDone(player, task, stage);
            Recalculate(allPlayers);
        }

        public void Recalculate(List<PlayerState> players)
        {
            int total = 0, done = 0;
            foreach (var p in players)
            {
                if (p.Role != Role.Crew) continue;
                if (p.Life == LifeState.Disconnected) continue;
                foreach (var t in p.Tasks)
                {
                    total += t.Definition.Stages;
                    done += t.StagesCompleted;
                }
            }

            TotalCrewStages = total;
            CompletedCrewStages = done;
            float value = total > 0 ? done / (float)total : 0f;
            if (!Mathf.Approximately(value, _crewProgress))
            {
                _crewProgress = value;
                // правило «шкала обновляется только на собраниях»: считаем прогресс
                // как обычно (от него зависит победа), но экипажу его не показываем
                if (_settings == null || _settings.TaskBarUpdatesAlways) PublishProgress();
            }
        }

        /// <summary>Показать накопленный прогресс — вызывается при созыве собрания.</summary>
        public void PublishProgress()
        {
            if (Mathf.Approximately(_shownProgress, _crewProgress)) return;
            _shownProgress = _crewProgress;
            GameEvents.RaiseTaskProgressChanged(_crewProgress);
        }

        /// <summary>Прогресс, присланный хостом: клиент сам ничего не считает.</summary>
        public void SetRemoteProgress(float value)
        {
            _crewProgress = Mathf.Clamp01(value);
            _shownProgress = _crewProgress;
        }

        public bool AllCrewTasksDone() => TotalCrewStages > 0 && CompletedCrewStages >= TotalCrewStages;
    }
}
