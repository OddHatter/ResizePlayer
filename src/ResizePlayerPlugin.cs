using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Oddhatter.ResizePlayer
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class ResizePlayerPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.oddhatter.resizeplayer";
        public const string Name = "ResizePlayer";
        public const string Version = "1.0.1";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;

        /// <summary>One size setting per Part (1 = vanilla). Read every frame, so edits apply live.</summary>
        internal static readonly Dictionary<Part, ConfigEntry<float>> Sizes = new Dictionary<Part, ConfigEntry<float>>();

        private static readonly Dictionary<Part, string> SizeDescriptions = new Dictionary<Part, string>
        {
            [Part.Overall] = "Whole body, including hitbox, collision, equipment, movement speed, jumps, reach, swim depth and camera.",
            [Part.Head] = "Head (with hair, beard and helmet).",
            [Part.Neck] = "Neck.",
            [Part.Chest] = "Chest and shoulders area.",
            [Part.Belly] = "Belly and lower back.",
            [Part.Hips] = "Pelvis and butt. Your legs are kept on the ground.",
            [Part.Arms] = "Upper and lower arms.",
            [Part.Hands] = "Hands.",
            [Part.Legs] = "Upper and lower legs. Your body is raised or lowered to keep your feet on the ground, and longer legs move you faster and jump higher (both = Overall x Legs).",
            [Part.Feet] = "Feet. You stand on their soles.",
        };

        /// <summary>Wait this long after the last file event before reloading (editors write in several steps).</summary>
        private const float ConfigReloadDelay = 0.5f;

        private Harmony _harmony;
        private FileSystemWatcher _configWatcher;
        private volatile bool _configFileChanged; // set on a thread-pool thread by the watcher
        private float _configReloadAt = -1f;
        private bool _configLockedLogged;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "true: your character uses the sizes below, and you see other players' sizes (if they have the mod). " +
                "false: everyone looks vanilla, and other players see you at normal size.");

            foreach (Part part in BodyParts.All)
            {
                Sizes[part] = Config.Bind("Size", part.ToString(), 1f,
                    new ConfigDescription(SizeDescriptions[part] + " 1 = normal size.",
                        new AcceptableValueRange<float>(BodyParts.Min, BodyParts.Max)));
            }

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(ResizePlayerPlugin).Assembly);
            foreach (System.Reflection.MethodBase method in _harmony.GetPatchedMethods())
            {
                Log.LogInfo($"Patched {method.DeclaringType?.Name}.{method.Name}");
            }
            WatchConfigFile();
            Log.LogInfo($"{Name} {Version} loaded.");
        }

        private void Update()
        {
            if (_configFileChanged)
            {
                _configFileChanged = false;
                _configReloadAt = Time.realtimeSinceStartup + ConfigReloadDelay;
            }
            if (_configReloadAt >= 0f && Time.realtimeSinceStartup >= _configReloadAt)
            {
                _configReloadAt = -1f;
                ReloadConfigFromDisk();
            }
        }

        private void OnDestroy()
        {
            _configWatcher?.Dispose();
            _harmony?.UnpatchSelf();
        }

        /// <summary>
        /// Picks up edits to the .cfg file made while the game runs (text editor, r2modman config editor).
        /// All settings are read every frame, so a reload applies immediately.
        /// </summary>
        private void WatchConfigFile()
        {
            string path = Config.ConfigFilePath;
            _configWatcher = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            _configWatcher.Changed += OnConfigFileEvent;
            _configWatcher.Created += OnConfigFileEvent;
            _configWatcher.Renamed += OnConfigFileEvent;
            _configWatcher.EnableRaisingEvents = true;
        }

        private void OnConfigFileEvent(object sender, FileSystemEventArgs e)
        {
            _configFileChanged = true; // handled on the main thread in Update
        }

        private void ReloadConfigFromDisk()
        {
            if (!File.Exists(Config.ConfigFilePath))
            {
                return;
            }
            // ConfigFile.Reload sets each entry; with SaveOnConfigSet every changed value would rewrite the file
            // and re-trigger this watcher. The file already holds these values, so don't save while reloading.
            bool saveOnSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;
            try
            {
                Config.Reload();
                _configLockedLogged = false;
                Log.LogInfo("Configuration reloaded from disk.");
            }
            catch (IOException ex)
            {
                // File still being written or locked by an editor: keep retrying, but only log once.
                if (!_configLockedLogged)
                {
                    _configLockedLogged = true;
                    Log.LogWarning($"Config file busy, will retry: {ex.Message}");
                }
                _configReloadAt = Time.realtimeSinceStartup + ConfigReloadDelay;
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to reload configuration: {ex.Message}");
            }
            finally
            {
                Config.SaveOnConfigSet = saveOnSet;
            }
        }
    }
}
