using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using MultiClicker.Models;
using Newtonsoft.Json;

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
                    if (File.Exists(ConfigFilePath))
                    {
                        LoadFromFile();
                    }
                    else
                    {
                        CreateDefaultConfig();
                    }

                    ValidateAndFixConfig();
                    SaveConfig();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Error loading configuration: {ex.Message}");
                    CreateDefaultConfig();
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// Saves current configuration to file
        /// </summary>
        public static void SaveConfig()
        {
            lock (ConfigLock)
            {
                try
                {
                    var configJson = JsonConvert.SerializeObject(_config, Formatting.Indented);
                    File.WriteAllText(ConfigFilePath, configJson);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Error saving configuration: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Updates a specific keybind
        /// </summary>
        public static void UpdateKeybind(TRIGGERS trigger, Keys key)
        {
            if (_config?.Keybinds != null)
            {
                _config.Keybinds[trigger] = key;
                SaveConfig();
            }
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
            _config = JsonConvert.DeserializeObject<Config>(configContent);
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
                    FollowNoDelay = 20
                },
                Panels = new Dictionary<string, PanelConfig>(),
                Positions = new Dictionary<TRIGGERS_POSITIONS, Position>(),
                Keybinds = GetDefaultKeybinds()
            };
        }

        /// <summary>
        /// Gets default keybind configuration
        /// </summary>
        private static Dictionary<TRIGGERS, Keys> GetDefaultKeybinds()
        {
            return new Dictionary<TRIGGERS, Keys>
            {
                { TRIGGERS.SELECT_NEXT, Keys.F1 },
                { TRIGGERS.SELECT_PREVIOUS, Keys.F2 },
                { TRIGGERS.SIMPLE_CLICK, Keys.XButton1 },
                { TRIGGERS.DOUBLE_CLICK, Keys.XButton2 },
                { TRIGGERS.SIMPLE_CLICK_NO_DELAY, Keys.MButton },
                { TRIGGERS.DOFUS_HAVENBAG, Keys.H },
                { TRIGGERS.DOFUS_OPEN_DISCUSSION, Keys.Tab },
                { TRIGGERS.TRAVEL, Keys.F6 },
                { TRIGGERS.OPTIONS, Keys.F12 }
            };
        }

        /// <summary>
        /// Validates and fixes any missing configuration entries
        /// </summary>
        private static void ValidateAndFixConfig()
        {
            // Ensure keybinds exist
            if (_config.Keybinds == null || _config.Keybinds.Count == 0)
            {
                _config.Keybinds = GetDefaultKeybinds();
            }

            // Ensure positions exist
            if (_config.Positions == null)
            {
                _config.Positions = GetDefaultPositions();
            }
            else
            {
                ValidatePositions();
            }

            // Ensure general config exists
            if (_config.General == null)
            {
                _config.General = new GeneralConfig
                {
                    GameVersion = "3.0.45.37",
                    MinimumFollowDelay = 200,
                    MaximumFollowDelay = 400,
                    FollowNoDelay = 20
                };
            }

            // Ensure panels dictionary exists
            if (_config.Panels == null)
            {
                _config.Panels = new Dictionary<string, PanelConfig>();
            }
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
        /// Validates and adds missing position entries
        /// </summary>
        private static void ValidatePositions()
        {
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
                }
            }
        }
        #endregion
    }
}
