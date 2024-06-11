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
    }

    public enum TRIGGERS
    {
        SELECT_NEXT,
        SELECT_PREVIOUS,
        HAVENBAG,
        TRAVEL,
        SIMPLE_CLICK,
        DOUBLE_CLICK,
        SIMPLE_CLICK_NO_DELAY,
        GROUP_INVITE,
        DOFUS_HAVENBAG,
        DOFUS_OPEN_DISCUSSION,
    }

    public class Config 
    {
        public Dictionary<string, PanelConfig> Panels { get; set; }
        public Dictionary<string, Position> Positions { get; set; }
        public Dictionary<TRIGGERS, Keys> Keybinds { get; set; }
    }
    public static class ConfigManagement
    {
        public static Config config;

        public static void LoadConfig()
        {
            string configFilePath = "config.json";
            if (File.Exists(configFilePath))
            {
                string configFileContent = File.ReadAllText(configFilePath);
                config = JsonConvert.DeserializeObject<Config>(configFileContent);
                if (config.Keybinds == null)
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
                        { TRIGGERS.TRAVEL, Keys.F6}
                    };
                }
            }
            else
            {
                config = new Config
                {
                    Panels = new Dictionary<string, PanelConfig>(),
                    Positions = new Dictionary<string, Position>(),
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
                        { TRIGGERS.TRAVEL, Keys.F6}
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
