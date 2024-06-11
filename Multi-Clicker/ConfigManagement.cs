using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace MultiClicker
{
    public class PanelConfig
    {
        public string Background { get; set; }
    }
    public class Position
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public enum TRIGGERS
    {
        SELECT_NEXT,
        SELECT_PREVIOUS,
        HAVENBAG,
        TRAVEL,
        OPTIONS,
        SIMPLE_CLICK,
        DOUBLE_CLICK,
        SIMPLE_CLICK_NO_DELAY,
        GROUP_INVITE,
        DOFUS_HAVENBAG,
        DOFUS_OPEN_DISCUSSION,
    }
    public enum TRIGGERS_POSITIONS
    {
        FIGHT_ANALISYS,
        SELL_CURRENT_MODE,
        SELL_LOT_1,
        SELL_LOT_10,
        SELL_LOT_100,
    }

    public class Config
    {
        public Dictionary<string, PanelConfig> Panels { get; set; }
        public Dictionary<TRIGGERS_POSITIONS, Position> Positions { get; set; }
        public Dictionary<TRIGGERS, Keys> Keybinds { get; set; }
    }
    public static class ConfigManagement
    {
        public static Boolean IS_MODIFYING_KEY_BINDS = false;
        public static Config config;

        public static void LoadConfig()
        {
            string configFilePath = "config.json";
            if (File.Exists(configFilePath))
            {
                string configFileContent = File.ReadAllText(configFilePath);
                config = JsonConvert.DeserializeObject<Config>(configFileContent);
                if (config.Keybinds == null || config.Keybinds.Count == 0)
                {
                    config.Keybinds = new Dictionary<TRIGGERS, Keys>{
                        { TRIGGERS.SELECT_NEXT, Keys.F1},
                        { TRIGGERS.SELECT_PREVIOUS, Keys.F2},
                        { TRIGGERS.HAVENBAG, Keys.F3},
                        { TRIGGERS.SIMPLE_CLICK, Keys.XButton1},
                        { TRIGGERS.DOUBLE_CLICK, Keys.XButton2},
                        { TRIGGERS.SIMPLE_CLICK_NO_DELAY, Keys.MButton},
                        { TRIGGERS.GROUP_INVITE, Keys.F5},
                        { TRIGGERS.DOFUS_HAVENBAG, Keys.H},
                        { TRIGGERS.DOFUS_OPEN_DISCUSSION, Keys.Tab},
                        { TRIGGERS.TRAVEL, Keys.F6},
                        { TRIGGERS.OPTIONS, Keys.F12},
                        { TRIGGERS.OPTIONS, Keys.F4}
                    };
                }
                if (config.Positions == null)
                {
                    config.Positions = new Dictionary<TRIGGERS_POSITIONS, Position>{
                        { TRIGGERS_POSITIONS.FIGHT_ANALISYS, new Position{ X = 0, Y = 0, Width = Screen.PrimaryScreen.Bounds.Width / 5, Height = Screen.PrimaryScreen.Bounds.Height / 2} },
                        { TRIGGERS_POSITIONS.SELL_CURRENT_MODE, new Position{ X = 0, Y = 0, Width = 0, Height = 0} },
                        { TRIGGERS_POSITIONS.SELL_LOT_1, new Position{ X = 0, Y = 0, Width = 0, Height = 0} },
                        { TRIGGERS_POSITIONS.SELL_LOT_10, new Position{ X = 0, Y = 0, Width = 0, Height = 0} },
                        { TRIGGERS_POSITIONS.SELL_LOT_100, new Position{ X = 0, Y = 0, Width = 0, Height = 0} }
                    };
                }else
                {
                    if (!config.Positions.ContainsKey(TRIGGERS_POSITIONS.FIGHT_ANALISYS))
                    {
                        config.Positions.Add(TRIGGERS_POSITIONS.FIGHT_ANALISYS, new Position { X = 0, Y = 0, Width = Screen.PrimaryScreen.Bounds.Width / 5, Height = Screen.PrimaryScreen.Bounds.Height / 2 });
                    }
                    if (!config.Positions.ContainsKey(TRIGGERS_POSITIONS.SELL_CURRENT_MODE))
                    {
                        config.Positions.Add(TRIGGERS_POSITIONS.SELL_CURRENT_MODE, new Position { X = 0, Y = 0, Width = 0, Height = 0 });
                    }
                    if (!config.Positions.ContainsKey(TRIGGERS_POSITIONS.SELL_LOT_1))
                    {
                        config.Positions.Add(TRIGGERS_POSITIONS.SELL_LOT_1, new Position { X = 0, Y = 0, Width = 0, Height = 0 });
                    }
                    if (!config.Positions.ContainsKey(TRIGGERS_POSITIONS.SELL_LOT_10))
                    {
                        config.Positions.Add(TRIGGERS_POSITIONS.SELL_LOT_10, new Position { X = 0, Y = 0, Width = 0, Height = 0 });
                    }
                    if (!config.Positions.ContainsKey(TRIGGERS_POSITIONS.SELL_LOT_100))
                    {
                        config.Positions.Add(TRIGGERS_POSITIONS.SELL_LOT_100, new Position { X = 0, Y = 0, Width = 0, Height = 0 });
                    }
                }
            }
            else
            {
                config = new Config
                {
                    Panels = new Dictionary<string, PanelConfig>(),
                    Positions = new Dictionary<TRIGGERS_POSITIONS, Position>{
                        { TRIGGERS_POSITIONS.FIGHT_ANALISYS, new Position{ X = 0, Y = 0, Width = Screen.PrimaryScreen.Bounds.Width / 5, Height = Screen.PrimaryScreen.Bounds.Height / 2} }
                    },
                    Keybinds = new Dictionary<TRIGGERS, Keys>{
                        { TRIGGERS.SELECT_NEXT, Keys.F1},
                        { TRIGGERS.SELECT_PREVIOUS, Keys.F2},
                        { TRIGGERS.HAVENBAG, Keys.F3},
                        { TRIGGERS.SIMPLE_CLICK, Keys.XButton1},
                        { TRIGGERS.DOUBLE_CLICK, Keys.XButton2},
                        { TRIGGERS.SIMPLE_CLICK_NO_DELAY, Keys.MButton},
                        { TRIGGERS.GROUP_INVITE, Keys.F5},
                        { TRIGGERS.DOFUS_HAVENBAG, Keys.H},
                        { TRIGGERS.DOFUS_OPEN_DISCUSSION, Keys.Tab},
                        { TRIGGERS.TRAVEL, Keys.F6},
                        { TRIGGERS.OPTIONS, Keys.F12}
                    },
                };
            }
            SaveConfig();
        }

        public static void SaveConfig()
        {
            string configFilePath = "config.json";
            string configFileContent = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(configFilePath, configFileContent);
        }
    }
}
