using System;
using System.IO;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// The player's options and profile, saved as JSON in the user's data folder (settings.json) and applied at start.
    /// Some options only take effect once their system exists (music and effects volume need the mixer, the voice
    /// options need voice chat, shake and subtitles need something that shakes or speaks); they are saved already.
    /// </summary>
    [Serializable]
    public class DMSettings
    {
        public const int MaxNicknameLength = 12;
        public static readonly int[] FrameLimits = { 0, 30, 60, 120, 144, 240 };
        public static readonly string[] Languages = { "ko", "en" };

        // Display (applied only once the player picked them; otherwise the window stays as launched).
        public bool displaySaved;
        public int resolutionWidth;
        public int resolutionHeight;
        public FullScreenMode windowMode = FullScreenMode.FullScreenWindow;
        /// <summary>-1 = the project's default quality level.</summary>
        public int qualityLevel = -1;
        public bool vSync = true;
        /// <summary>Frames per second, 0 = no limit.</summary>
        public int frameLimit;

        // Audio, 0..1.
        public float masterVolume = 1f;
        public float musicVolume = 0.8f;
        public float effectsVolume = 1f;
        public float voiceVolume = 1f;

        // Controls.
        public float mouseSensitivity = 1f;
        public bool invertY;
        /// <summary>Input System binding overrides (JSON), empty = default keys.</summary>
        public string bindingOverrides = string.Empty;

        // Voice chat (saved for when voice chat exists).
        /// <summary>Empty = the system default microphone.</summary>
        public string microphone = string.Empty;
        public bool pushToTalk = true;

        // Accessibility.
        public bool reduceShake;
        /// <summary>How far the picture may fade toward the sketch as the dream collapses (1 = as designed).</summary>
        public float collapseIntensity = 1f;
        public bool subtitles = true;

        // Language and profile.
        public string language = "ko";
        public string nickname = string.Empty;
        public string lastAddress = "127.0.0.1";

        static DMSettings current;

        /// <summary>Tests point this at a temporary file so they never touch the player's settings.</summary>
        public static string PathOverride { get; set; }

        /// <summary>
        /// settings.json in the user's data folder. -dmprofile=NAME (dev builds) uses settings_NAME.json instead, so two
        /// copies on one PC keep their own nickname and keys.
        /// </summary>
        public static string FilePath
        {
            get
            {
                if (PathOverride != null)
                {
                    return PathOverride;
                }
                string file = DMArgs.TryGet("dmprofile=", out string profile) && IsSafeProfileName(profile) ? $"settings_{profile}.json" : "settings.json";
                return Path.Combine(Application.persistentDataPath, file);
            }
        }

        static bool IsSafeProfileName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 16)
            {
                return false;
            }
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                {
                    return false;
                }
            }
            return true;
        }

        public static DMSettings Current
        {
            get
            {
                if (current == null)
                {
                    current = Load();
                }
                return current;
            }
        }

        /// <summary>Raised after <see cref="Apply"/>, so open screens and the local doll pick up new values.</summary>
        public static event Action Changed;

        public static DMSettings Load()
        {
            var settings = new DMSettings();
            try
            {
                if (File.Exists(FilePath))
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), settings);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DreamMovers] settings could not be read, using defaults: {e.Message}");
                settings = new DMSettings();
            }
            settings.Sanitize();
            return settings;
        }

        /// <summary>Forget the loaded copy (tests, after changing <see cref="PathOverride"/>).</summary>
        public static void Reload()
        {
            current = Load();
        }

        public void Save()
        {
            Sanitize();
            try
            {
                string folder = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DreamMovers] settings could not be saved: {e.Message}");
            }
        }

        /// <summary>Back to the defaults, keeping the profile (nickname, last address).</summary>
        public void ResetOptions()
        {
            var defaults = new DMSettings { nickname = nickname, lastAddress = lastAddress };
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(defaults), this);
        }

        public void Sanitize()
        {
            masterVolume = Mathf.Clamp01(masterVolume);
            musicVolume = Mathf.Clamp01(musicVolume);
            effectsVolume = Mathf.Clamp01(effectsVolume);
            voiceVolume = Mathf.Clamp01(voiceVolume);
            mouseSensitivity = Mathf.Clamp(mouseSensitivity, 0.1f, 3f);
            collapseIntensity = Mathf.Clamp01(collapseIntensity);
            frameLimit = Mathf.Max(0, frameLimit);
            if (Array.IndexOf(Languages, language) < 0)
            {
                language = Languages[0];
            }
            nickname = CleanNickname(nickname);
            bindingOverrides ??= string.Empty;
            microphone ??= string.Empty;
            lastAddress = string.IsNullOrWhiteSpace(lastAddress) ? "127.0.0.1" : lastAddress.Trim();
        }

        /// <summary>Trimmed, one line, at most <see cref="MaxNicknameLength"/> characters (empty stays empty).</summary>
        public static string CleanNickname(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }
            name = name.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
            return name.Length > MaxNicknameLength ? name.Substring(0, MaxNicknameLength) : name;
        }

        /// <summary>The nickname, picking and saving a random one the first time.</summary>
        public string EnsureNickname()
        {
            if (string.IsNullOrEmpty(nickname))
            {
                nickname = $"인형{UnityEngine.Random.Range(1000, 10000)}";
                Save();
            }
            return nickname;
        }

        public void Apply()
        {
            AudioListener.volume = masterVolume;
            DMControls.LoadOverrides(bindingOverrides);
            // Batch runs (tests, the network check) keep their window and frame rate as launched.
            if (!Application.isBatchMode)
            {
                QualitySettings.vSyncCount = vSync ? 1 : 0;
                Application.targetFrameRate = frameLimit > 0 ? frameLimit : -1;
                if (qualityLevel >= 0 && qualityLevel < QualitySettings.names.Length && qualityLevel != QualitySettings.GetQualityLevel())
                {
                    QualitySettings.SetQualityLevel(qualityLevel, true);
                }
                if (displaySaved)
                {
                    // Only the window mode chosen so far: keep the current size.
                    int width = resolutionWidth > 0 ? resolutionWidth : Screen.width;
                    int height = resolutionHeight > 0 ? resolutionHeight : Screen.height;
                    if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != windowMode)
                    {
                        Screen.SetResolution(width, height, windowMode);
                    }
                }
            }
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyAtStartup()
        {
            current = null;
            Current.Apply();
        }
    }
}
