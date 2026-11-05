using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace NeonStrikers.Arenas
{
    /// <summary>One saved arena file as listed by <see cref="ArenaStorage.ListArenas"/>.</summary>
    public sealed class ArenaFileEntry
    {
        public string Id;
        public string Name;
        public string FilePath;
        public DateTime ModifiedUtc;
        public int ObjectCount;

        /// <summary>The file can't be read at all (corrupted JSON, wrong format, newer version...).</summary>
        public bool IsBroken;
        public string Error;

        /// <summary>Loaded arena (null when broken).</summary>
        public ArenaData Data;
    }

    [Serializable]
    public class GameFlowSettings
    {
        public int formatVersion = 1;
        public string fourthArenaId = "";
    }

    /// <summary>
    /// Stores custom arenas as human readable JSON files in
    /// <c>Application.persistentDataPath/CustomArenas/&lt;id&gt;.arena.json</c>, and the arena chosen as the
    /// 4th arena of the game flow in <c>gameflow.json</c> next to them. Files survive restarts; replacing
    /// the 4th arena never deletes any arena file.
    /// Writes go through a temporary file so a crash can't leave a half written arena behind.
    /// </summary>
    public sealed class ArenaStorage
    {
        public const string FolderName = "CustomArenas";
        public const string ArenaFileSuffix = ".arena.json";
        public const string GameFlowFileName = "gameflow.json";

        static ArenaStorage defaultStorage;
        static string overrideRoot;

        public string RootDirectory { get; }

        public ArenaStorage(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory))
                throw new ArgumentException("A storage directory is required", nameof(rootDirectory));

            RootDirectory = rootDirectory;
        }

        /// <summary>The storage used by the game (persistent data path, or the test override).</summary>
        public static ArenaStorage Default
        {
            get
            {
                if (defaultStorage == null)
                {
                    string root = !string.IsNullOrEmpty(overrideRoot)
                        ? overrideRoot
                        : Path.Combine(Application.persistentDataPath, FolderName);
                    defaultStorage = new ArenaStorage(root);
                }

                return defaultStorage;
            }
        }

        /// <summary>Tests: redirect <see cref="Default"/> to another folder (null restores the real one).</summary>
        public static void OverrideDefaultRoot(string root)
        {
            overrideRoot = root;
            defaultStorage = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLaunch()
        {
            defaultStorage = null;
            overrideRoot = null;
        }

        string GameFlowPath => Path.Combine(RootDirectory, GameFlowFileName);

        public string PathFor(string id)
        {
            return Path.Combine(RootDirectory, id + ArenaFileSuffix);
        }

        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64)
                return false;

            foreach (char c in id)
            {
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                    return false;
            }

            return true;
        }

        void EnsureDirectory()
        {
            if (!Directory.Exists(RootDirectory))
                Directory.CreateDirectory(RootDirectory);
        }

        // ------------------------------------------------------------------ serialization

        public static string ToJson(ArenaData data)
        {
            return JsonUtility.ToJson(data, true);
        }

        /// <summary>Parses arena JSON. Returns false with a human readable reason for broken content.</summary>
        public static bool TryParse(string json, out ArenaData data, out string error)
        {
            data = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The file is empty.";
                return false;
            }

            ArenaData parsed;
            try
            {
                parsed = JsonUtility.FromJson<ArenaData>(json);
            }
            catch (Exception e)
            {
                error = "The file is not valid arena data (" + e.Message + ").";
                return false;
            }

            if (parsed == null)
            {
                error = "The file is not valid arena data.";
                return false;
            }

            if (parsed.formatVersion <= 0 || !json.Contains("\"objects\""))
            {
                error = "The file is not a Neon Strikers arena.";
                return false;
            }

            if (parsed.formatVersion > ArenaData.CurrentFormatVersion)
            {
                error = "The arena was made with a newer version of the game (format " + parsed.formatVersion + ").";
                return false;
            }

            if (parsed.objects == null)
                parsed.objects = new List<ArenaObjectData>();
            parsed.objects.RemoveAll(o => o == null);

            var ids = new HashSet<int>();
            foreach (ArenaObjectData obj in parsed.objects)
            {
                if (obj.id <= 0 || !ids.Add(obj.id))
                {
                    error = "The file is damaged: object id " + obj.id + " is used more than once.";
                    return false;
                }
            }

            if (parsed.name == null)
                parsed.name = ArenaData.DefaultName;
            if (parsed.name.Length > ArenaData.MaxNameLength)
                parsed.name = parsed.name.Substring(0, ArenaData.MaxNameLength);

            int highest = parsed.objects.Count == 0 ? 0 : parsed.objects.Max(o => o.id);
            if (parsed.nextObjectId <= highest)
                parsed.nextObjectId = highest + 1;

            data = parsed;
            return true;
        }

        // ------------------------------------------------------------------ arenas

        /// <summary>
        /// Writes the arena. A missing id is generated (and written back into <paramref name="data"/>).
        /// Returns the stored copy.
        /// </summary>
        public ArenaData Save(ArenaData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            EnsureDirectory();

            if (!IsValidId(data.id))
                data.id = Guid.NewGuid().ToString("N");

            string now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(data.createdUtc))
                data.createdUtc = now;
            data.modifiedUtc = now;
            data.formatVersion = ArenaData.CurrentFormatVersion;
            if (string.IsNullOrWhiteSpace(data.name))
                data.name = ArenaData.DefaultName;
            data.name = data.name.Trim();
            if (data.name.Length > ArenaData.MaxNameLength)
                data.name = data.name.Substring(0, ArenaData.MaxNameLength).Trim();

            ArenaData stored = data.Clone();
            WriteAtomic(PathFor(stored.id), ToJson(stored));
            return stored;
        }

        public bool Exists(string id)
        {
            return IsValidId(id) && File.Exists(PathFor(id));
        }

        public bool TryLoad(string id, out ArenaData data, out string error)
        {
            data = null;

            if (!IsValidId(id))
            {
                error = "Invalid arena id.";
                return false;
            }

            string path = PathFor(id);
            if (!File.Exists(path))
            {
                error = "The arena file no longer exists.";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                error = "The arena file can't be read (" + e.Message + ").";
                return false;
            }

            if (!TryParse(json, out data, out error))
                return false;

            // The file name is the identity of the arena.
            data.id = id;
            return true;
        }

        /// <summary>All arena files, newest first. Unreadable files are returned too, flagged as broken.</summary>
        public List<ArenaFileEntry> ListArenas()
        {
            var entries = new List<ArenaFileEntry>();
            if (!Directory.Exists(RootDirectory))
                return entries;

            foreach (string path in Directory.GetFiles(RootDirectory, "*" + ArenaFileSuffix))
            {
                string fileName = Path.GetFileName(path);
                string id = fileName.Substring(0, fileName.Length - ArenaFileSuffix.Length);
                var entry = new ArenaFileEntry
                {
                    Id = id,
                    Name = id,
                    FilePath = path,
                    ModifiedUtc = SafeLastWrite(path)
                };

                if (!IsValidId(id))
                {
                    entry.IsBroken = true;
                    entry.Error = "The file name is not a valid arena id.";
                }
                else if (TryLoad(id, out ArenaData data, out string error))
                {
                    entry.Data = data;
                    entry.Name = data.DisplayName;
                    entry.ObjectCount = data.objects.Count;
                    if (DateTime.TryParse(data.modifiedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime modified))
                        entry.ModifiedUtc = modified.ToUniversalTime();
                }
                else
                {
                    entry.IsBroken = true;
                    entry.Error = error;
                }

                entries.Add(entry);
            }

            return entries.OrderByDescending(e => e.ModifiedUtc).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static DateTime SafeLastWrite(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>Deletes an arena file. If it was the 4th arena, the game flow goes back to the three stock arenas.</summary>
        public bool Delete(string id)
        {
            if (!IsValidId(id))
                return false;

            string path = PathFor(id);
            bool existed = File.Exists(path);
            if (existed)
                File.Delete(path);

            if (GetGameFlowArenaId() == id)
                ClearGameFlowArena();

            return existed;
        }

        // ------------------------------------------------------------------ game flow (4th arena)

        /// <summary>Id of the arena used as the 4th arena, or "" for the original three-arena flow.</summary>
        public string GetGameFlowArenaId()
        {
            string path = GameFlowPath;
            if (!File.Exists(path))
                return "";

            try
            {
                GameFlowSettings settings = JsonUtility.FromJson<GameFlowSettings>(File.ReadAllText(path));
                if (settings == null || !IsValidId(settings.fourthArenaId))
                    return "";
                return settings.fourthArenaId;
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>Makes the arena the 4th arena (replacing any previous one, which stays saved).</summary>
        public void SetGameFlowArena(string id)
        {
            if (!IsValidId(id))
                throw new ArgumentException("Invalid arena id", nameof(id));

            EnsureDirectory();
            WriteAtomic(GameFlowPath, JsonUtility.ToJson(new GameFlowSettings { fourthArenaId = id }, true));
        }

        /// <summary>Back to the original Arena_01 -> Arena_02 -> Arena_03 flow. No arena is deleted.</summary>
        public void ClearGameFlowArena()
        {
            string path = GameFlowPath;
            if (File.Exists(path))
                File.Delete(path);
        }

        static void WriteAtomic(string path, string contents)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, contents);

            if (File.Exists(path))
                File.Delete(path);

            File.Move(temp, path);
        }
    }
}
