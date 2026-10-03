using Android.Content;


namespace Mocha.App
{
    /// <summary>
    /// User settings, persisted in shared preferences.
    /// </summary>
    public class Settings
    {
        /// <summary>
        /// Play a sound when the alarm goes off?
        /// </summary>
        public required bool AlarmPlaySound { get; set; }

        /// <summary>
        /// Vibrate when the alarm goes off?
        /// </summary>
        public required bool AlarmVibrate { get; set; }


        /// <summary>
        /// Load the settings, using defaults for any that haven't been saved.
        /// </summary>
        /// <param name="context">Context</param>
        /// <returns>Settings</returns>
        public static Settings Load(Context context)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            using (var prefs = Settings.GetSharedPreferences(context))
            {
                return new Settings()
                {
                    AlarmPlaySound = prefs.GetBoolean(_alarmPlaySoundKey, true),
                    AlarmVibrate = prefs.GetBoolean(_alarmVibrateKey, true)
                };
            }
        }


        /// <summary>
        /// Save the settings.
        /// </summary>
        /// <param name="context">Context</param>
        public void Save(Context context)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            using (var prefs = Settings.GetSharedPreferences(context))
            {
                using (var editor = prefs.Edit()!)
                {
                    editor.Clear();
                    editor.PutInt(Settings._schemaVersionKey, Settings._schemaVersionId);
                    editor.PutBoolean(Settings._alarmPlaySoundKey, this.AlarmPlaySound);
                    editor.PutBoolean(Settings._alarmVibrateKey, this.AlarmVibrate);
                    editor.Apply();
                }
            }
        }


        /// <summary>
        /// Delete the saved settings, so that the next <see cref="Load(Context)"/> returns the defaults.
        /// </summary>
        /// <param name="context">Context</param>
        public static void Reset(Context context)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            using (var prefs = Settings.GetSharedPreferences(context))
            {
                using (var editor = prefs.Edit()!)
                {
                    editor.Clear();
                    editor.Apply();
                }
            }

        }


        #region Implementation

        private const string _schemaVersionKey = "_schema_version";
        private const int _schemaVersionId = 1;

        private const string _alarmPlaySoundKey = "alarmPlaySoundKey";
        private const string _alarmVibrateKey = "alarmVibrateKey";


        private static ISharedPreferences GetSharedPreferences(Context context)
        {
            return context.GetSharedPreferences(context.PackageName + "SettingsPrefs", FileCreationMode.Private)!;
        }

        #endregion Implementation
    }
}
