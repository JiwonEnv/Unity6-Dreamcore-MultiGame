using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DreamMovers
{
    /// <summary>
    /// Wires the settings layout (UI_Settings.uxml) to <see cref="DMSettings"/>: every change applies at once, closing
    /// saves. Keys are changed by clicking one and pressing the new key (Esc cancels).
    /// </summary>
    public class DMSettingsScreen : IDisposable
    {
        static readonly string[] TabNames = { "graphics", "audio", "controls", "voice", "accessibility", "language" };
        static readonly (string Label, FullScreenMode Mode)[] WindowModes =
        {
            ("전체 화면", FullScreenMode.ExclusiveFullScreen),
            ("테두리 없는 전체 화면", FullScreenMode.FullScreenWindow),
            ("창 모드", FullScreenMode.Windowed),
        };
        static readonly string[] LanguageLabels = { "한국어", "English" };

        readonly VisualElement root;
        readonly List<Vector2Int> resolutions = new List<Vector2Int>();
        readonly List<string> microphones = new List<string>();
        InputActionRebindingExtensions.RebindingOperation rebinding;
        int rebindEndedFrame = -10;
        /// <summary>Something changed since the last save (saved on close, and on scene change or quit if still open).</summary>
        bool dirty;
        Label rebindStatus;
        ScrollView bindingList;

        DropdownField resolution;
        DropdownField windowMode;
        DropdownField quality;
        Toggle vSync;
        DropdownField frameLimit;
        SliderInt masterVolume;
        SliderInt musicVolume;
        SliderInt effectsVolume;
        SliderInt voiceVolume;
        Slider sensitivity;
        Toggle invertY;
        DropdownField microphone;
        Toggle pushToTalk;
        Toggle reduceShake;
        Slider collapseIntensity;
        Toggle subtitles;
        DropdownField language;

        static DMSettings Settings => DMSettings.Current;

        /// <summary>Esc belongs to a key change in progress (or the one that just ended), not to the screens.</summary>
        public bool EatsEscape => rebinding != null || Time.frameCount - rebindEndedFrame <= 1;

        public bool IsRebinding => rebinding != null;

        public DMSettingsScreen(VisualElement root)
        {
            this.root = root;
            foreach (string tab in TabNames)
            {
                Button button = root.Q<Button>($"tab-{tab}");
                if (button != null)
                {
                    string name = tab;
                    button.clicked += () => ShowTab(name);
                }
            }

            resolution = Bind<DropdownField, string>("resolution", OnResolution);
            windowMode = Bind<DropdownField, string>("window-mode", _ => { Settings.windowMode = WindowModes[Mathf.Max(0, windowMode.index)].Mode; Settings.displaySaved = true; Changed(); });
            quality = Bind<DropdownField, string>("quality", _ => { Settings.qualityLevel = quality.index; Changed(); });
            vSync = Bind<Toggle, bool>("vsync", v => { Settings.vSync = v; Changed(); });
            frameLimit = Bind<DropdownField, string>("frame-limit", _ => { Settings.frameLimit = DMSettings.FrameLimits[Mathf.Max(0, frameLimit.index)]; Changed(); });

            masterVolume = Bind<SliderInt, int>("master-volume", v => { Settings.masterVolume = v / 100f; Changed(); });
            musicVolume = Bind<SliderInt, int>("music-volume", v => { Settings.musicVolume = v / 100f; Changed(); });
            effectsVolume = Bind<SliderInt, int>("effects-volume", v => { Settings.effectsVolume = v / 100f; Changed(); });
            voiceVolume = Bind<SliderInt, int>("voice-volume", v => { Settings.voiceVolume = v / 100f; Changed(); });

            sensitivity = Bind<Slider, float>("sensitivity", v => { Settings.mouseSensitivity = v; Changed(); });
            invertY = Bind<Toggle, bool>("invert-y", v => { Settings.invertY = v; Changed(); });
            rebindStatus = root.Q<Label>("rebind-status");
            bindingList = root.Q<ScrollView>("bindings");
            Button resetBindings = root.Q<Button>("reset-bindings");
            if (resetBindings != null)
            {
                resetBindings.clicked += ResetBindings;
            }

            microphone = Bind<DropdownField, string>("microphone", _ => { Settings.microphone = microphone.index <= 0 ? string.Empty : microphones[microphone.index - 1]; Changed(); });
            pushToTalk = Bind<Toggle, bool>("push-to-talk", v => { Settings.pushToTalk = v; Changed(); });

            reduceShake = Bind<Toggle, bool>("reduce-shake", v => { Settings.reduceShake = v; Changed(); });
            collapseIntensity = Bind<Slider, float>("collapse-intensity", v => { Settings.collapseIntensity = v; Changed(); });
            subtitles = Bind<Toggle, bool>("subtitles", v => { Settings.subtitles = v; Changed(); });

            language = Bind<DropdownField, string>("language", _ => { Settings.language = DMSettings.Languages[Mathf.Max(0, language.index)]; Changed(); });

            Button defaults = root.Q<Button>("defaults");
            if (defaults != null)
            {
                defaults.clicked += ResetAll;
            }
            ShowTab(TabNames[0]);
        }

        TField Bind<TField, TValue>(string name, Action<TValue> onChange) where TField : BaseField<TValue>
        {
            var field = root.Q<TField>(name);
            if (field == null)
            {
                Debug.LogWarning($"[DreamMovers] settings field '{name}' is missing from the layout");
                return null;
            }
            field.RegisterValueChangedCallback(e => onChange(e.newValue));
            return field;
        }

        void Changed()
        {
            dirty = true;
            Settings.Apply();
        }

        public void ShowTab(string name)
        {
            foreach (string tab in TabNames)
            {
                VisualElement page = root.Q<VisualElement>($"page-{tab}");
                if (page != null)
                {
                    page.style.display = tab == name ? DisplayStyle.Flex : DisplayStyle.None;
                }
                root.Q<Button>($"tab-{tab}")?.EnableInClassList("tab--selected", tab == name);
            }
        }

        /// <summary>Fill every field from the saved settings.</summary>
        public void Open()
        {
            DMSettings settings = Settings;

            resolutions.Clear();
            foreach (Resolution r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!resolutions.Contains(size))
                {
                    resolutions.Add(size);
                }
            }
            var currentSize = new Vector2Int(settings.displaySaved && settings.resolutionWidth > 0 ? settings.resolutionWidth : Screen.width,
                settings.displaySaved && settings.resolutionHeight > 0 ? settings.resolutionHeight : Screen.height);
            if (!resolutions.Contains(currentSize))
            {
                resolutions.Add(currentSize);
            }
            resolutions.Sort((a, b) => b.x != a.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            SetChoices(resolution, resolutions.Select(r => $"{r.x} × {r.y}").ToList(), resolutions.IndexOf(currentSize));

            FullScreenMode mode = settings.displaySaved ? settings.windowMode : Screen.fullScreenMode;
            int modeIndex = Array.FindIndex(WindowModes, m => m.Mode == mode);
            SetChoices(windowMode, WindowModes.Select(m => m.Label).ToList(), modeIndex < 0 ? 1 : modeIndex);
            SetChoices(quality, QualitySettings.names.ToList(), settings.qualityLevel >= 0 ? settings.qualityLevel : QualitySettings.GetQualityLevel());
            vSync?.SetValueWithoutNotify(settings.vSync);
            int limitIndex = Array.IndexOf(DMSettings.FrameLimits, settings.frameLimit);
            SetChoices(frameLimit, DMSettings.FrameLimits.Select(f => f == 0 ? "제한 없음" : $"{f} fps").ToList(), limitIndex < 0 ? 0 : limitIndex);

            masterVolume?.SetValueWithoutNotify(Mathf.RoundToInt(settings.masterVolume * 100f));
            musicVolume?.SetValueWithoutNotify(Mathf.RoundToInt(settings.musicVolume * 100f));
            effectsVolume?.SetValueWithoutNotify(Mathf.RoundToInt(settings.effectsVolume * 100f));
            voiceVolume?.SetValueWithoutNotify(Mathf.RoundToInt(settings.voiceVolume * 100f));

            sensitivity?.SetValueWithoutNotify(settings.mouseSensitivity);
            invertY?.SetValueWithoutNotify(settings.invertY);
            RefreshBindings();

            microphones.Clear();
            microphones.AddRange(Microphone.devices);
            var micChoices = new List<string> { "기본 장치" };
            micChoices.AddRange(microphones);
            int micIndex = microphones.IndexOf(settings.microphone);
            SetChoices(microphone, micChoices, micIndex < 0 ? 0 : micIndex + 1);
            pushToTalk?.SetValueWithoutNotify(settings.pushToTalk);

            reduceShake?.SetValueWithoutNotify(settings.reduceShake);
            collapseIntensity?.SetValueWithoutNotify(settings.collapseIntensity);
            subtitles?.SetValueWithoutNotify(settings.subtitles);

            int languageIndex = Array.IndexOf(DMSettings.Languages, settings.language);
            SetChoices(language, LanguageLabels.ToList(), languageIndex < 0 ? 0 : languageIndex);
        }

        static void SetChoices(DropdownField field, List<string> choices, int index)
        {
            if (field == null)
            {
                return;
            }
            field.choices = choices;
            field.SetValueWithoutNotify(choices.Count > 0 ? choices[Mathf.Clamp(index, 0, choices.Count - 1)] : string.Empty);
        }

        void OnResolution(string _)
        {
            if (resolution.index < 0 || resolution.index >= resolutions.Count)
            {
                return;
            }
            Vector2Int size = resolutions[resolution.index];
            Settings.resolutionWidth = size.x;
            Settings.resolutionHeight = size.y;
            if (!Settings.displaySaved)
            {
                Settings.windowMode = Screen.fullScreenMode;
                Settings.displaySaved = true;
            }
            Changed();
        }

        /// <summary>Saves (the screen is closing).</summary>
        public void Close()
        {
            CancelRebind();
            Settings.Save();
            dirty = false;
        }

        void ResetAll()
        {
            CancelRebind();
            Settings.ResetOptions();
            // The defaults say nothing about the window, which would stay as it is: put it back to the monitor's own size.
            Settings.displaySaved = true;
            Settings.resolutionWidth = Screen.currentResolution.width;
            Settings.resolutionHeight = Screen.currentResolution.height;
            Settings.windowMode = FullScreenMode.FullScreenWindow;
            Changed();
            Open();
        }

        // ---- Keys ----

        void RefreshBindings()
        {
            if (bindingList == null)
            {
                return;
            }
            bindingList.Clear();
            foreach (DMControls.Rebindable key in DMControls.Rebindables())
            {
                var row = new VisualElement();
                row.AddToClassList("key-row");
                row.Add(new Label(key.Label));
                var button = new Button { text = DMControls.KeyName(key) };
                button.AddToClassList("button--small");
                DMControls.Rebindable target = key;
                button.clicked += () => StartRebind(target, button);
                row.Add(button);
                bindingList.Add(row);
            }
            if (rebindStatus != null)
            {
                rebindStatus.text = "키를 누르면 바꿀 수 있습니다";
            }
        }

        void StartRebind(DMControls.Rebindable key, Button button)
        {
            if (rebinding != null)
            {
                return;
            }
            bool wasEnabled = DMControls.Map.enabled;
            DMControls.Map.Disable();
            string oldPath = key.Action.bindings[key.BindingIndex].effectivePath;
            button.text = "…";
            if (rebindStatus != null)
            {
                rebindStatus.text = $"{key.Label}: 새 키를 누르세요 (Esc 취소)";
            }
            // While waiting, clicks must not reach tabs or buttons (they would also become the new key).
            root.SetEnabled(false);
            InputActionRebindingExtensions.RebindingOperation operation = key.Action.PerformInteractiveRebinding(key.BindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Pointer>/position")
                .OnMatchWaitForAnother(0.1f)
                .OnComplete(_ => EndRebind(wasEnabled, key, oldPath))
                .OnCancel(_ => EndRebind(wasEnabled, key, null));
            if (key.Action != DMControls.Grab)
            {
                // The left button is how a menu is clicked; only Grab (its default) may take it.
                operation.WithControlsExcluding("<Mouse>/leftButton");
            }
            rebinding = operation.Start();
        }

        void EndRebind(bool reenable, DMControls.Rebindable key, string oldPath)
        {
            rebinding?.Dispose();
            rebinding = null;
            rebindEndedFrame = Time.frameCount;
            root.SetEnabled(true);
            string swapped = oldPath != null ? SwapDuplicate(key, oldPath) : null;
            Settings.bindingOverrides = DMControls.SaveOverrides();
            dirty = true;
            if (reenable)
            {
                DMControls.Map.Enable();
            }
            RefreshBindings();
            if (swapped != null && rebindStatus != null)
            {
                rebindStatus.text = $"같은 키를 쓰던 '{swapped}'에는 원래 키({InputControlPath.ToHumanReadableString(oldPath, InputControlPath.HumanReadableStringOptions.OmitDevice)})를 넣었습니다";
            }
        }

        /// <summary>
        /// One key, one job: if another action already used the new key, it takes over this action's old key.
        /// Returns that action's label, or null when there was no clash.
        /// </summary>
        public static string SwapDuplicate(DMControls.Rebindable changed, string oldPath)
        {
            string newPath = changed.Action.bindings[changed.BindingIndex].effectivePath;
            if (string.IsNullOrEmpty(newPath) || newPath == oldPath)
            {
                return null;
            }
            foreach (DMControls.Rebindable other in DMControls.Rebindables())
            {
                if (other.Action == changed.Action && other.BindingIndex == changed.BindingIndex)
                {
                    continue;
                }
                if (other.Action.bindings[other.BindingIndex].effectivePath == newPath)
                {
                    other.Action.ApplyBindingOverride(other.BindingIndex, oldPath);
                    return other.Label;
                }
            }
            return null;
        }

        void CancelRebind()
        {
            rebinding?.Cancel();
        }

        void ResetBindings()
        {
            CancelRebind();
            dirty = true;
            DMControls.Map.RemoveAllBindingOverrides();
            Settings.bindingOverrides = string.Empty;
            Changed();
            RefreshBindings();
        }

        /// <summary>The scene is going away (room dropped, game quitting): keep what was changed.</summary>
        public void Dispose()
        {
            CancelRebind();
            rebinding?.Dispose();
            rebinding = null;
            if (dirty)
            {
                Settings.Save();
                dirty = false;
            }
        }
    }
}
