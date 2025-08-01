using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
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
    /// Represents a key combination with modifiers and optional mouse buttons
    /// </summary>
    [JsonConverter(typeof(KeyCombinationConverter))]
    public class KeyCombination
    {
        public Keys Key { get; set; }
        public bool Control { get; set; }
        public bool Shift { get; set; }
        public bool Alt { get; set; }
        public bool LeftMouseButton { get; set; }
        public bool RightMouseButton { get; set; }
        public bool MiddleMouseButton { get; set; }
        public bool XButton1 { get; set; }
        public bool XButton2 { get; set; }

        public KeyCombination()
        {
            Key = Keys.None;
            Control = false;
            Shift = false;
            Alt = false;
            LeftMouseButton = false;
            RightMouseButton = false;
            MiddleMouseButton = false;
            XButton1 = false;
            XButton2 = false;
        }

        public KeyCombination(Keys key, bool control = false, bool shift = false, bool alt = false, 
                             bool leftMouse = false, bool rightMouse = false, bool middleMouse = false,
                             bool xButton1 = false, bool xButton2 = false)
        {
            Key = key;
            Control = control;
            Shift = shift;
            Alt = alt;
            LeftMouseButton = leftMouse;
            RightMouseButton = rightMouse;
            MiddleMouseButton = middleMouse;
            XButton1 = xButton1;
            XButton2 = xButton2;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Control) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            if (Key != Keys.None) parts.Add(Key.ToString());
            if (LeftMouseButton) parts.Add("LClick");
            if (RightMouseButton) parts.Add("RClick");
            if (MiddleMouseButton) parts.Add("MClick");
            if (XButton1) parts.Add("X1");
            if (XButton2) parts.Add("X2");
            return parts.Count > 0 ? string.Join(" + ", parts) : "";
        }

        public static KeyCombination FromKeys(Keys keys)
        {
            var combination = new KeyCombination();
            combination.Control = (keys & Keys.Control) == Keys.Control;
            combination.Shift = (keys & Keys.Shift) == Keys.Shift;
            combination.Alt = (keys & Keys.Alt) == Keys.Alt;
            combination.Key = keys & ~Keys.Control & ~Keys.Shift & ~Keys.Alt;
            return combination;
        }

        public Keys ToKeys()
        {
            Keys result = Key;
            if (Control) result |= Keys.Control;
            if (Shift) result |= Keys.Shift;
            if (Alt) result |= Keys.Alt;
            return result;
        }

        public bool HasMouseButtons => LeftMouseButton || RightMouseButton || MiddleMouseButton || XButton1 || XButton2;
        
        public bool IsEmpty => Key == Keys.None && !HasMouseButtons;
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
        GROUP_CHARACTERS,
        FILL_HDV,
        PASTE_ON_ALL_WINDOWS,
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
        public Dictionary<TRIGGERS, KeyCombination> Keybinds { get; set; }

        public Config()
        {
            General = new GeneralConfig();
            Panels = new Dictionary<string, PanelConfig>();
            Positions = new Dictionary<TRIGGERS_POSITIONS, Position>();
            Keybinds = new Dictionary<TRIGGERS, KeyCombination>();
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
    
    /// <summary>
    /// Custom JSON converter for KeyCombination to handle migration from old Keys format
    /// </summary>
    public class KeyCombinationConverter : JsonConverter<KeyCombination>
    {
        public override void WriteJson(JsonWriter writer, KeyCombination value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            writer.WritePropertyName("Key");
            writer.WriteValue((int)value.Key);
            writer.WritePropertyName("Control");
            writer.WriteValue(value.Control);
            writer.WritePropertyName("Shift");
            writer.WriteValue(value.Shift);
            writer.WritePropertyName("Alt");
            writer.WriteValue(value.Alt);
            writer.WritePropertyName("LeftMouseButton");
            writer.WriteValue(value.LeftMouseButton);
            writer.WritePropertyName("RightMouseButton");
            writer.WriteValue(value.RightMouseButton);
            writer.WritePropertyName("MiddleMouseButton");
            writer.WriteValue(value.MiddleMouseButton);
            writer.WritePropertyName("XButton1");
            writer.WriteValue(value.XButton1);
            writer.WritePropertyName("XButton2");
            writer.WriteValue(value.XButton2);
            writer.WriteEndObject();
        }

        public override KeyCombination ReadJson(JsonReader reader, Type objectType, KeyCombination existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            if (reader.TokenType == JsonToken.Integer)
            {
                // Old format: direct Keys enum value
                var keyValue = Convert.ToInt32(reader.Value);
                var keys = (Keys)keyValue;
                return KeyCombination.FromKeys(keys);
            }

            if (reader.TokenType == JsonToken.StartObject)
            {
                // New format: KeyCombination object
                var obj = JObject.Load(reader);

                var keyCombination = new KeyCombination();

                if (obj["Key"] != null)
                    keyCombination.Key = (Keys)obj["Key"].Value<int>();

                if (obj["Control"] != null)
                    keyCombination.Control = obj["Control"].Value<bool>();

                if (obj["Shift"] != null)
                    keyCombination.Shift = obj["Shift"].Value<bool>();

                if (obj["Alt"] != null)
                    keyCombination.Alt = obj["Alt"].Value<bool>();

                // Handle new mouse button properties (default to false if not present for backward compatibility)
                if (obj["LeftMouseButton"] != null)
                    keyCombination.LeftMouseButton = obj["LeftMouseButton"].Value<bool>();

                if (obj["RightMouseButton"] != null)
                    keyCombination.RightMouseButton = obj["RightMouseButton"].Value<bool>();

                if (obj["MiddleMouseButton"] != null)
                    keyCombination.MiddleMouseButton = obj["MiddleMouseButton"].Value<bool>();

                // Handle X button properties (default to false if not present for backward compatibility)
                if (obj["XButton1"] != null)
                    keyCombination.XButton1 = obj["XButton1"].Value<bool>();

                if (obj["XButton2"] != null)
                    keyCombination.XButton2 = obj["XButton2"].Value<bool>();

                return keyCombination;
            }

            throw new JsonSerializationException($"Unexpected token type: {reader.TokenType}");
        }
    }
}
