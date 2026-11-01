using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Oculus.Haptics;

namespace HapticsLab
{
    /// <summary>
    /// Hot-reload for .haptic files. Export from Haptics Studio straight into the watched
    /// folder while the scene is playing and the newest clip is ready within a second.
    ///
    ///   Editor (Quest Link):  &lt;project&gt;/Assets/HapticsLab/Haptics/Live
    ///   Quest build:          Android/data/&lt;package&gt;/files/Live   (copy files with adb or SideQuest)
    ///
    /// An audio file with the same name (.wav/.ogg/.mp3) next to the clip is loaded too,
    /// so you can feel AND hear your design together.
    ///
    /// Files are read directly (the same way Haptics Studio's companion app loads clips),
    /// so no asset import or domain reload is needed while playing.
    /// </summary>
    [AddComponentMenu("Haptics Lab/Live Clip Library")]
    public class LiveClipLibrary : MonoBehaviour
    {
        [Tooltip("Folder relative to the project root, used in the editor.")]
        public string editorFolder = "Assets/HapticsLab/Haptics/Live";
        [Min(0.1f)] public float pollInterval = 0.5f;

        [Tooltip("Always-available example clips (shown after the live ones).")]
        public HapticClip[] builtInClips;
        public AudioClip[] builtInAudio;

        public class Entry
        {
            public string name;
            public HapticClip clip;
            public AudioClip audio;
            public bool isLive;
            public string path;
            public DateTime stamp;
        }

        public readonly List<Entry> Entries = new List<Entry>();

        /// <summary>Raised when a live file is added or changed (argument: that entry).</summary>
        public event Action<Entry> ClipUpdated;
        public event Action ListChanged;

        public string FolderPath { get; private set; }

        readonly Dictionary<string, Entry> _live = new Dictionary<string, Entry>();

        void Awake()
        {
            FolderPath = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", editorFolder))
                : Path.Combine(Application.persistentDataPath, "Live");
            try { Directory.CreateDirectory(FolderPath); }
            catch (Exception e) { Debug.LogWarning("[HapticsLab] Cannot create live folder: " + e.Message); }

            for (int i = 0; i < (builtInClips?.Length ?? 0); i++)
            {
                var c = builtInClips[i];
                if (c == null) continue;
                Entries.Add(new Entry
                {
                    name = c.name,
                    clip = c,
                    audio = builtInAudio != null && i < builtInAudio.Length ? builtInAudio[i] : null,
                });
            }
        }

        void OnEnable() => StartCoroutine(PollLoop());

        IEnumerator PollLoop()
        {
            var wait = new WaitForSeconds(pollInterval);
            while (true)
            {
                yield return ScanOnce();
                yield return wait;
            }
        }

        IEnumerator ScanOnce()
        {
            string[] files;
            try { files = Directory.Exists(FolderPath) ? Directory.GetFiles(FolderPath, "*.haptic") : Array.Empty<string>(); }
            catch (Exception) { yield break; }

            bool listChanged = false;
            var seen = new HashSet<string>();

            foreach (var path in files)
            {
                seen.Add(path);
                DateTime stamp;
                try { stamp = File.GetLastWriteTimeUtc(path); }
                catch (Exception) { continue; }

                if (_live.TryGetValue(path, out var existing) && existing.stamp == stamp)
                    continue;

                string json;
                try { json = File.ReadAllText(path); }
                catch (IOException) { continue; } // still being written - try next poll

                if (HapticEnvelope.Parse(json) == null)
                {
                    Debug.LogWarning($"[HapticsLab] '{Path.GetFileName(path)}' is not a valid .haptic file (yet).");
                    continue;
                }

                var clip = ScriptableObject.CreateInstance<HapticClip>();
                clip.json = json;
                clip.name = Path.GetFileNameWithoutExtension(path);

                var entry = existing ?? new Entry { isLive = true, path = path };
                var oldClip = entry.clip;
                entry.name = clip.name;
                entry.clip = clip;
                entry.stamp = stamp;

                if (existing == null)
                {
                    _live[path] = entry;
                    Entries.Insert(0, entry);
                    listChanged = true;
                }

                yield return LoadAudio(entry);
                Debug.Log($"[HapticsLab] Live clip loaded: {entry.name}" + (entry.audio ? " (+ audio)" : ""));
                ClipUpdated?.Invoke(entry);

                // Each re-export creates a new clip object; free the one it replaced.
                if (oldClip != null) Destroy(oldClip);
            }

            // Remove deleted files
            var removed = new List<string>();
            foreach (var kv in _live)
                if (!seen.Contains(kv.Key))
                    removed.Add(kv.Key);
            foreach (var path in removed)
            {
                Entries.Remove(_live[path]);
                _live.Remove(path);
                listChanged = true;
            }

            if (listChanged) ListChanged?.Invoke();
        }

        IEnumerator LoadAudio(Entry entry)
        {
            entry.audio = null;
            string basePath = Path.Combine(Path.GetDirectoryName(entry.path) ?? "", entry.name);
            (string ext, AudioType type)[] options = { (".wav", AudioType.WAV), (".ogg", AudioType.OGGVORBIS), (".mp3", AudioType.MPEG) };
            foreach (var (ext, type) in options)
            {
                string p = basePath + ext;
                if (!File.Exists(p)) continue;
                using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(p).AbsoluteUri, type))
                {
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        entry.audio = DownloadHandlerAudioClip.GetContent(req);
                        if (entry.audio) entry.audio.name = entry.name;
                    }
                }
                yield break;
            }
        }
    }
}
