using Android.OS;
using Android.Views;

using AndroidX.AppCompat.App;


namespace Mocha.App
{
    /// <summary>
    /// The "coffee ready" screen. Optionally sounds the alarm, via <see cref="AlarmService"/>, until the user taps OK
    /// or dismisses the screen, or the alarm times out.
    /// </summary>
    [Activity]
    public class AlarmActivity : AppCompatActivity
    {
        /// <summary>Intent extra key (bool): play the alarm sound?</summary>
        public const string PlaySoundKey = "PlaySoundKey";

        /// <summary>Intent extra key (bool): vibrate?</summary>
        public const string VibrateKey = "VibrateKey";


        /// <inheritdoc/>
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            SetTheme(Resource.Style.Theme_App);
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.alarm_activity);

            _playSound = this.Intent!.GetBooleanExtra(PlaySoundKey, false);
            _vibrate = this.Intent!.GetBooleanExtra(VibrateKey, false);

            FindViewById<ImageButton>(Resource.Id.okButton)!.Click += OkBtn_Click;

            // Start the alarm service once, when the activity is first created. Not in OnStart(), which is called
            // each time the screen wakes, and not when the activity is recreated (e.g. after a configuration change).
            if (savedInstanceState == null && (_playSound || _vibrate))
            {
                Mocha.App.AlarmService.Start(this, _playSound, _vibrate);
            }

            // Keep the screen on while the alarm sounds. Otherwise, from Android 17, the alarm would be silenced
            // when the screen turns off, because it is played by a service that isn't a foreground service.
            this.Window!.AddFlags(WindowManagerFlags.KeepScreenOn);
            this._handler.PostDelayed(() => { this.Window?.ClearFlags(WindowManagerFlags.KeepScreenOn); }, Mocha.App.AlarmService.AlarmTimeoutMsec);
        }


        private readonly Handler _handler = new Handler(Looper.MainLooper!);
        private bool _playSound;
        private bool _vibrate;


        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            base.OnDestroy();

            this._handler.RemoveCallbacksAndMessages(null);

            // Stop the alarm service when the activity is finished (OK, or dismissed by the user), but not when it
            // is only stopped (e.g. the screen turning off) or being recreated.
            if (this.IsFinishing)
            {
                Mocha.App.AlarmService.Stop(this);
            }
        }



        /// <summary>
        /// OK button handler. Finishes the activity, which stops the alarm.
        /// </summary>
        private void OkBtn_Click(object? sender, EventArgs e)
        {
            this.SetResult(Result.Ok);
            this.Finish();
        }
    }
}
