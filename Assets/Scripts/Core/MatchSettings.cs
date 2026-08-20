// -----------------------------------------------------------------------------
//  NEBULA NINE - match rules, tunable from the lobby screen and persisted
//  between sessions with PlayerPrefs + JsonUtility.
// -----------------------------------------------------------------------------

using System;
using UnityEngine;

namespace Nebula.Core
{
    [Serializable]
    public class MatchSettings
    {
        // -- roster ---------------------------------------------------------
        [Range(4, 15)] public int PlayerCount = 12;
        [Range(1, 3)] public int InfiltratorCount = 2;

        // -- роли -----------------------------------------------------------
        /// <summary>Кем достанется играть владельцу устройства.</summary>
        public RoleWish MyRole = RoleWish.Random;
        [Range(0, 3)] public int ScientistCount = 1;
        [Range(0, 3)] public int EngineerCount = 1;
        [Range(0, 2)] public int ShapeshifterCount = 1;
        public float ScientistVitalsSeconds = 10f;
        public float ShapeshiftDuration = 22f;
        public float ShapeshiftCooldown = 30f;

        // -- movement / vision ----------------------------------------------
        public float MoveSpeed = 6.2f;
        public float CrewVision = 13f;
        public float InfiltratorVision = 17f;
        public float LightsOutVision = 4.5f;

        // -- kill -----------------------------------------------------------
        public float KillCooldown = 27f;
        public float KillRange = 2.6f;
        public float FirstKillDelay = 12f;

        // -- meetings -------------------------------------------------------
        public int EmergencyMeetingsPerPlayer = 1;
        public float EmergencyCooldown = 25f;
        public float DiscussionTime = 32f;
        public float VotingTime = 45f;
        public bool AnonymousVotes = false;
        public bool ConfirmEjects = true;
        public float ReportRange = 3.2f;

        // -- tasks ----------------------------------------------------------
        public int CommonTasks = 2;
        public int LongTasks = 3;
        public int ShortTasks = 5;
        public bool VisualTasks = true;
        public bool TaskBarUpdatesAlways = true;

        // -- sabotage -------------------------------------------------------
        public float SabotageCooldown = 24f;
        public float ReactorMeltdownTime = 45f;
        public float OxygenDepletionTime = 40f;
        public float CoolantOverloadTime = 50f;
        public float DoorCloseDuration = 11f;
        public float LightsRepairFactor = 1f;

        // -- ai -------------------------------------------------------------
        public Difficulty AiDifficulty = Difficulty.Hard;
        public bool AiChatterInRoaming = true;

        // -- misc -----------------------------------------------------------
        public bool GhostsSeeGhosts = true;
        public bool GhostsDoTasks = true;
        public int RandomSeed = 0;   // 0 == time based

        public MatchSettings Clone()
        {
            return (MatchSettings)MemberwiseClone();
        }

        public void Validate()
        {
            PlayerCount = Mathf.Clamp(PlayerCount, 4, 15);
            int maxInf = Mathf.Max(1, (PlayerCount - 1) / 3);
            InfiltratorCount = Mathf.Clamp(InfiltratorCount, 1, Mathf.Min(3, maxInf));
            CommonTasks = Mathf.Clamp(CommonTasks, 0, 4);
            LongTasks = Mathf.Clamp(LongTasks, 0, 6);
            ShortTasks = Mathf.Clamp(ShortTasks, 0, 10);
            if (CommonTasks + LongTasks + ShortTasks == 0) ShortTasks = 3;
            MoveSpeed = Mathf.Clamp(MoveSpeed, 2f, 12f);
            KillCooldown = Mathf.Clamp(KillCooldown, 10f, 60f);
        }

        public int TotalTasksPerCrew => CommonTasks + LongTasks + ShortTasks;
    }

    /// <summary>Local player identity + client side options.</summary>
    [Serializable]
    public class PlayerProfile
    {
        public string DisplayName = "";
        public int ColorIndex = 0;
        public int HatIndex = 0;
        public int OutfitIndex = 0;
        public int AccessoryIndex = 0;
        public int TrailIndex = 0;
        public float MusicVolume = 0.55f;
        public float SfxVolume = 0.9f;
        public QualityTier Quality = QualityTier.High;
        public bool LeftHandedUi = false;
        public bool ScreenShake = true;
        public bool HapticFeedback = true;
        public int TargetFps = 60;
    }

    /// <summary>Global, process wide settings holder.</summary>
    public static class GameSettings
    {
        private const string MatchKey = "nebula.match.v1";
        private const string ProfileKey = "nebula.profile.v1";

        private static MatchSettings _match;
        private static PlayerProfile _profile;

        public static MatchSettings Match
        {
            get
            {
                if (_match == null)
                {
                    _match = new MatchSettings();
                    var json = PlayerPrefs.GetString(MatchKey, "");
                    if (!string.IsNullOrEmpty(json))
                    {
                        try { JsonUtility.FromJsonOverwrite(json, _match); }
                        catch (Exception) { _match = new MatchSettings(); }
                    }
                    _match.Validate();
                }
                return _match;
            }
        }

        public static PlayerProfile Profile
        {
            get
            {
                if (_profile == null)
                {
                    _profile = new PlayerProfile();
                    var json = PlayerPrefs.GetString(ProfileKey, "");
                    if (!string.IsNullOrEmpty(json))
                    {
                        try { JsonUtility.FromJsonOverwrite(json, _profile); }
                        catch (Exception) { _profile = new PlayerProfile(); }
                    }
                    if (string.IsNullOrWhiteSpace(_profile.DisplayName))
                        _profile.DisplayName = NameBank.RandomHumanName();
                }
                return _profile;
            }
        }

        public static void Save()
        {
            Match.Validate();
            PlayerPrefs.SetString(MatchKey, JsonUtility.ToJson(Match));
            PlayerPrefs.SetString(ProfileKey, JsonUtility.ToJson(Profile));
            PlayerPrefs.Save();
        }
    }
}
