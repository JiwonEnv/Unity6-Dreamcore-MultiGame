using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace DreamMovers
{
    /// <summary>
    /// The doll's input actions, shared by the local doll and the key settings, with the player's saved rebindings.
    /// Esc is not here: it always opens the pause menu (and cancels a rebinding).
    /// </summary>
    public static class DMControls
    {
        /// <summary>One key the player can change: an action and the binding inside it (WASD is four parts of Move).</summary>
        public struct Rebindable
        {
            public InputAction Action;
            public int BindingIndex;
            public string Label;
        }

        static readonly Dictionary<string, string> ActionLabels = new Dictionary<string, string>
        {
            { "Move", "이동" },
            { "Jump", "점프" },
            { "Sprint", "달리기" },
            { "Grab", "잡기 (누르고 있기)" },
            { "Crouch", "웅크리기 (누르고 있기)" },
            { "Interact", "서랍장 숨기 · 나오기" },
        };

        static readonly Dictionary<string, string> PartLabels = new Dictionary<string, string>
        {
            { "up", "앞" },
            { "down", "뒤" },
            { "left", "왼쪽" },
            { "right", "오른쪽" },
        };

        static InputActionMap map;

        public static InputActionMap Map
        {
            get
            {
                if (map == null)
                {
                    map = Build();
                    LoadOverrides(DMSettings.Current.bindingOverrides);
                }
                return map;
            }
        }

        public static InputAction Move => Map["Move"];
        public static InputAction Look => Map["Look"];
        public static InputAction Jump => Map["Jump"];
        public static InputAction Sprint => Map["Sprint"];
        public static InputAction Grab => Map["Grab"];
        public static InputAction HoldDistance => Map["HoldDistance"];
        public static InputAction Crouch => Map["Crouch"];
        public static InputAction Interact => Map["Interact"];

        static InputActionMap Build()
        {
            var actions = new InputActionMap("DreamMovers");
            InputAction move = actions.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            actions.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            actions.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            actions.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            actions.AddAction("Grab", InputActionType.Button, "<Mouse>/leftButton");
            actions.AddAction("HoldDistance", InputActionType.Value, "<Mouse>/scroll/y");
            actions.AddAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            actions.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            return actions;
        }

        public static void LoadOverrides(string json)
        {
            if (map == null)
            {
                // Built (with these overrides) the first time something asks for it.
                return;
            }
            map.RemoveAllBindingOverrides();
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    map.LoadBindingOverridesFromJson(json);
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[DreamMovers] saved key bindings could not be read, using defaults: {e.Message}");
                    map.RemoveAllBindingOverrides();
                }
            }
        }

        public static string SaveOverrides() => Map.SaveBindingOverridesAsJson();

        /// <summary>Every key the settings screen lists, in order.</summary>
        public static List<Rebindable> Rebindables()
        {
            var list = new List<Rebindable>();
            foreach (KeyValuePair<string, string> entry in ActionLabels)
            {
                InputAction action = Map[entry.Key];
                for (int i = 0; i < action.bindings.Count; ++i)
                {
                    InputBinding binding = action.bindings[i];
                    if (binding.isComposite)
                    {
                        continue;
                    }
                    string label = entry.Value;
                    if (binding.isPartOfComposite && PartLabels.TryGetValue(binding.name.ToLowerInvariant(), out string part))
                    {
                        label = $"{entry.Value} {part}";
                    }
                    list.Add(new Rebindable { Action = action, BindingIndex = i, Label = label });
                }
            }
            return list;
        }

        public static string KeyName(Rebindable key) => key.Action.GetBindingDisplayString(key.BindingIndex);

        /// <summary>One line per key, for the pause menu's key list.</summary>
        public static string Summary()
        {
            var lines = new List<string>();
            foreach (Rebindable key in Rebindables())
            {
                lines.Add($"{key.Label}: {KeyName(key)}");
            }
            lines.Add("시점: 마우스 · 실 거리: 휠 · 일시정지: Esc");
            return string.Join("\n", lines);
        }
    }
}
