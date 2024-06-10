using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

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

    public class Config 
    {
        public Dictionary<string, PanelConfig> Panels { get; set; }
        public Dictionary<string, Position> Positions { get; set; }
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
            }
            else
            {
                config = new Config
                {
                    Panels = new Dictionary<string, PanelConfig>(),
                    Positions = new Dictionary<string, Position>()
                };
                SaveConfig();
            }
        }

        public static void SaveConfig()
        {
            string configFilePath = "config.json";
            string configFileContent = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(configFilePath, configFileContent);
        }
    }
}
