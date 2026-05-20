using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MultiClicker.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace MultiClicker.Services
{
    /// <summary>
    /// Service responsible for managing application configuration
    /// </summary>
    public static class ConfigurationService
    {
        #region Private Fields
        private static Config _config;
        private static readonly string ConfigFilePath = "config.json";
        private static readonly object ConfigLock = new object();
        private static System.Threading.Timer _saveTimer;
        private static readonly TimeSpan SaveDebounceDelay = TimeSpan.FromMilliseconds(500);
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() }
        };
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the current configuration instance
        /// </summary>
        public static Config Current
        {
            get
            {
                if (_config == null)
                {
                    LoadConfig();
                }
                return _config;
            }
        }

        /// <summary>
        /// Indicates if key binds are currently being modified
        /// </summary>
        public static bool IsModifyingKeyBinds { get; set; } = false;

        /// <summary>
        /// Raised whenever the keybind dictionary contents change so consumers
        /// (such as the hook service) can rebuild their lookup indices.
        /// </summary>
        public static event Action KeybindsChanged;

        internal static void RaiseKeybindsChanged()
        {
            try { KeybindsChanged?.Invoke(); }
            catch (Exception ex) { Trace.WriteLine($"Error raising KeybindsChanged: {ex.Message}"); }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Loads configuration from file or creates default configuration
        /// </summary>
        public static void LoadConfig()
        {
            lock (ConfigLock)
            {
                try
                {
                    bool createdDefault = false;
                    if (File.Exists(ConfigFilePath))
                    {
                        LoadFromFile();
                        if (_config == null)
                        {
                            CreateDefaultConfig();
                            createdDefault = true;
                        }
                    }
                    else
                    {
                        CreateDefaultConfig();
                        createdDefault = true;
                    }

                    var changed = ValidateAndFixConfig();
                    if (createdDefault || changed)
                    {
                        SaveConfigImmediateInternal();
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Error loading configuration: {ex.Message}");
                    CreateDefaultConfig();
                    SaveConfigImmediateInternal();
                }
            }
        }

        /// <summary>
        /// Schedules a debounced save to file (coalesces rapid edits).
        /// </summary>
        public static void SaveConfig()
        {
            lock (ConfigLock)
            {
                if (_saveTimer == null)
                {
                    _saveTimer = new System.Threading.Timer(_ => SaveConfigImmediate(), null,
                        SaveDebounceDelay, System.Threading.Timeout.InfiniteTimeSpan);
                }
                else
                {
                    _saveTimer.Change(SaveDebounceDelay, System.Threading.Timeout.InfiniteTimeSpan);
                }
            }
        }

        /// <summary>
        /// Saves configuration to disk immediately, flushing any pending debounced save.
        /// </summary>
        public static void SaveConfigImmediate()
        {
            lock (ConfigLock)
            {
                _saveTimer?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
                SaveConfigImmediateInternal();
            }
        }

        private static void SaveConfigImmediateInternal()
        {
            try
            {
                if (_config == null) return;
                var configJson = JsonConvert.SerializeObject(_config, JsonSettings);
                var tmp = ConfigFilePath + ".tmp";
                File.WriteAllText(tmp, configJson);
                if (File.Exists(ConfigFilePath))
                {
                    File.Replace(tmp, ConfigFilePath, ConfigFilePath + ".bak");
                }
                else
                {
                    File.Move(tmp, ConfigFilePath);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates a specific keybind
        /// </summary>
        public static void UpdateKeybind(TRIGGERS trigger, KeyCombination keyCombination)
        {
            if (_config?.Keybinds != null)
            {
                _config.Keybinds[trigger] = keyCombination;
                SaveConfig();
                RaiseKeybindsChanged();
            }
        }

        /// <summary>
        /// Updates a specific keybind using Keys (for backward compatibility)
        /// </summary>
        public static void UpdateKeybind(TRIGGERS trigger, Keys key)
        {
            UpdateKeybind(trigger, KeyCombination.FromKeys(key));
        }

        /// <summary>
        /// Updates a panel configuration
        /// </summary>
        public static void UpdatePanelConfig(string panelName, PanelConfig config)
        {
            if (_config?.Panels != null)
            {
                _config.Panels[panelName] = config;
                SaveConfig();
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Loads configuration from existing file
        /// </summary>
        private static void LoadFromFile()
        {
            var configContent = File.ReadAllText(ConfigFilePath);
            
            try
            {
                _config = JsonConvert.DeserializeObject<Config>(configContent, JsonSettings);
                Trace.WriteLine("Configuration loaded successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error deserializing config, creating new one: {ex.Message}");
                CreateDefaultConfig();
            }
        }

        /// <summary>
        /// Creates default configuration
        /// </summary>
        private static void CreateDefaultConfig()
        {
            _config = new Config
            {
                General = new GeneralConfig
                {
                    GameVersion = "3.0.45.37",
                    MinimumFollowDelay = 200,
                    MaximumFollowDelay = 400,
                    DisplayMode = PanelDisplayMode.AUTOMATIC,
                    SelectedCharacters = new List<string>()
                },
                Panels = new Dictionary<string, PanelConfig>(),
                Positions = new Dictionary<TRIGGERS_POSITIONS, Position>(),
                Keybinds = GetDefaultKeybinds()
            };
        }

        /// <summary>
        /// Gets default keybind configuration
        /// </summary>
        private static Dictionary<TRIGGERS, KeyCombination> GetDefaultKeybinds()
        {
            return new Dictionary<TRIGGERS, KeyCombination>
            {
                { TRIGGERS.SELECT_NEXT, new KeyCombination(Keys.F1) },
                { TRIGGERS.SELECT_PREVIOUS, new KeyCombination(Keys.F2) },
                { TRIGGERS.SIMPLE_CLICK, new KeyCombination(Keys.XButton1) },
                { TRIGGERS.DOUBLE_CLICK, new KeyCombination(Keys.XButton2) },
                { TRIGGERS.DOFUS_OPEN_DISCUSSION, new KeyCombination(Keys.Tab) },
                { TRIGGERS.GROUP_CHARACTERS, new KeyCombination(Keys.F5) },
                { TRIGGERS.OPTIONS, new KeyCombination(Keys.F12) },
                { TRIGGERS.FILL_HDV, new KeyCombination(Keys.Oem7, false, false, false, true, false, false, false, false) },
                { TRIGGERS.PASTE_ON_ALL_WINDOWS, new KeyCombination(Keys.V, true, false, true, false, false, false, false, false) }
            };
        }

        /// <summary>
        /// Validates and fixes any missing configuration entries. Returns true if any change was applied.
        /// </summary>
        private static bool ValidateAndFixConfig()
        {
            bool changed = false;

            // Ensure keybinds exist
            if (_config.Keybinds == null || _config.Keybinds.Count == 0)
            {
                _config.Keybinds = GetDefaultKeybinds();
                changed = true;
            }
            else
            {
                changed |= ValidateKeybinds();
            }

            // Ensure positions exist
            if (_config.Positions == null)
            {
                _config.Positions = GetDefaultPositions();
                changed = true;
            }
            else
            {
                changed |= ValidatePositions();
            }

            // Ensure general config exists
            if (_config.General == null)
            {
                _config.General = new GeneralConfig
                {
                    GameVersion = "3.0.45.37",
                    MinimumFollowDelay = 200,
                    MaximumFollowDelay = 400
                };
                changed = true;
            }

            // Ensure panels dictionary exists
            if (_config.Panels == null)
            {
                _config.Panels = new Dictionary<string, PanelConfig>();
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Validates and adds missing keybind entries. Returns true if any change was applied.
        /// </summary>
        private static bool ValidateKeybinds()
        {
            bool changed = false;
            var defaultKeybinds = GetDefaultKeybinds();

            // Remove keybinds for triggers that no longer exist in the current enum
            var validTriggers = new HashSet<TRIGGERS>((TRIGGERS[])Enum.GetValues(typeof(TRIGGERS)));
            var toRemove = _config.Keybinds.Keys.Where(k => !validTriggers.Contains(k)).ToList();
            foreach (var k in toRemove)
            {
                _config.Keybinds.Remove(k);
                changed = true;
            }

            foreach (var defaultKeybind in defaultKeybinds)
            {
                if (!_config.Keybinds.ContainsKey(defaultKeybind.Key))
                {
                    _config.Keybinds.Add(defaultKeybind.Key, defaultKeybind.Value);
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// Gets default position configuration
        /// </summary>
        private static Dictionary<TRIGGERS_POSITIONS, Position> GetDefaultPositions()
        {
            return new Dictionary<TRIGGERS_POSITIONS, Position>
            {
                { TRIGGERS_POSITIONS.SELL_CURRENT_MODE, new Position { X = 0, Y = 0, Width = 0, Height = 0 } },
                { TRIGGERS_POSITIONS.SELL_LOT_1, new Position { X = 0, Y = 0, Width = 0, Height = 0 } },
                { TRIGGERS_POSITIONS.SELL_LOT_10, new Position { X = 0, Y = 0, Width = 0, Height = 0 } },
                { TRIGGERS_POSITIONS.SELL_LOT_100, new Position { X = 0, Y = 0, Width = 0, Height = 0 } },
                { TRIGGERS_POSITIONS.SELL_LOT_1000, new Position { X = 0, Y = 0, Width = 0, Height = 0 } }
            };
        }

        /// <summary>
        /// Validates and adds missing position entries. Returns true if any change was applied.
        /// </summary>
        private static bool ValidatePositions()
        {
            bool changed = false;
            var requiredPositions = new[]
            {
                TRIGGERS_POSITIONS.SELL_CURRENT_MODE,
                TRIGGERS_POSITIONS.SELL_LOT_1,
                TRIGGERS_POSITIONS.SELL_LOT_10,
                TRIGGERS_POSITIONS.SELL_LOT_100,
                TRIGGERS_POSITIONS.SELL_LOT_1000
            };

            foreach (var position in requiredPositions)
            {
                if (!_config.Positions.ContainsKey(position))
                {
                    _config.Positions.Add(position, new Position { X = 0, Y = 0, Width = 0, Height = 0 });
                    changed = true;
                }
            }

            return changed;
        }
        #endregion
    }
}
