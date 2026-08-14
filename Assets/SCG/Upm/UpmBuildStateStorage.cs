using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace SCG.UPPM.Upm
{
    /// <summary>
    /// Handles persistence of <see cref="UpmBuildState"/> to a JSON file under the Temp folder.
    /// The state is used to resume staged editor operations across domain reloads.
    /// The file format is intentionally stable and does not depend on external serializers.
    /// </summary>
    internal static class UpmBuildStateStorage
    {
        /// <summary>
        /// Loads the current state from the Temp state file when it exists.
        /// Returns a new instance when the file is missing or cannot be parsed.
        /// The method is safe to call repeatedly and does not allocate when no state is present.
        /// </summary>
        public static UpmBuildState LoadOrCreate() => LoadOrCreate(GetStateFileAbs(), Debug.LogException);

        /// <summary>
        /// Loads state from an injected path and removes invalid data so it cannot restart a failing workflow.
        /// </summary>
        /// <param name="path">State file path.</param>
        /// <param name="logException">Optional parse error logger.</param>
        /// <returns>Persisted valid state or a new empty state.</returns>
        internal static UpmBuildState LoadOrCreate(string path, Action<Exception> logException)
        {
            if (!File.Exists(path))
                return new UpmBuildState();

            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var st = JsonUtility.FromJson<UpmBuildState>(json);
                if (st == null || (st.Stage != default && !Enum.IsDefined(typeof(UpmStage), st.Stage)))
                    throw new InvalidDataException("UPPM build state is invalid.");

                return st;
            }
            catch (Exception exception)
            {
                Clear(path);
                logException?.Invoke(exception);
                return new UpmBuildState();
            }
        }

        /// <summary>
        /// Saves the provided state into the Temp state file.
        /// The method ensures the Temp directory exists before writing.
        /// Callers can rely on this operation being idempotent and fast for small payloads.
        /// </summary>
        /// <param name="st">State object to persist.</param>
        public static void Save(UpmBuildState st)
        {
            if (st == null)
                throw new ArgumentNullException(nameof(st));

            var dir = GetTempFolderAbs();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var path = GetStateFileAbs();
            var json = JsonUtility.ToJson(st, true);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        /// <summary>
        /// Deletes the persisted state file, if present.
        /// This helper affects only bookkeeping and does not remove any assets or package folders.
        /// It should be called after a successful return flow.
        /// </summary>
        public static void Clear() => Clear(GetStateFileAbs());

        private static void Clear(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static string GetStateFileAbs() =>
            Path.Combine(GetTempFolderAbs(), UpmConstants.StateFileName);

        private static string GetTempFolderAbs() =>
            Path.Combine(UpmPathUtility.ProjectRootAbs, UpmConstants.TempFolderName);
    }
}
