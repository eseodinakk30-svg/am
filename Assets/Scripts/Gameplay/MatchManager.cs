// -----------------------------------------------------------------------------
//  NEBULA NINE - the match state machine.
//
//  Lobby -> RoleReveal -> Roaming -> (MeetingIntro -> Discussion -> Voting ->
//  VoteResult -> Ejection) -> Roaming -> ... -> GameOver
//
//  The manager is authoritative: in multiplayer only the host runs it and pushes
//  the results to clients, which keeps the game cheat resistant (clients never
//  decide who dies or what a vote result is).
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using Nebula.AI;
using Nebula.Characters;
using Nebula.Core;
using Nebula.Map;
using Nebula.Tasks;

namespace Nebula.Gameplay
{
    public class ChatLine
    {
        public int SpeakerId;
        public string Text;
        public float Time;
        public bool GhostChannel;
        public SpeechIntent Intent;
    }

    public class MeetingState
    {
        public PlayerState Caller;
        public PlayerState BodyVictim;
        public bool IsEmergency;
        public readonly Dictionary<int, int> Votes = new Dictionary<int, int>();
        public readonly List<ChatLine> Chat = new List<ChatLine>();
        public bool VotingOpen;
        public PlayerState Ejected;
        public bool Skipped;
        public int MeetingNumber;

        public void Reset()
        {
            Caller = null;
            BodyVictim = null;
            IsEmergency = false;
            Votes.Clear();
            Chat.Clear();
            VotingOpen = false;
            Ejected = null;
            Skipped = false;
        }
    }

    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        public MatchSettings Settings { get; private set; }
        public readonly List<PlayerState> Players = new List<PlayerState>();
        public PlayerState Local { get; private set; }
        public MatchPhase Phase { get; private set; } = MatchPhase.Lobby;
        public float PhaseTimer { get; private set; }
        public TaskSystem Tasks { get; private set; }
        public SabotageSystem Sabotage { get; private set; }
        public StationView Station { get; set; }
        public NebulaRandom Rng { get; private set; }
        public readonly MeetingState Meeting = new MeetingState();
        public WinSide Winner { get; private set; } = WinSide.None;
        public WinReason Reason { get; private set; } = WinReason.None;
        public float MatchTime { get; private set; }
        public bool Running => Phase != MatchPhase.Lobby && Phase != MatchPhase.GameOver;

        private readonly List<DeadBody> _bodies = new List<DeadBody>();
        private readonly List<NpcBrain> _brains = new List<NpcBrain>();
        private DebateDirector _debate;
        private Transform _actorRoot;
        private Transform _worldRoot;
        private float _visionFactor = 1f;
        private int _brainCursor;
        private float _emergencyCooldown;
        private int _meetingCount;

        public IReadOnlyList<NpcBrain> Brains => _brains;
        public IReadOnlyList<DeadBody> Bodies => _bodies;
        public float VisionFactor => _visionFactor;
        public DebateDirector Debate => _debate;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            GameEvents.VisionFactorChanged += SetVisionFactor;
        }

        private void OnDisable()
        {
            GameEvents.VisionFactorChanged -= SetVisionFactor;
        }

        // ==================================================================
        //  setup
        // ==================================================================
        public void StartMatch(MatchSettings settings, string localName, int humanSeats = 1, int seed = 0)
        {
            Settings = settings ?? new MatchSettings();
            Settings.Validate();
            Rng = new NebulaRandom(seed != 0 ? seed : Settings.RandomSeed != 0 ? Settings.RandomSeed : Random.Range(1, int.MaxValue));

            _worldRoot = new GameObject("MatchWorld").transform;
            _actorRoot = new GameObject("Actors").transform;
            _actorRoot.SetParent(_worldRoot, false);

            Tasks = new TaskSystem();
            Tasks.Init(_worldRoot);
            Sabotage = new SabotageSystem();
            Sabotage.Init(Settings, Station);

            BuildRoster(localName, humanSeats);
            SpawnActors();
            CreateBrains();

            _debate = new DebateDirector();
            _meetingCount = 0;
            MatchTime = 0f;
            Winner = WinSide.None;
            Reason = WinReason.None;

            GameEvents.RaiseMatchStarted();

            // Сперва комната ожидания: все стоят в кафетерии, роли ещё никому не
            // розданы. Раздача — по кнопке СТАРТ, как в оригинале жанра.
            MoveEveryoneToLobby();
            SetPhase(MatchPhase.Lobby, 0f);
        }

        /// <summary>Расставляет всех кружком у стола в кафетерии.</summary>
        private void MoveEveryoneToLobby()
        {
            var cafe = StationLayout.Get("cafeteria");
            Vector3 centre = cafe != null
                ? StationLayout.CellToWorld(cafe.Deck, cafe.CenterCell.x, cafe.CenterCell.y)
                : Vector3.zero;

            for (int i = 0; i < Players.Count; i++)
            {
                float angle = i / Mathf.Max(1f, Players.Count) * Mathf.PI * 2f;
                float radius = 3.4f + (i % 2) * 1.5f;
                var pos = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Players[i].Position = pos;
                if (Players[i].View is Actor a && a.Motor != null) a.Motor.Teleport(pos);
            }
        }

        /// <summary>Хост нажал СТАРТ: раздаём роли, задания и запускаем показ роли.</summary>
        public void LaunchFromLobby()
        {
            if (Phase != MatchPhase.Lobby) return;

            Settings.Validate();
            AssignRoles();
            Tasks.AssignAll(Players, Settings, Rng);
            SpawnAtStartPoints();

            SetPhase(MatchPhase.RoleReveal, 4f);
            Tasks.RefreshMarkers(Local);
        }

        private void SpawnAtStartPoints()
        {
            var spawns = Station != null ? Station.SpawnPoints : new List<Vector3>();
            if (spawns.Count == 0) return;
            for (int i = 0; i < Players.Count; i++)
            {
                var pos = spawns[i % spawns.Count];
                Players[i].Position = pos;
                if (Players[i].View is Actor a && a.Motor != null) a.Motor.Teleport(pos);
            }
        }

        private void BuildRoster(string localName, int humanSeats)
        {
            Players.Clear();
            Local = null;
            var colors = new List<int>();
            for (int i = 0; i < ColorBank.Suits.Length; i++) colors.Add(i);
            Rng.Shuffle(colors);

            var profile = GameSettings.Profile;

            for (int i = 0; i < Settings.PlayerCount; i++)
            {
                bool isHuman = i < humanSeats;
                var p = new PlayerState
                {
                    Id = i,
                    IsBot = !isHuman,
                    IsLocal = i == 0,
                    ColorIndex = colors[i % colors.Count],
                };

                if (isHuman && i == 0)
                {
                    p.Name = string.IsNullOrWhiteSpace(localName) ? profile.DisplayName : localName;
                    p.ColorIndex = profile.ColorIndex >= 0 ? profile.ColorIndex : p.ColorIndex;
                    p.HatIndex = profile.HatIndex;
                    p.OutfitIndex = profile.OutfitIndex;
                    p.AccessoryIndex = profile.AccessoryIndex;
                    p.TrailIndex = profile.TrailIndex;
                    Local = p;
                }
                else
                {
                    p.Name = NameBank.RandomNpcName(new System.Random((int)Rng.NextUInt()));
                    p.HatIndex = Rng.NextInt(CosmeticBank.Hats.Length);
                    p.OutfitIndex = Rng.NextInt(CosmeticBank.Outfits.Length);
                    p.AccessoryIndex = Rng.NextInt(CosmeticBank.Accessories.Length);
                    p.TrailIndex = Rng.Chance(0.25f) ? Rng.NextInt(CosmeticBank.Trails.Length) : 0;
                }

                Players.Add(p);
            }

            FixDuplicateColors();
            GameEvents.RaiseRosterChanged();
        }

        private void FixDuplicateColors()
        {
            int len = ColorBank.Suits.Length;
            var used = new HashSet<int>();

            // the local player keeps the colour chosen in the customisation screen
            if (Local != null)
            {
                Local.ColorIndex = ((Local.ColorIndex % len) + len) % len;
                used.Add(Local.ColorIndex);
            }

            foreach (var p in Players)
            {
                if (p == Local) continue;
                int c = ((p.ColorIndex % len) + len) % len;
                int guard = 0;
                while (used.Contains(c) && guard++ < len) c = (c + 1) % len;
                p.ColorIndex = c;
                used.Add(c);
            }
        }

        private void AssignRoles()
        {
            foreach (var p in Players) p.ResetForNewMatch();

            var order = new List<PlayerState>(Players);
            Rng.Shuffle(order);
            int infiltrators = Mathf.Clamp(Settings.InfiltratorCount, 1, Mathf.Max(1, (Players.Count - 1) / 3));

            // Пожелание владельца устройства выполняем до общей раздачи: иначе
            // предатель выпадал бы ему раз в семь-восемь матчей, и посмотреть на
            // эту половину игры было бы попросту нечем.
            if (Local != null && Settings.MyRole != RoleWish.Random)
            {
                order.Remove(Local);
                if (Settings.MyRole == RoleWish.AlwaysInfiltrator)
                {
                    Local.Role = Role.Infiltrator;
                    infiltrators--;
                }
                else
                {
                    Local.Role = Role.Crew;
                }
            }

            for (int i = 0; i < infiltrators && i < order.Count; i++)
                order[i].Role = Role.Infiltrator;

            AssignSpecialRoles();

            foreach (var p in Players)
                p.KillCooldown = p.Role == Role.Infiltrator ? Settings.FirstKillDelay : 0f;
        }

        /// <summary>Профессии поверх стороны. Раздаются отдельно и на условия победы не влияют.</summary>
        private void AssignSpecialRoles()
        {
            var crew = new List<PlayerState>();
            var impostors = new List<PlayerState>();
            foreach (var p in Players)
            {
                if (p.Role == Role.Infiltrator) impostors.Add(p);
                else crew.Add(p);
            }
            Rng.Shuffle(crew);
            Rng.Shuffle(impostors);

            int idx = 0;
            for (int i = 0; i < Settings.ScientistCount && idx < crew.Count; i++, idx++)
                crew[idx].Special = SpecialRole.Scientist;
            for (int i = 0; i < Settings.EngineerCount && idx < crew.Count; i++, idx++)
                crew[idx].Special = SpecialRole.Engineer;

            for (int i = 0; i < Settings.ShapeshifterCount && i < impostors.Count; i++)
                impostors[i].Special = SpecialRole.Shapeshifter;
        }

        // ------------------------------------------------------------------ профессии
        private void TickSpecialRoles(float dt)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                if (p == null) continue;

                if (p.ShapeshiftCooldown > 0f) p.ShapeshiftCooldown -= dt;

                if (p.DisguisedAs >= 0)
                {
                    p.ShapeshiftLeft -= dt;
                    // облик слетает сам по времени и мгновенно — если носитель погиб
                    if (p.ShapeshiftLeft <= 0f || !p.IsAlive) EndShapeshift(p);
                }

                // заряд показателей жизни медленно восстанавливается
                if (p.Special == SpecialRole.Scientist && p.VitalsCharge < 1f)
                    p.VitalsCharge = Mathf.Min(1f, p.VitalsCharge + dt / 90f);
            }
        }

        public bool CanShapeshift(PlayerState p)
        {
            return p != null && p.IsAlive && p.Special == SpecialRole.Shapeshifter
                   && p.DisguisedAs < 0 && p.ShapeshiftCooldown <= 0f
                   && Phase == MatchPhase.Roaming;
        }

        public void BeginShapeshift(PlayerState self, PlayerState target)
        {
            if (!CanShapeshift(self) || target == null) return;

            self.DisguisedAs = target.Id;
            self.ShapeshiftLeft = Settings.ShapeshiftDuration;
            self.ShapeshiftCooldown = Settings.ShapeshiftCooldown + Settings.ShapeshiftDuration;

            if (self.View is Actor a)
                a.Visual.SetDisguise(target.ColorIndex, target.Label);
        }

        public void EndShapeshift(PlayerState self)
        {
            if (self == null || self.DisguisedAs < 0) return;
            self.DisguisedAs = -1;
            self.ShapeshiftLeft = 0f;
            if (self.View is Actor a)
                a.Visual.ClearDisguise(self.ColorIndex, self.Label);
        }

        /// <summary>Название роли для экрана показа и для списка на собрании.</summary>
        public static string RoleTitle(PlayerState p)
        {
            if (p == null) return "";
            if (p.Role == Role.Infiltrator)
                return p.Special == SpecialRole.Shapeshifter ? "ОБОРОТЕНЬ" : "ДИВЕРСАНТ";
            switch (p.Special)
            {
                case SpecialRole.Scientist: return "УЧЁНЫЙ";
                case SpecialRole.Engineer: return "ИНЖЕНЕР";
                default: return "ЭКИПАЖ";
            }
        }

        public static string RoleHint(PlayerState p)
        {
            if (p == null) return "";
            if (p.Role == Role.Infiltrator)
                return p.Special == SpecialRole.Shapeshifter
                    ? "Убивай, ходи по вентиляции и на время принимай облик любого из экипажа."
                    : "Убивай экипаж, ходи по вентиляции и ломай станцию так, чтобы никто не понял.";
            switch (p.Special)
            {
                case SpecialRole.Scientist:
                    return "Выполняй задания. В любой момент можешь посмотреть, кто ещё жив, — заряд тратится.";
                case SpecialRole.Engineer:
                    return "Выполняй задания. Тебе, единственному из экипажа, открыта вентиляция.";
                default:
                    return "Выполняй задания и вычисли диверсантов раньше, чем они вычислят вас.";
            }
        }

        private void SpawnActors()
        {
            var spawns = Station != null ? Station.SpawnPoints : new List<Vector3>();
            for (int i = 0; i < Players.Count; i++)
            {
                var pos = spawns.Count > 0 ? spawns[i % spawns.Count] : Vector3.zero;
                var actor = Actor.Spawn(Players[i], _actorRoot, pos);
                actor.Motor.BaseSpeed = Settings.MoveSpeed;
                actor.Motor.Frozen = true;
                GameEvents.RaisePlayerSpawned(Players[i]);
            }
        }

        private void CreateBrains()
        {
            _brains.Clear();
            foreach (var p in Players)
            {
                if (!p.IsBot) continue;
                var brain = new NpcBrain();
                brain.Init(p, this, Settings.AiDifficulty, new NebulaRandom((int)Rng.NextUInt()));
                p.Brain = brain;
                _brains.Add(brain);
            }
        }

        // ==================================================================
        //  phases
        // ==================================================================
        public void SetPhase(MatchPhase phase, float duration)
        {
            Phase = phase;
            PhaseTimer = duration;
            GameEvents.RaisePhaseChanged(phase);

            // В лобби все ходят свободно — это отдельная комната ожидания, а не пауза.
            bool frozen = phase != MatchPhase.Roaming && phase != MatchPhase.Lobby;
            foreach (var p in Players)
            {
                if (p.View is Actor a && a.Motor != null)
                    a.Motor.Frozen = frozen && !(p.IsGhost && phase == MatchPhase.Roaming);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (!Running && Phase != MatchPhase.GameOver) return;

            if (RemoteControlled)
            {
                // clients only advance timers and visuals; every decision arrives from the host
                if (Phase != MatchPhase.GameOver) MatchTime += dt;
                if (PhaseTimer > 0f) PhaseTimer -= dt;
                Sabotage?.UpdateVisualsOnly(dt);
                return;
            }

            if (Phase != MatchPhase.GameOver) MatchTime += dt;
            if (PhaseTimer > 0f) PhaseTimer -= dt;
            if (_emergencyCooldown > 0f) _emergencyCooldown -= dt;

            switch (Phase)
            {
                case MatchPhase.RoleReveal:
                    if (PhaseTimer <= 0f) BeginRound();
                    break;

                case MatchPhase.Roaming:
                    TickRoaming(dt);
                    TickSpecialRoles(dt);
                    TickDoorLog();
                    break;

                case MatchPhase.MeetingIntro:
                    if (PhaseTimer <= 0f) BeginDiscussion();
                    break;

                case MatchPhase.Discussion:
                    _debate?.Tick(dt);
                    if (PhaseTimer <= 0f) BeginVoting();
                    break;

                case MatchPhase.Voting:
                    _debate?.Tick(dt);
                    TickVoting(dt);
                    break;

                case MatchPhase.VoteResult:
                    if (PhaseTimer <= 0f) BeginEjection();
                    break;

                case MatchPhase.Ejection:
                    if (PhaseTimer <= 0f) EndMeeting();
                    break;
            }
        }

        private void BeginRound()
        {
            Sabotage.ResetForRound();
            foreach (var p in Players)
            {
                if (p.Role == Role.Infiltrator && p.IsAlive)
                    p.KillCooldown = Mathf.Max(p.KillCooldown, Settings.FirstKillDelay);
            }
            foreach (var b in _brains) b.OnRoundStart();
            SetPhase(MatchPhase.Roaming, 0f);
            Audio.MusicDirector.Instance?.SetMood(Audio.MusicMood.Calm);
        }

        private void TickRoaming(float dt)
        {
            // kill cooldowns
            foreach (var p in Players)
                if (p.KillCooldown > 0f) p.KillCooldown -= dt;

            Sabotage.Update(dt);

            if (Sabotage.CriticalExpired)
            {
                Finish(WinSide.Infiltrators, WinReason.SabotageTimeout);
                return;
            }

            TickBrains(dt);
            CheckWinConditions();
        }

        /// <summary>
        /// Brains are time sliced: heavy reasoning runs for a few agents per frame,
        /// light sensing runs for everybody.  15 agents cost well under a millisecond.
        /// </summary>
        private void TickBrains(float dt)
        {
            if (_brains.Count == 0) return;

            for (int i = 0; i < _brains.Count; i++) _brains[i].TickFast(dt);

            int budget = Mathf.Clamp(Mathf.CeilToInt(_brains.Count / 4f), 1, 5);
            for (int i = 0; i < budget; i++)
            {
                _brainCursor = (_brainCursor + 1) % _brains.Count;
                _brains[_brainCursor].TickSlow(dt * _brains.Count / Mathf.Max(1, budget));
            }
        }

        // ==================================================================
        //  actions
        // ==================================================================
        public bool CanKill(PlayerState killer, PlayerState victim)
        {
            if (Phase != MatchPhase.Roaming) return false;
            if (killer == null || victim == null) return false;
            if (!killer.IsAlive || !victim.IsAlive) return false;
            if (killer.Role != Role.Infiltrator) return false;
            if (victim.Role == Role.Infiltrator) return false;
            if (killer.KillCooldown > 0f) return false;
            if (killer.Deck != victim.Deck) return false;
            if (victim.InVent) return false;
            return Vector3.Distance(killer.Position, victim.Position) <= Settings.KillRange + 0.4f;
        }

        public PlayerState FindKillTarget(PlayerState killer)
        {
            if (killer == null || killer.Role != Role.Infiltrator || killer.KillCooldown > 0f) return null;
            PlayerState best = null;
            float bestD = Settings.KillRange * Settings.KillRange;
            foreach (var p in Players)
            {
                if (p == killer || !p.IsAlive || p.Role == Role.Infiltrator) continue;
                if (p.Deck != killer.Deck || p.InVent) continue;
                float d = (p.Position - killer.Position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        public bool TryKill(PlayerState killer, PlayerState victim)
        {
            if (!CanKill(killer, victim)) return false;

            killer.KillCooldown = Settings.KillCooldown;
            victim.Life = LifeState.Murdered;
            victim.DeathTime = MatchTime;
            victim.KilledById = killer.Id;
            victim.HasUnreportedBody = true;
            victim.BodyPosition = victim.Position;
            victim.BodyRoomId = victim.RoomId;
            victim.BodyDeck = victim.Deck;

            if (killer.View is Actor ka)
            {
                ka.PlayKillAnimation();
                if (killer.InVent) { }
                else ka.Motor.Teleport(Vector3.Lerp(killer.Position, victim.Position, 0.55f));
            }

            if (victim.View is Actor va)
            {
                va.PlayDeath();
                va.BecomeGhost();
                va.Motor.Teleport(victim.Position);
            }

            _bodies.Add(DeadBody.Spawn(victim, _worldRoot));
            GameEvents.RaiseKilled(killer, victim);

            if (victim.IsLocal)
            {
                Audio.SoundBank.Play(Audio.Sfx.Kill, 1f);
                Audio.MusicDirector.Instance?.SetMood(Audio.MusicMood.Silence);
            }

            CheckWinConditions();
            return true;
        }

        public PlayerState FindReportableBody(PlayerState reporter)
        {
            if (reporter == null || !reporter.IsAlive || Phase != MatchPhase.Roaming) return null;
            foreach (var b in _bodies)
            {
                if (b == null || b.Victim == null) continue;
                if (!b.Victim.HasUnreportedBody) continue;
                if (b.Deck != reporter.Deck) continue;
                if (Vector3.Distance(b.transform.position, reporter.Position) <= Settings.ReportRange)
                    return b.Victim;
            }
            return null;
        }

        public bool TryReport(PlayerState reporter, PlayerState victim)
        {
            if (reporter == null || victim == null || !reporter.IsAlive) return false;
            if (Phase != MatchPhase.Roaming) return false;
            if (!victim.HasUnreportedBody) return false;

            GameEvents.RaiseBodyReported(reporter, victim);
            StartMeeting(reporter, victim, false);
            return true;
        }

        public bool CanCallEmergency(PlayerState caller)
        {
            if (caller == null || !caller.IsAlive || Phase != MatchPhase.Roaming) return false;
            if (caller.EmergenciesUsed >= Settings.EmergencyMeetingsPerPlayer) return false;
            if (_emergencyCooldown > 0f) return false;
            if (Sabotage.IsCritical) return false;
            var cafeteria = StationLayout.Get("cafeteria");
            if (cafeteria == null) return false;
            return caller.RoomId == cafeteria.Id;
        }

        public bool TryEmergency(PlayerState caller)
        {
            if (!CanCallEmergency(caller)) return false;
            caller.EmergenciesUsed++;
            _emergencyCooldown = Settings.EmergencyCooldown;
            StartMeeting(caller, null, true);
            return true;
        }

        public bool TryUseVent(PlayerState player, out VentPoint vent)
        {
            vent = null;
            // не только диверсанты: инженеру вентиляция положена по роли, и без
            // этой проверки кнопка вентиляции у него просто ничего не делала
            if (player == null || !player.CanUseVents || !player.IsAlive) return false;
            if (Station == null) return false;
            vent = Station.NearestVent(player.Position, player.Deck, 2.6f);
            if (vent == null) return false;
            if (player.View is Actor a)
            {
                if (player.InVent) a.ExitVent(vent);
                else a.EnterVent(vent);
                return true;
            }
            return false;
        }

        public List<VentPoint> ConnectedVents(PlayerState player)
        {
            var result = new List<VentPoint>();
            if (player == null || !player.InVent || Station == null) return result;
            var current = Station.VentById(player.VentId);
            if (current == null) return result;
            foreach (var v in Station.VentsInNetwork(current.Def.Network))
                if (v.Def.Id != current.Def.Id) result.Add(v);
            return result;
        }

        public bool TryElevator(PlayerState player)
        {
            if (player == null || Station == null || !player.IsAlive) return false;
            var pad = Station.NearestElevator(player.Position, player.Deck, 3.2f);
            if (pad == null || pad.Cooldown > 0f) return false;
            pad.Cooldown = 1.5f;
            if (player.View is Actor a)
            {
                a.Motor.Teleport(pad.OtherSideWorld);
                player.Deck = pad.OtherDeck;
                a.PlaySound(Audio.Sfx.Elevator, 0.8f);
                return true;
            }
            return false;
        }

        // ==================================================================
        //  meetings
        // ==================================================================
        public void StartMeeting(PlayerState caller, PlayerState victim, bool emergency)
        {
            _meetingCount++;
            Meeting.Reset();
            Meeting.Caller = caller;
            Meeting.BodyVictim = victim;
            Meeting.IsEmergency = emergency;
            Meeting.MeetingNumber = _meetingCount;

            // corpses are cleared, unreported flags reset
            foreach (var b in _bodies) if (b != null) Object.Destroy(b.gameObject);
            _bodies.Clear();
            foreach (var p in Players) p.HasUnreportedBody = false;

            Sabotage.Resolve(false);
            Station?.OpenAllDoors();
            Station?.SetAllLights(1f);
            _visionFactor = 1f;

            // облик спадает при созыве: иначе за столом сидели бы два одинаковых
            // персонажа, и обсуждать было бы нечего
            foreach (var p in Players) if (p.DisguisedAs >= 0) EndShapeshift(p);

            // журнал перемещений относится к прошедшему кругу — после собрания
            // все стоят в столовой, и старые записи только путали бы
            ResetDoorLog();

            // правило «шкала заданий обновляется только на собраниях»
            Tasks?.PublishProgress();

            // gather everybody around the table
            var spawns = Station != null ? Station.SpawnPoints : null;
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                if (p.View is Actor a)
                {
                    if (p.IsAlive && spawns != null && spawns.Count > 0)
                        a.Motor.Teleport(spawns[i % spawns.Count]);
                    a.Motor.SetInput(Vector2.zero);
                    a.ClearActivity();
                    if (p.IsAlive) a.Visual.SetState(AnimState.Meeting);
                }
                p.InVent = false;
                p.VentId = -1;
            }

            GameEvents.RaiseMeetingStarted(caller, victim);
            Audio.SoundBank.Play(emergency ? Audio.Sfx.Emergency : Audio.Sfx.Report, 0.9f);
            Audio.MusicDirector.Instance?.SetMood(Audio.MusicMood.Meeting);

            foreach (var b in _brains) b.OnMeetingStart(Meeting);
            _debate.Begin(this, Meeting);

            SetPhase(MatchPhase.MeetingIntro, 3.2f);
        }

        private void BeginDiscussion()
        {
            SetPhase(MatchPhase.Discussion, Settings.DiscussionTime);
            Meeting.VotingOpen = false;
            _debate.BeginDiscussion();
        }

        private void BeginVoting()
        {
            SetPhase(MatchPhase.Voting, Settings.VotingTime);
            Meeting.VotingOpen = true;
            _debate.BeginVoting();
        }

        private void TickVoting(float dt)
        {
            int aliveCount = 0;
            foreach (var p in Players) if (p.IsAlive) aliveCount++;
            if (Meeting.Votes.Count >= aliveCount || PhaseTimer <= 0f)
            {
                Meeting.VotingOpen = false;
                ResolveVotes();
                SetPhase(MatchPhase.VoteResult, 5.5f);
            }
        }

        public bool CastVote(PlayerState voter, int targetId)
        {
            if (!Meeting.VotingOpen || voter == null || !voter.IsAlive) return false;
            if (Meeting.Votes.ContainsKey(voter.Id)) return false;
            Meeting.Votes[voter.Id] = targetId;
            GameEvents.RaiseVoteCast(voter, targetId);
            Audio.SoundBank.Play(Audio.Sfx.VoteCast, 0.6f);
            return true;
        }

        private void ResolveVotes()
        {
            var tally = new Dictionary<int, int>();
            foreach (var kv in Meeting.Votes)
            {
                if (!tally.ContainsKey(kv.Value)) tally[kv.Value] = 0;
                tally[kv.Value]++;
            }

            int bestTarget = -1, bestCount = 0;
            bool tie = false;
            foreach (var kv in tally)
            {
                if (kv.Value > bestCount) { bestCount = kv.Value; bestTarget = kv.Key; tie = false; }
                else if (kv.Value == bestCount) tie = true;
            }

            if (bestTarget < 0 || tie || bestCount == 0)
            {
                Meeting.Skipped = true;
                Meeting.Ejected = null;
            }
            else
            {
                Meeting.Skipped = false;
                Meeting.Ejected = PlayerById(bestTarget);
            }
        }

        private void BeginEjection()
        {
            if (Meeting.Ejected != null)
            {
                var p = Meeting.Ejected;
                p.Life = LifeState.Ejected;
                if (p.View is Actor a)
                {
                    a.BecomeGhost();
                    a.Visual.SetState(AnimState.Ejected);
                }
                GameEvents.RaiseEjected(p, p.Role == Role.Infiltrator);
                Audio.SoundBank.Play(Audio.Sfx.Eject, 0.9f);
            }
            else
            {
                GameEvents.RaiseEjected(null, false);
            }

            SetPhase(MatchPhase.Ejection, Meeting.Ejected != null ? 5.5f : 3f);
        }

        private void EndMeeting()
        {
            foreach (var b in _brains) b.OnMeetingEnd();
            GameEvents.RaiseMeetingEnded();

            foreach (var p in Players)
            {
                if (p.Role == Role.Infiltrator && p.IsAlive)
                    p.KillCooldown = Settings.KillCooldown;
                if (p.View is Actor a && p.IsAlive) a.Visual.SetState(AnimState.Idle);
            }

            Tasks.Recalculate(Players);
            Tasks.RefreshMarkers(Local);

            if (CheckWinConditions()) return;
            SetPhase(MatchPhase.Roaming, 0f);
            Audio.MusicDirector.Instance?.SetMood(Audio.MusicMood.Calm);
        }

        // ==================================================================
        //  win conditions
        // ==================================================================
        public bool CheckWinConditions()
        {
            if (Phase == MatchPhase.GameOver) return true;

            int aliveCrew = 0, aliveInf = 0;
            foreach (var p in Players)
            {
                if (!p.IsAlive) continue;
                if (p.Role == Role.Infiltrator) aliveInf++;
                else aliveCrew++;
            }

            if (aliveInf == 0)
            {
                Finish(WinSide.Crew, WinReason.AllInfiltratorsEjected);
                return true;
            }
            if (aliveInf >= aliveCrew)
            {
                Finish(WinSide.Infiltrators, WinReason.InfiltratorsReachedParity);
                return true;
            }
            if (Tasks != null && Tasks.AllCrewTasksDone())
            {
                Finish(WinSide.Crew, WinReason.TasksComplete);
                return true;
            }
            return false;
        }

        public void Finish(WinSide side, WinReason reason)
        {
            if (Phase == MatchPhase.GameOver) return;
            Winner = side;
            Reason = reason;
            SetPhase(MatchPhase.GameOver, 0f);

            foreach (var p in Players)
            {
                if (p.View is Actor a)
                {
                    bool won = (side == WinSide.Crew && p.Role == Role.Crew) ||
                               (side == WinSide.Infiltrators && p.Role == Role.Infiltrator);
                    a.Visual.SetVisible(true);
                    a.Visual.SetState(won ? AnimState.Win : AnimState.Lose);
                    a.Motor.Frozen = true;
                }
            }

            GameEvents.RaiseGameOver(side, reason);
            Audio.MusicDirector.Instance?.SetMood(Audio.MusicMood.Silence);
            bool localWon = Local != null && ((side == WinSide.Crew && Local.Role == Role.Crew) ||
                                              (side == WinSide.Infiltrators && Local.Role == Role.Infiltrator));
            Audio.SoundBank.Play(localWon ? Audio.Sfx.Win : Audio.Sfx.Lose, 0.9f);
        }

        // ==================================================================
        //  queries
        // ==================================================================
        public PlayerState PlayerById(int id)
        {
            for (int i = 0; i < Players.Count; i++) if (Players[i].Id == id) return Players[i];
            return null;
        }

        public List<PlayerState> AlivePlayers()
        {
            var list = new List<PlayerState>();
            foreach (var p in Players) if (p.IsAlive) list.Add(p);
            return list;
        }

        public int AliveCount()
        {
            int n = 0;
            foreach (var p in Players) if (p.IsAlive) n++;
            return n;
        }

        public float VisionRadiusFor(PlayerState p)
        {
            if (p == null) return Settings.CrewVision;
            if (p.IsGhost) return 100f;
            float baseRadius = p.Role == Role.Infiltrator ? Settings.InfiltratorVision : Settings.CrewVision;
            return baseRadius * _visionFactor;
        }

        public void SetVisionFactor(float factor)
        {
            _visionFactor = Mathf.Clamp(factor, 0.15f, 1.4f);
        }

        /// <summary>
        /// The single authority for "can this participant perceive that point".
        /// Both the player's rendering and the NPC perception go through here, which
        /// is what keeps the AI honest.
        /// </summary>
        public bool CanSee(PlayerState observer, Vector3 point, DeckId pointDeck, float extraRange = 0f)
        {
            if (observer == null) return false;
            if (observer.IsGhost) return true;
            if (observer.Deck != pointDeck) return false;
            if (observer.InVent) return false;

            float radius = VisionRadiusFor(observer) + extraRange;
            var flatA = new Vector3(observer.Position.x, 0f, observer.Position.z);
            var flatB = new Vector3(point.x, 0f, point.z);
            if ((flatA - flatB).sqrMagnitude > radius * radius) return false;

            var grid = StationGrid.Instance;
            if (grid == null) return true;
            return grid.LineOfSight(pointDeck, observer.Position, point);
        }

        public bool CanSeePlayer(PlayerState observer, PlayerState target)
        {
            if (target == null) return false;
            if (target.InVent && (observer == null || !observer.IsGhost)) return false;
            return CanSee(observer, target.Position, target.Deck);
        }

        public List<PlayerState> InRoom(int roomId)
        {
            var list = new List<PlayerState>();
            foreach (var p in Players)
                if (p.IsAlive && p.RoomId == roomId) list.Add(p);
            return list;
        }

        public void AddChat(PlayerState speaker, string text, SpeechIntent intent = SpeechIntent.None, bool ghost = false)
        {
            var line = new ChatLine
            {
                SpeakerId = speaker?.Id ?? -1,
                Text = text,
                Time = Time.time,
                GhostChannel = ghost,
                Intent = intent,
            };
            Meeting.Chat.Add(line);
            if (Meeting.Chat.Count > 220) Meeting.Chat.RemoveAt(0);
            GameEvents.RaiseChat(speaker, text, ghost);
        }

        // ==================================================================
        //  remote (client) mode - the host owns every decision
        // ==================================================================
        public bool RemoteControlled { get; private set; }

        public void StartRemoteMatch(MatchSettings settings, List<PlayerState> roster, int localId)
        {
            RemoteControlled = true;
            Settings = settings ?? new MatchSettings();
            Settings.Validate();
            Rng = new NebulaRandom(12345);

            if (_worldRoot == null)
            {
                _worldRoot = new GameObject("MatchWorld").transform;
                _actorRoot = new GameObject("Actors").transform;
                _actorRoot.SetParent(_worldRoot, false);
            }

            Tasks ??= new TaskSystem();
            Tasks.Init(_worldRoot);
            Sabotage ??= new SabotageSystem();
            Sabotage.Init(Settings, Station);

            // reuse existing actors when the roster is only being refreshed
            bool rebuild = Players.Count != roster.Count;
            if (rebuild)
            {
                foreach (var p in Players)
                    if (p.View is Actor a && a != null) Destroy(a.gameObject);
                Players.Clear();
            }

            var spawns = Station != null ? Station.SpawnPoints : new List<Vector3>();
            for (int i = 0; i < roster.Count; i++)
            {
                var incoming = roster[i];
                PlayerState p = rebuild ? null : PlayerById(incoming.Id);
                if (p == null)
                {
                    p = incoming;
                    p.IsLocal = p.Id == localId;
                    Players.Add(p);
                    var pos = spawns.Count > 0 ? spawns[i % spawns.Count] : Vector3.zero;
                    var actor = Actor.Spawn(p, _actorRoot, pos);
                    actor.Motor.BaseSpeed = Settings.MoveSpeed;
                    actor.Motor.Frozen = !p.IsLocal;
                    if (p.IsLocal) Local = p;
                    GameEvents.RaisePlayerSpawned(p);
                }
                else
                {
                    p.Name = incoming.Name;
                    p.IsBot = incoming.IsBot;
                    p.IsLocal = p.Id == localId;
                    if (p.IsLocal) Local = p;
                }
            }

            GameEvents.RaiseRosterChanged();
            GameEvents.RaiseMatchStarted();
        }

        public void ApplyRemotePhase(MatchPhase phase, float timer)
        {
            Phase = phase;
            PhaseTimer = timer;
            GameEvents.RaisePhaseChanged(phase);

            bool frozen = phase != MatchPhase.Roaming;
            foreach (var p in Players)
                if (p.View is Actor a && a.Motor != null)
                    a.Motor.Frozen = frozen || !p.IsLocal;

            if (phase == MatchPhase.MeetingIntro) Meeting.Reset();
        }

        public void ApplyRemoteKill(int killerId, int victimId, Vector3 bodyPosition, int roomId)
        {
            var killer = PlayerById(killerId);
            var victim = PlayerById(victimId);
            if (victim == null) return;

            victim.Life = LifeState.Murdered;
            victim.HasUnreportedBody = true;
            victim.BodyPosition = bodyPosition;
            victim.BodyRoomId = roomId;
            victim.BodyDeck = StationLayout.DeckOfWorld(bodyPosition);
            victim.DeathTime = MatchTime;
            victim.KilledById = killerId;

            if (killer?.View is Actor ka) ka.PlayKillAnimation();
            if (victim.View is Actor va)
            {
                va.PlayDeath();
                va.BecomeGhost();
            }
            _bodies.Add(DeadBody.Spawn(victim, _worldRoot));
            GameEvents.RaiseKilled(killer, victim);
        }

        public void ApplyRemoteMeeting(int callerId, int victimId, bool emergency)
        {
            Meeting.Reset();
            Meeting.Caller = PlayerById(callerId);
            Meeting.BodyVictim = victimId >= 0 ? PlayerById(victimId) : null;
            Meeting.IsEmergency = emergency;
            Meeting.MeetingNumber++;
            Meeting.VotingOpen = false;

            foreach (var b in _bodies) if (b != null) Destroy(b.gameObject);
            _bodies.Clear();
            foreach (var p in Players) p.HasUnreportedBody = false;

            Sabotage?.Resolve(false);
            Station?.OpenAllDoors();
            Station?.SetAllLights(1f);
            GameEvents.RaiseMeetingStarted(Meeting.Caller, Meeting.BodyVictim);
        }

        public void ApplyRemoteVote(int voterId, int targetId)
        {
            Meeting.Votes[voterId] = targetId;
            GameEvents.RaiseVoteCast(PlayerById(voterId), targetId);
        }

        public void ApplyRemoteEject(int playerId, bool wasInfiltrator)
        {
            var p = playerId >= 0 ? PlayerById(playerId) : null;
            if (p != null)
            {
                p.Life = LifeState.Ejected;
                if (p.View is Actor a)
                {
                    a.BecomeGhost();
                    a.Visual.SetState(AnimState.Ejected);
                }
            }
            Meeting.Ejected = p;
            Meeting.Skipped = p == null;
            GameEvents.RaiseEjected(p, wasInfiltrator);
        }

        // ==================================================================
        //  журнал перемещений (узел связи)
        // ==================================================================
        /// <summary>Одна запись журнала: кого зафиксировали и где.</summary>
        public struct DoorLogEntry
        {
            public int SubjectId;      // тот, кого видит журнал: облик, а не носитель
            public int RoomId;
            public float Time;
        }

        private const int DoorLogCapacity = 24;
        private readonly List<DoorLogEntry> _doorLog = new List<DoorLogEntry>(DoorLogCapacity);
        private readonly Dictionary<int, int> _doorLogLastRoom = new Dictionary<int, int>();

        /// <summary>Записи от новых к старым.</summary>
        public IReadOnlyList<DoorLogEntry> DoorLog => _doorLog;

        /// <summary>
        /// Узел связи пишет, кто в какой отсек заходил. Никаких скрытых данных:
        /// фиксируется только смена отсека, ровно то же, что увидел бы датчик на
        /// двери. Оборотень попадает в журнал под чужим обликом.
        /// </summary>
        private void TickDoorLog()
        {
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                if (p == null || !p.IsAlive || p.InVent || p.RoomId < 0) continue;

                if (_doorLogLastRoom.TryGetValue(p.Id, out int prev) && prev == p.RoomId) continue;
                _doorLogLastRoom[p.Id] = p.RoomId;
                if (prev == p.RoomId) continue;

                var area = StationLayout.Get(p.RoomId);
                if (area == null || area.Type != AreaType.Room) continue;   // коридоры не пишем

                _doorLog.Insert(0, new DoorLogEntry
                {
                    SubjectId = p.DisguisedAs >= 0 ? p.DisguisedAs : p.Id,
                    RoomId = p.RoomId,
                    Time = MatchTime,
                });
                if (_doorLog.Count > DoorLogCapacity) _doorLog.RemoveAt(_doorLog.Count - 1);
            }
        }

        private void ResetDoorLog()
        {
            _doorLog.Clear();
            _doorLogLastRoom.Clear();
        }

        public void ApplyRemoteTaskProgress(int playerId, float progress)
        {
            Tasks?.SetRemoteProgress(progress);
            GameEvents.RaiseTaskProgressChanged(progress);
        }

        public void Teardown()
        {
            if (_worldRoot != null) Destroy(_worldRoot.gameObject);
            _worldRoot = null;
            _actorRoot = null;
            _bodies.Clear();
            _brains.Clear();
            Players.Clear();
            Local = null;
            RemoteControlled = false;
            Phase = MatchPhase.Lobby;
        }
    }
}
