using Android.Content;
using Android.Views;

using AndroidX.AppCompat.App;
using AndroidX.AppCompat.Widget;



namespace Mocha.App
{
    /// <summary>
    /// The settings screen: alarm sound and vibration switches, and links to the app's notification settings and
    /// the About screen.
    /// Started for a result. Returns the switch settings whenever they change.
    /// </summary>
    [Activity]
    public class SettingsActivity : AppCompatActivity
    {
        /// <summary>Intent extra key (bool, in and out): play a sound when the alarm goes off?</summary>
        public const string AlarmPlaySoundKey = "AlarmPlaySoundKey";

        /// <summary>Intent extra key (bool, in and out): vibrate when the alarm goes off?</summary>
        public const string AlarmVibrateKey = "AlarmVibrateKey";

        private bool _playSound;
        private bool _vibrate;


        /// <inheritdoc/>
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            SetTheme(Resource.Style.Theme_App);
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.settings_activity);

            // Get the current settings.
            this._playSound = this.Intent!.GetBooleanExtra(AlarmPlaySoundKey, false);
            this._vibrate = this.Intent!.GetBooleanExtra(AlarmVibrateKey, false);

            // Alarm sound switch. Tapping anywhere on its row, or its icon, toggles the switch.
            var alarmSoundSw = FindViewById<SwitchCompat>(Resource.Id.alarmSoundSw)!;
            alarmSoundSw.Checked = this._playSound;
            alarmSoundSw.CheckedChange += (o, e) =>
            {
                this._playSound = e.IsChecked;
                SetActivityResult();
            };
            ((View)FindViewById(Resource.Id.alarmSoundSw)!.Parent!).Click += (o, e) => { alarmSoundSw.PerformClick(); };
            FindViewById(Resource.Id.alarmSoundImg)!.Click += (o, e) => { alarmSoundSw.PerformClick(); };

            // Vibrate switch, set up in the same way.
            var alarmVibrateSw = FindViewById<SwitchCompat>(Resource.Id.alarmVibrateSw)!;
            alarmVibrateSw.Checked = this._vibrate;
            alarmVibrateSw.CheckedChange += (o, e) =>
            {
                this._vibrate = e.IsChecked;
                SetActivityResult();
            };
            ((View)FindViewById(Resource.Id.alarmVibrateSw)!.Parent!).Click += (o, e) => { alarmVibrateSw.PerformClick(); };
            FindViewById(Resource.Id.alarmVibrateImg)!.Click += (o, e) => { alarmVibrateSw.PerformClick(); };

            // Link to app notification settings.
            View v1 = FindViewById(Resource.Id.notificationsImg)!;
            View v2 = (View)v1.Parent!;
            v2.Click += (o, e) => { this.OpenAppNotificationSettings(); };
            v1.Click += (o, e) => { v2.PerformClick(); };

            // Link to the About screen.
            v1 = FindViewById(Resource.Id.infoImg)!;
            v2 = (View)v1.Parent!;
            v2.Click += (o, e) => 
            {
                var intent = new Intent(this, typeof(AboutActivity));
                StartActivity(intent);
            };
            v1.Click += (o, e) => { v2.PerformClick(); };
        }


        /// <summary>
        /// Open the system's notification settings for this app, falling back to more general settings pages if
        /// that isn't available.
        /// </summary>
        private void OpenAppNotificationSettings()
        {
            // See https://stackoverflow.com/a/48816413/67316
            // Note that ActionAppNotificationSettings and ActionApplicationDetailsSettings don't always seem to work
            // so we fall back to using ActionApplicationSettings
            var actions = new string[]
            {
                Android.Provider.Settings.ActionAppNotificationSettings,     // best
                Android.Provider.Settings.ActionApplicationDetailsSettings,  // second best
                Android.Provider.Settings.ActionApplicationSettings          // fallback
            };
            foreach (var action in actions)
            {
                var intent = new Intent(action)
                                    .PutExtra(Android.Provider.Settings.ExtraAppPackage, Application.Context.PackageName);
                if (intent.ResolveActivity(this.PackageManager!) != null)
                {
                    this.StartActivity(intent);
                    return;
                }
            }

            // If we get to here then we failed to invoke the settings with any of the above actions.
            Toast.MakeText(this, Resource.String.openSettingsErrorMsg, ToastLength.Short)!.Show();
        }


        /// <summary>
        /// Set the activity result. We do this each time a switch changes because there doesn't seem to be a reliable 
        /// way to do it when the activity ends.
        /// </summary>
        private void SetActivityResult()
        {
            var intent = new Intent();
            intent.PutExtra(AlarmPlaySoundKey, this._playSound);
            intent.PutExtra(AlarmVibrateKey, this._vibrate);
            this.SetResult(Result.Ok, intent);
        }
    }
}
