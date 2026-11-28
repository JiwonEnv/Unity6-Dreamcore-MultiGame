using System;
using System.Linq;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Command line test hooks (dev builds and the editor only). Only flags count (leading '-'), so a value such as
    /// "-testFilter DMGameplayTests" never turns a hook on. Same rule as the level generator's -plg flags.
    /// </summary>
    public static class DMArgs
    {
        static bool Allowed => Debug.isDebugBuild || Application.isEditor;

        public static bool Has(string name)
        {
            return Allowed && Environment.GetCommandLineArgs().Any(a => a.StartsWith("-") && string.Equals(a.TrimStart('-'), name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>"-name=value" style flags.</summary>
        public static bool TryGet(string prefix, out string value)
        {
            value = null;
            if (!Allowed)
            {
                return false;
            }
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (!arg.StartsWith("-"))
                {
                    continue;
                }
                string trimmed = arg.TrimStart('-');
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    value = trimmed.Substring(prefix.Length);
                    return true;
                }
            }
            return false;
        }
    }
}
