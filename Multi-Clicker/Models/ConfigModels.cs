using System.Collections.Generic;
using System.Windows.Forms;

namespace MultiClicker.Models
{
    /// <summary>
    /// Configuration model for individual panels
    /// </summary>
    public class PanelConfig
    {
        public string Background { get; set; }
    }

    /// <summary>
    /// General application configuration
    /// </summary>
    public class GeneralConfig
    {
        public string GameVersion { get; set; }
        public int MinimumFollowDelay { get; set; }
        public int FollowNoDelay { get; set; }
        public int MaximumFollowDelay { get; set; }
    }

    /// <summary>
    /// Position configuration for UI elements
    /// </summary>
    public class Position
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    /// <summary>
    /// Enumeration for different trigger types
    /// </summary>
    public enum TRIGGERS
    {
        SELECT_NEXT,
        SELECT_PREVIOUS,
        TRAVEL,
        OPTIONS,
        SIMPLE_CLICK,
        DOUBLE_CLICK,
        SIMPLE_CLICK_NO_DELAY,
        DOFUS_HAVENBAG,
        DOFUS_OPEN_DISCUSSION,
    }

    /// <summary>
    /// Enumeration for position-based triggers
    /// </summary>
    public enum TRIGGERS_POSITIONS
    {
        SELL_CURRENT_MODE,
        SELL_LOT_1,
        SELL_LOT_10,
        SELL_LOT_100,
        SELL_LOT_1000
    }

    /// <summary>
    /// Main configuration model containing all application settings
    /// </summary>
    public class Config
    {
        public GeneralConfig General { get; set; }
        public Dictionary<string, PanelConfig> Panels { get; set; }
        public Dictionary<TRIGGERS_POSITIONS, Position> Positions { get; set; }
        public Dictionary<TRIGGERS, Keys> Keybinds { get; set; }

        public Config()
        {
            General = new GeneralConfig();
            Panels = new Dictionary<string, PanelConfig>();
            Positions = new Dictionary<TRIGGERS_POSITIONS, Position>();
            Keybinds = new Dictionary<TRIGGERS, Keys>();
        }
    }

    /// <summary>
    /// Window information model
    /// </summary>
    public class WindowInfo
    {
        public string WindowName { get; set; }
        public string CharacterName { get; set; }
        public object RelatedPanel { get; set; }
    }

    /// <summary>
    /// Point structure for coordinates
    /// </summary>
    public struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Rectangle structure for window bounds
    /// </summary>
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
