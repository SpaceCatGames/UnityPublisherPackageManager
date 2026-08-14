using System;
using System.Linq;
using UnityEditor;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build;
#endif

namespace SCG.UPPM.Helpers
{
    public class DefineSymbolsManager
    {
        #region Internals

        /// <summary>
        /// Returns the build target group for the currently active build target.
        /// Uses <c>BuildPipeline.GetBuildTargetGroup</c> on Unity 6000.3+ where
        /// <c>EditorUserBuildSettings.selectedBuildTargetGroup</c> is obsolete.
        /// </summary>
        private static BuildTargetGroup GetActiveBuildTargetGroup() =>
#if UNITY_6000_3_OR_NEWER
            BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
#else
            EditorUserBuildSettings.selectedBuildTargetGroup;
#endif

        #endregion

        /// <summary>
        /// Checks whether a scripting define symbol is present for the
        /// currently selected build target.
        /// <para>
        /// Uses the modern NamedBuildTarget API on Unity 2021.2+ and falls back
        /// to the legacy *ForGroup API on older versions.
        /// </para>
        /// </summary>
        /// <param name="define">Exact define symbol to check.</param>
        /// <returns>
        /// True if the symbol is present for the selected build target; otherwise false.
        /// </returns>
        public static bool HasDefineSymbol(string define)
        {
            if (string.IsNullOrWhiteSpace(define))
                return false;

#if UNITY_2021_2_OR_NEWER
            var named = NamedBuildTarget.FromBuildTargetGroup(GetActiveBuildTargetGroup());
            PlayerSettings.GetScriptingDefineSymbols(named, out var defines);
            return defines.Any(d => string.Equals(d, define, StringComparison.Ordinal));
#else
            // Legacy API: *ForGroup
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            var list = defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            return list.Any(d => string.Equals(d, define, StringComparison.Ordinal));
#endif
        }

        /// <summary>
        /// Add a scripting define symbol for the currently selected build target.
        /// Uses the modern NamedBuildTarget API when available (Unity 2021.2+).
        /// Falls back to legacy *ForGroup API on older versions.
        /// Returns true if the symbol was added; false when it already existed or input is invalid.
        /// Also attempts to add the same define for Android and iOS (best-effort, does not affect return).
        /// </summary>
        /// <param name="newDefine">Exact define symbol to add.</param>
        public static bool AddDefineSymbol(string newDefine)
        {
            if (string.IsNullOrWhiteSpace(newDefine))
                return false;

#if UNITY_2021_2_OR_NEWER
            var selected = NamedBuildTarget.FromBuildTargetGroup(GetActiveBuildTargetGroup());

            var changedOnSelected = AddTo(selected, newDefine);

            foreach (var t in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS })
            {
                if (!Equals(t, selected)) AddTo(t, newDefine);
            }

            return changedOnSelected;

            static bool AddTo(NamedBuildTarget t, string define)
            {
                PlayerSettings.GetScriptingDefineSymbols(t, out var defs);
                var list = defs.ToList();
                if (list.Any(d => string.Equals(d, define, StringComparison.Ordinal))) return false;

                list.Add(define);
                PlayerSettings.SetScriptingDefineSymbols(t, list.ToArray());
                return true;
            }
#else
            var selected = EditorUserBuildSettings.selectedBuildTargetGroup;
            var changedOnSelected = AddTo(selected, newDefine);

            foreach (var g in new[] { BuildTargetGroup.Android, BuildTargetGroup.iOS })
            {
                if (g != selected && g != BuildTargetGroup.Unknown) AddTo(g, newDefine);
            }

            return changedOnSelected;

            static bool AddTo(BuildTargetGroup g, string define)
            {
                if (g == BuildTargetGroup.Unknown) return false;

                var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(g);
                var list = defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

                if (list.Any(d => string.Equals(d, define, StringComparison.Ordinal))) return false;

                list.Add(define);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(g, string.Join(";", list));
                return true;
            }
#endif
        }

        /// <summary>
        /// Removes a scripting define symbol from the currently selected build target.
        /// Uses the modern NamedBuildTarget API when available; falls back to the legacy
        /// *ForGroup API on older Unity versions.
        /// </summary>
        /// <param name="defineToRemove">Exact define symbol to remove.</param>
        /// <returns>
        /// True if the symbol existed and was removed for the selected build target;
        /// false if the symbol was not present.
        /// </returns>
        public static bool RemoveDefineSymbol(string defineToRemove)
        {
            if (string.IsNullOrWhiteSpace(defineToRemove))
                return false;

#if UNITY_2021_2_OR_NEWER
            var selected = NamedBuildTarget.FromBuildTargetGroup(GetActiveBuildTargetGroup());

            var changedOnSelected = RemoveFrom(selected, defineToRemove);

            foreach (var t in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS })
            {
                if (!Equals(t, selected)) RemoveFrom(t, defineToRemove);
            }

            return changedOnSelected;

            static bool RemoveFrom(NamedBuildTarget t, string define)
            {
                PlayerSettings.GetScriptingDefineSymbols(t, out var defs);
                var list = defs.ToList();
                var removed = list.RemoveAll(d => string.Equals(d, define, StringComparison.Ordinal)) > 0;
                if (!removed) return false;

                PlayerSettings.SetScriptingDefineSymbols(t, list.ToArray());
                return true;
            }
#else
            var selected = EditorUserBuildSettings.selectedBuildTargetGroup;
            var changedOnSelected = RemoveFrom(selected, defineToRemove);

            foreach (var g in new[] { BuildTargetGroup.Android, BuildTargetGroup.iOS })
            {
                if (g != selected && g != BuildTargetGroup.Unknown) RemoveFrom(g, defineToRemove);
            }

            return changedOnSelected;

            static bool RemoveFrom(BuildTargetGroup g, string define)
            {
                if (g == BuildTargetGroup.Unknown) return false;

                var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(g);
                var list = defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

                var removed = list.RemoveAll(d => string.Equals(d, define, StringComparison.Ordinal)) > 0;
                if (!removed) return false;

                PlayerSettings.SetScriptingDefineSymbolsForGroup(g, string.Join(";", list));
                return true;
            }
#endif
        }
    }
}