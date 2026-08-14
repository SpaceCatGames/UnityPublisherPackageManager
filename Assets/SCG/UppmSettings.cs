using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SCG.UPPM.Helpers;
using static SCG.UPPM.Constants;

namespace SCG.UPPM
{
    /// <summary>
    /// Stores editor configuration used by Unity Publisher Package Manager.
    /// Keeps selected package.json fields synchronized with inspector values.
    /// Uses delayed serialized assignment to avoid unstable editor states during import.
    /// </summary>
    [CreateAssetMenu(
        fileName = nameof(UppmSettings),
        menuName = MenuRoot + nameof(UppmSettings))]
    public sealed class UppmSettings : ScriptableObject
    {
        #region Serialized

        /// <summary>Package version written to package.json and PlayerSettings.bundleVersion.</summary>
        [field: SerializeField]
        [field: Tooltip("Package version written to package.json and PlayerSettings.bundleVersion.")]
        public string PackageVersion { get; set; }

        [SerializeField, HideInInspector]
        private string _lastSyncedPackageVersion;

        /// <summary>Package id written to the package.json field \"name\".</summary>
        [field: SerializeField]
        [field: Tooltip("Package id written to the package.json field \"name\".")]
        public string PackageId { get; set; }

        [SerializeField, HideInInspector]
        private string _lastSyncedPackageId;

        /// <summary>Human-readable name written to the package.json field \"displayName\".</summary>
        [field: SerializeField]
        [field: Tooltip("Human-readable name written to the package.json field \"displayName\".")]
        public string PackageDisplayName { get; set; }

        [SerializeField, HideInInspector]
        private string _lastSyncedPackageDisplayName;

        /// <summary>Description written to the package.json field \"description\".</summary>
        [field: SerializeField]
        [field: Tooltip("Description written to the package.json field \"description\".")]
        [field: TextArea(1, 25)]
        public string PackageDescription { get; set; }

        [SerializeField, HideInInspector]
        private string _lastSyncedPackageDescription;

        /// <summary>Project-relative folder name of the package root under Assets.</summary>
        [field: SerializeField]
        [field: Tooltip("Project-relative folder name of the package root under Assets.")]
        public string AssetRootFolder { get; set; } = nameof(SCG);

        /// <summary>Project folder asset that contains the package root.</summary>
        [field: Space(10)]
        [field: SerializeField]
        [field: Tooltip("Project folder asset that contains the package root.")]
        public UnityEngine.Object BaseFolder { get; set; }

        /// <summary>TextAsset reference to the effective package.json file.</summary>
        [field: SerializeField]
        [field: Tooltip("TextAsset reference to the effective package.json file.")]
        public TextAsset PackageAsset { get; set; }

        /// <summary>Enables root mirror synchronization for hidden Samples~/Documentation~ folders.</summary>
        [field: SerializeField]
        [field: Tooltip("Enables root mirror synchronization for hidden Samples~/Documentation~ folders.")]
        public bool IsRootFolderSyncEnabled { get; set; }

        [SerializeField]
        [HideInInspector]
        private string _lastSyncedPackageJsonPath;

        #endregion

        #region Singleton

        private static UppmSettings s_instance;

        /// <summary>
        /// Gets a settings instance.
        /// Loads from Resources first and falls back to an AssetDatabase search.
        /// If no asset exists, returns a temporary instance for editor-only use.
        /// </summary>
        public static UppmSettings Instance
        {
            get
            {
                var instance = s_instance;
                if (instance != null && EditorUtility.IsPersistent(instance))
                    return instance;

                instance = Resources.Load<UppmSettings>(nameof(UppmSettings));
                if (instance == null)
                    instance = TryFindAnyProjectAsset();

                if (instance == null)
                    instance = CreateInstance<UppmSettings>();

                s_instance = instance;
                return instance;
            }
        }

        /// <summary>
        /// Tries to load a persistent settings asset without creating a temporary fallback instance.
        /// This is used by menu and synchronization code that must not silently mutate a transient object.
        /// Returns false when no stored settings asset exists in the project.
        /// </summary>
        /// <param name="settings">Loaded persistent settings asset when available.</param>
        internal static bool TryGetPersistentInstance(out UppmSettings settings)
        {
            settings = Resources.Load<UppmSettings>(nameof(UppmSettings));
            if (settings == null)
                settings = TryFindAnyProjectAsset();

            return settings != null && EditorUtility.IsPersistent(settings);
        }

        /// <summary>
        /// Tries to locate any settings asset in the project via AssetDatabase.
        /// This is used as a fallback when the asset is not placed under Resources.
        /// Returns null when no settings asset exists in the project.
        /// </summary>
        /// <returns>Loaded settings asset or null.</returns>
        private static UppmSettings TryFindAnyProjectAsset()
        {
            var guid = AssetDatabase.FindAssets($"t:{nameof(UppmSettings)}").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(guid))
                return null;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.LoadAssetAtPath<UppmSettings>(path);
        }

        /// <summary>
        /// Updates the root mirror synchronization toggle and persists the settings asset when needed.
        /// The method returns false when the stored value already matches the requested one.
        /// Callers can use the result to skip redundant follow-up work.
        /// </summary>
        /// <param name="value">Requested synchronization state.</param>
        internal bool SetRootFolderSyncEnabled(bool value)
        {
            if (IsRootFolderSyncEnabled == value)
                return false;

            IsRootFolderSyncEnabled = value;

            if (!EditorUtility.IsPersistent(this))
                return true;

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            return true;
        }

        #endregion

        #region Unity

        /// <summary>
        /// Invoked when the asset instance is created in the editor domain.
        /// Schedules validation to run on the next editor tick.
        /// This avoids performing work during import-time serialization.
        /// </summary>
        private void Awake() => ScheduleValidate();

        /// <summary>
        /// Invoked by Unity when inspector values change.
        /// Schedules validation to run on the next editor tick.
        /// This avoids re-entrancy and import-time serialization issues.
        /// </summary>
        private void OnValidate() => ScheduleValidate();

        /// <summary>
        /// Invoked when the object is destroyed by the editor.
        /// Clears the singleton cache when it points to this instance.
        /// Prevents returning a dead reference from the Instance property.
        /// </summary>
        private void OnDestroy()
        {
            if (ReferenceEquals(s_instance, this))
                s_instance = null;
        }

        #endregion

        #region Validation

        /// <summary>
        /// Schedules validation to run on the next editor tick.
        /// Debounces repeated calls into a single delayed callback.
        /// Avoids running validation while Unity is still importing assets.
        /// </summary>
        private void ScheduleValidate()
        {
            EditorApplication.delayCall -= ValidateNow;
            EditorApplication.delayCall += ValidateNow;
        }

        /// <summary>
        /// Tries to run reference resolution and package.json synchronization immediately.
        /// This method is intended for workflows that move folders and cannot rely on delayCall timing.
        /// The call is skipped when Unity is updating or compiling to avoid import-time instability.
        /// </summary>
        /// <returns>True when synchronization ran, otherwise false.</returns>
        internal bool TrySyncImmediately()
        {
            EditorApplication.delayCall -= ValidateNow;

            if (!this)
                return false;

            if (IsEditorBusy())
                return false;

            ResolveReferences();

            if (PackageAsset == null)
                return true;

            var changed = SyncFromOrToPackageJson();
            if (!changed)
                return true;

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// Resolves asset references and synchronizes package.json fields.
        /// Reschedules itself when the editor is compiling or updating.
        /// Runs outside the OnValidate call stack to reduce instability.
        /// </summary>
        [ContextMenu(nameof(ValidateNow))]
        private void ValidateNow()
        {
            EditorApplication.delayCall -= ValidateNow;

            if (!this)
                return;

            ResolveReferences();

            if (IsEditorBusy())
            {
                EditorApplication.delayCall += ValidateNow;
                return;
            }

            if (PackageAsset == null)
                return;

            var changed = SyncFromOrToPackageJson();

            if (!changed) return;

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Resolves BaseFolder and PackageAsset based on current settings.
        /// Prefers explicit inspector references and falls back to conventional paths.
        /// Does not write to disk and only loads references via AssetDatabase.
        /// </summary>
        private void ResolveReferences()
        {
#if !UPM_PACKAGE
            if (BaseFolder != null && string.IsNullOrEmpty(AssetRootFolder))
                AssetRootFolder = GetAssetPathSafe(BaseFolder);

            var assetsRootPath = CombineUnityPath(AssetsRoot, AssetRootFolder);
            var defaultJsonPath = CombineUnityPath(assetsRootPath, PackageJsonFileName);

            var basePath = GetAssetPathSafe(BaseFolder);
            if (BaseFolder == null || string.IsNullOrWhiteSpace(basePath) || IsUnderPackages(basePath))
            {
                var loaded = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetsRootPath);
                if (loaded != null)
                    BaseFolder = loaded;
            }

            var packagePath = GetAssetPathSafe(PackageAsset);
            if (PackageAsset == null || string.IsNullOrWhiteSpace(packagePath) || IsUnderPackages(packagePath))
            {
                var loaded = AssetDatabase.LoadAssetAtPath<TextAsset>(defaultJsonPath);
                if (loaded != null)
                    PackageAsset = loaded;
            }

            if (BaseFolder == null && PackageAsset != null)
            {
                var jsonPath = GetAssetPathSafe(PackageAsset);
                var folderPath = string.IsNullOrWhiteSpace(jsonPath)
                    ? string.Empty
                    : Path.GetDirectoryName(jsonPath)?.Replace("\\", "/");

                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    var loaded = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
                    if (loaded != null)
                        BaseFolder = loaded;
                }
            }

            if (PackageAsset == null && BaseFolder != null)
            {
                var resolvedBasePath = GetAssetPathSafe(BaseFolder);
                if (!string.IsNullOrWhiteSpace(resolvedBasePath))
                {
                    var jsonPath = CombineUnityPath(resolvedBasePath, PackageJsonFileName);
                    var loaded = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
                    if (loaded != null)
                        PackageAsset = loaded;
                }
            }
#else
            if (string.IsNullOrWhiteSpace(PackageId))
                return;

            var packageRootPath = CombineUnityPath(PackagesRoot, PackageId);
            var defaultJsonPath = CombineUnityPath(packageRootPath, PackageJsonFileName);

            var basePath = GetAssetPathSafe(BaseFolder);
            if (BaseFolder == null || string.IsNullOrWhiteSpace(basePath) || IsUnderAssets(basePath))
            {
                var loaded = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(packageRootPath);
                if (loaded != null)
                    BaseFolder = loaded;
            }

            var packagePath = GetAssetPathSafe(PackageAsset);
            if (PackageAsset == null || string.IsNullOrWhiteSpace(packagePath) || IsUnderAssets(packagePath))
            {
                var loaded = AssetDatabase.LoadAssetAtPath<TextAsset>(defaultJsonPath);
                if (loaded != null)
                    PackageAsset = loaded;
            }

            if (BaseFolder == null && PackageAsset != null)
            {
                var jsonPath = GetAssetPathSafe(PackageAsset);
                var folderPath = string.IsNullOrWhiteSpace(jsonPath)
                    ? string.Empty
                    : Path.GetDirectoryName(jsonPath)?.Replace("\\", "/");

                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    var loaded = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
                    if (loaded != null)
                        BaseFolder = loaded;
                }
            }

            if (PackageAsset == null && BaseFolder != null)
            {
                var resolvedBasePath = GetAssetPathSafe(BaseFolder);
                if (!string.IsNullOrWhiteSpace(resolvedBasePath))
                {
                    var jsonPath = CombineUnityPath(resolvedBasePath, PackageJsonFileName);
                    var loaded = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
                    if (loaded != null)
                        PackageAsset = loaded;
                }
            }
#endif

#if !UPM_PACKAGE
            static bool IsUnderPackages(string path) =>
                path.StartsWith(PackagesRoot, StringComparison.Ordinal);
#else
            static bool IsUnderAssets(string path) =>
                path.StartsWith(AssetsRoot, StringComparison.Ordinal);
#endif
        }

        /// <summary>
        /// Synchronizes inspector values with the underlying package.json.
        /// Detects external edits by tracking the last observed file values per field.
        /// Prefers package.json when it changed outside the editor or when the resolved file path changed.
        /// </summary>
        /// <returns>True when this settings asset should be saved after synchronization.</returns>
        private bool SyncFromOrToPackageJson()
        {
            var packageJsonAssetPath = GetAssetPathSafe(PackageAsset);
            var pathChanged = !string.Equals(_lastSyncedPackageJsonPath, packageJsonAssetPath, StringComparison.Ordinal);

            var changed = pathChanged;

            changed |= SyncStringFieldBidirectional(
                inspectorValue: PackageVersion,
                read: () => PackageJsonUtility.GetPackageVersion(PackageAsset),
                write: v => PackageJsonUtility.SetPackageVersion(PackageAsset, v),
                assign: v => PackageVersion = v,
                lastSyncedValue: ref _lastSyncedPackageVersion,
                pathChanged: pathChanged);

            changed |= SyncStringFieldBidirectional(
                inspectorValue: PackageId,
                read: () => PackageJsonUtility.GetPackageName(PackageAsset),
                write: v => PackageJsonUtility.SetPackageName(PackageAsset, v),
                assign: v => PackageId = v,
                lastSyncedValue: ref _lastSyncedPackageId,
                pathChanged: pathChanged);

            changed |= SyncStringFieldBidirectional(
                inspectorValue: PackageDisplayName,
                read: () => PackageJsonUtility.GetPackageDisplayName(PackageAsset),
                write: v => PackageJsonUtility.SetPackageDisplayName(PackageAsset, v),
                assign: v => PackageDisplayName = v,
                lastSyncedValue: ref _lastSyncedPackageDisplayName,
                pathChanged: pathChanged);

            changed |= SyncStringFieldBidirectional(
                inspectorValue: PackageDescription,
                read: () => PackageJsonUtility.GetPackageDescription(PackageAsset),
                write: v => PackageJsonUtility.SetPackageDescription(PackageAsset, v),
                assign: v => PackageDescription = v,
                lastSyncedValue: ref _lastSyncedPackageDescription,
                pathChanged: pathChanged);

            if (!string.IsNullOrWhiteSpace(PackageVersion))
                PlayerSettings.bundleVersion = PackageVersion;

            _lastSyncedPackageJsonPath = packageJsonAssetPath;
            return changed;
        }

        #endregion

        #region Helpers
        /// <summary>
        /// Synchronizes a single string field using a bidirectional strategy.
        /// When package.json changes outside the editor, the file value is pulled into the inspector.
        /// When the inspector changes while the file stays unchanged, the inspector value is pushed into package.json.
        /// </summary>
        /// <param name="inspectorValue">Current value stored in the ScriptableObject.</param>
        /// <param name="read">Reads the value from package.json.</param>
        /// <param name="write">Writes the value to package.json.</param>
        /// <param name="assign">Assigns a value into the ScriptableObject.</param>
        /// <param name="lastSyncedValue">Last observed value read from or written to package.json.</param>
        /// <param name="pathChanged">True when the resolved package.json asset path changed since last sync.</param>
        /// <returns>True when this settings asset was mutated by the sync.</returns>
        private static bool SyncStringFieldBidirectional(
            string inspectorValue,
            Func<string> read,
            Action<string> write,
            Action<string> assign,
            ref string lastSyncedValue,
            bool pathChanged)
        {
            var fileValue = Normalize(read());
            var currentInspector = Normalize(inspectorValue);
            var last = Normalize(lastSyncedValue);

            if (string.IsNullOrEmpty(fileValue))
            {
                if (string.IsNullOrEmpty(currentInspector))
                    return SetLastSynced(ref lastSyncedValue, string.Empty);

                write(currentInspector);
                var written = Normalize(read());
                if (string.IsNullOrEmpty(written))
                    written = currentInspector;

                var didChange = SetLastSynced(ref lastSyncedValue, written);
                didChange |= AssignIfDifferent(currentInspector, written, assign);
                return didChange;
            }

            if (pathChanged || string.IsNullOrEmpty(last) || !string.Equals(fileValue, last, StringComparison.Ordinal))
            {
                var didChange = SetLastSynced(ref lastSyncedValue, fileValue);
                didChange |= AssignIfDifferent(currentInspector, fileValue, assign);
                return didChange;
            }

            if (string.IsNullOrEmpty(currentInspector))
                return AssignIfDifferent(currentInspector, fileValue, assign);

            if (string.Equals(currentInspector, fileValue, StringComparison.Ordinal))
                return false;

            write(currentInspector);
            var normalized = Normalize(read());
            if (string.IsNullOrEmpty(normalized))
                normalized = currentInspector;

            var changed = SetLastSynced(ref lastSyncedValue, normalized);
            changed |= AssignIfDifferent(currentInspector, normalized, assign);
            return changed;
        }

        /// <summary>
        /// Assigns a new value into the settings only when it differs from the current value.
        /// Uses ordinal comparison so synchronization behavior is deterministic across editor sessions.
        /// Returns true when an assignment was performed.
        /// </summary>
        /// <param name="current">Current normalized value.</param>
        /// <param name="next">Next normalized value to assign.</param>
        /// <param name="assign">Assignment callback that writes into the ScriptableObject.</param>
        private static bool AssignIfDifferent(string current, string next, Action<string> assign)
        {
            if (string.Equals(current, next, StringComparison.Ordinal))
                return false;

            assign(next);
            return true;
        }

        /// <summary>
        /// Updates the stored last-synced value when it differs from the current stored value.
        /// This value is persisted to detect external package.json edits across domain reloads.
        /// Returns true when the stored value was changed.
        /// </summary>
        /// <param name="lastSyncedValue">Stored last-synced value backing field.</param>
        /// <param name="next">Next normalized value to store.</param>
        private static bool SetLastSynced(ref string lastSyncedValue, string next)
        {
            if (string.Equals(lastSyncedValue, next, StringComparison.Ordinal))
                return false;

            lastSyncedValue = next;
            return true;
        }

        /// <summary>
        /// Normalizes an input value for sync comparisons by trimming whitespace.
        /// Converts null and whitespace-only values into an empty string for consistent checks.
        /// Leaves non-empty values unchanged beyond trimming.
        /// </summary>
        /// <param name="value">Input value.</param>
        /// <returns>Normalized value suitable for comparisons and persistence.</returns>
        private static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        /// <summary>
        /// Returns a normalized AssetDatabase path for the given object.
        /// Converts separators to forward slashes and returns an empty string for null.
        /// </summary>
        /// <param name="obj">Unity object to resolve an AssetDatabase path for.</param>
        /// <returns>Normalized asset path or an empty string.</returns>
        private static string GetAssetPathSafe(UnityEngine.Object obj) =>
            obj == null ? string.Empty : AssetDatabase.GetAssetPath(obj)?.Replace("\\", "/") ?? string.Empty;

        /// <summary>
        /// Builds a Unity project path using forward slashes.
        /// Avoids duplicated separators and keeps AssetDatabase-compatible formatting.
        /// Trims leading and trailing slashes in each segment.
        /// </summary>
        /// <param name="parts">Path parts to combine.</param>
        private static string CombineUnityPath(params string[] parts) =>
            parts == null || parts.Length == 0
                ? string.Empty
                : (from raw in parts
                   where !string.IsNullOrWhiteSpace(raw)
                   select raw.Replace("\\", "/").Trim('/')
                    into part
                   where !string.IsNullOrWhiteSpace(part)
                   select part).Aggregate(string.Empty,
                    (current, part) => string.IsNullOrWhiteSpace(current) ? part : current + "/" + part);

        /// <summary>
        /// Checks whether the editor is in a state where imports and serialization are unstable.
        /// Used to defer validation and assignments during compilation or asset update.
        /// Returning true means work should be rescheduled via delayCall.
        /// </summary>
        /// <returns>True when the editor is updating or compiling.</returns>
        private static bool IsEditorBusy() =>
            EditorApplication.isUpdating ||
            EditorApplication.isCompiling;

        #endregion
    }
}
