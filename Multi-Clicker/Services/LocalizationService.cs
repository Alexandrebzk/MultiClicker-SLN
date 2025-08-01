using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using MultiClicker.Properties;

namespace MultiClicker.Services
{
    /// <summary>
    /// Service for managing application localization and language settings
    /// </summary>
    public static class LocalizationService
    {
        /// <summary>
        /// Supported languages in the application
        /// </summary>
        public enum SupportedLanguage
        {
            English,
            French, 
            Spanish
        }

        /// <summary>
        /// Event fired when the language is changed
        /// </summary>
        public static event EventHandler LanguageChanged;

        /// <summary>
        /// Gets the current language setting
        /// </summary>
        public static SupportedLanguage CurrentLanguage { get; private set; } = SupportedLanguage.English;

        /// <summary>
        /// Initialize the localization service with the saved language preference
        /// </summary>
        public static void Initialize()
        {
            try
            {
                // Try to load saved language preference
                var savedLanguage = Properties.Settings.Default.Language;
                if (!string.IsNullOrEmpty(savedLanguage) && Enum.TryParse<SupportedLanguage>(savedLanguage, out var language))
                {
                    SetLanguage(language);
                }
                else
                {
                    // Default to system language if supported, otherwise English
                    SetLanguageFromSystemCulture();
                }
            }
            catch
            {
                // Fallback to English if any error occurs
                SetLanguage(SupportedLanguage.English);
            }
        }

        /// <summary>
        /// Set the application language
        /// </summary>
        /// <param name="language">The language to set</param>
        public static void SetLanguage(SupportedLanguage language)
        {
            CurrentLanguage = language;
            
            var culture = GetCultureInfo(language);
            
            // Set thread culture for resource loading
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            
            // Set application culture
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            
            // Update the Strings resource culture
            Strings.Culture = culture;
            
            // Save language preference
            try
            {
                Properties.Settings.Default.Language = language.ToString();
                Properties.Settings.Default.Save();
            }
            catch
            {
                // Ignore save errors
            }
            
            // Notify listeners
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Get the display name for a language
        /// </summary>
        /// <param name="language">The language</param>
        /// <returns>The display name</returns>
        public static string GetLanguageDisplayName(SupportedLanguage language)
        {
            switch (language)
            {
                case SupportedLanguage.English:
                    return "English";
                case SupportedLanguage.French:
                    return "Français";
                case SupportedLanguage.Spanish:
                    return "Español";
                default:
                    return language.ToString();
            }
        }

        /// <summary>
        /// Get the culture info for a supported language
        /// </summary>
        /// <param name="language">The language</param>
        /// <returns>The culture info</returns>
        private static CultureInfo GetCultureInfo(SupportedLanguage language)
        {
            switch (language)
            {
                case SupportedLanguage.French:
                    return new CultureInfo("fr-FR");
                case SupportedLanguage.Spanish:
                    return new CultureInfo("es-ES");
                case SupportedLanguage.English:
                default:
                    return new CultureInfo("en-US");
            }
        }

        /// <summary>
        /// Set language based on system culture if supported
        /// </summary>
        private static void SetLanguageFromSystemCulture()
        {
            var systemCulture = CultureInfo.CurrentUICulture;
            
            if (systemCulture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase))
            {
                SetLanguage(SupportedLanguage.French);
            }
            else if (systemCulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase))
            {
                SetLanguage(SupportedLanguage.Spanish);
            }
            else
            {
                SetLanguage(SupportedLanguage.English);
            }
        }

        /// <summary>
        /// Get localized string with formatting
        /// </summary>
        /// <param name="key">Resource key</param>
        /// <param name="args">Format arguments</param>
        /// <returns>Formatted localized string</returns>
        public static string GetString(string key, params object[] args)
        {
            try
            {
                var value = Strings.ResourceManager.GetString(key, Strings.Culture);
                if (string.IsNullOrEmpty(value))
                    return key; // Return key if translation not found
                
                return args?.Length > 0 ? string.Format(value, args) : value;
            }
            catch
            {
                return key; // Return key if any error occurs
            }
        }

        /// <summary>
        /// Show a language selection dialog
        /// </summary>
        /// <param name="parent">Parent form</param>
        /// <returns>True if language was changed</returns>
        public static bool ShowLanguageSelectionDialog(Form parent = null)
        {
            using (var form = new Form())
            {
                form.Text = GetString("Language");
                form.Size = new System.Drawing.Size(300, 200);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var panel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 5,
                    Padding = new Padding(10)
                };

                var label = new Label
                {
                    Text = GetString("Language"),
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold)
                };

                var englishButton = new Button
                {
                    Text = GetLanguageDisplayName(SupportedLanguage.English),
                    Dock = DockStyle.Fill,
                    Tag = SupportedLanguage.English
                };

                var frenchButton = new Button
                {
                    Text = GetLanguageDisplayName(SupportedLanguage.French),
                    Dock = DockStyle.Fill,
                    Tag = SupportedLanguage.French
                };

                var spanishButton = new Button
                {
                    Text = GetLanguageDisplayName(SupportedLanguage.Spanish),
                    Dock = DockStyle.Fill,
                    Tag = SupportedLanguage.Spanish
                };

                bool languageChanged = false;

                EventHandler buttonClick = (s, e) =>
                {
                    var button = s as Button;
                    var language = (SupportedLanguage)button.Tag;
                    SetLanguage(language);
                    languageChanged = true;
                    form.Close();
                };

                englishButton.Click += buttonClick;
                frenchButton.Click += buttonClick;
                spanishButton.Click += buttonClick;

                panel.Controls.Add(label, 0, 0);
                panel.Controls.Add(englishButton, 0, 1);
                panel.Controls.Add(frenchButton, 0, 2);
                panel.Controls.Add(spanishButton, 0, 3);

                form.Controls.Add(panel);

                if (parent != null)
                    form.ShowDialog(parent);
                else
                    form.ShowDialog();

                return languageChanged;
            }
        }
    }
}
