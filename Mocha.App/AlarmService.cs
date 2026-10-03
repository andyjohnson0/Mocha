using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using Android.Util;

using Kilsten.Android;


namespace Mocha.App
{
    /// <summary>
    /// Sounds the alarm and/or vibrates, as allowed by the Do Not Disturb settings, then ends the alarm after
    /// <see cref="AlarmTimeoutMsec"/>. Started by <see cref="AlarmActivity"/> when the alarm goes off in the
    /// foreground, and by <see cref="AlarmReceiver"/> when it goes off in the background.
    /// </summary>
    /// <remarks>
    /// For a background alarm on Android 17 (SDK 37) and above, the service runs as a foreground service, because
    /// background audio is otherwise silenced. It uses type systemExempted, which is available to apps that hold
    /// SCHEDULE_EXACT_ALARM or USE_EXACT_ALARM, and is intended for continuing alarms in the background.
    /// See https://developer.android.com/develop/background-work/services/fgs/service-types and
    /// https://developer.android.com/about/versions/17/changes/bg-audio
    /// </remarks>
    [Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSystemExempted)]
    internal class AlarmService : Service
    {
        /// <summary>Intent extra key (bool): play the alarm sound?</summary>
        public const string PlaySoundKey = "PlaySoundKey";

        /// <summary>Intent extra key (bool): vibrate?</summary>
        public const string VibrateKey = "VibrateKey";

        /// <summary>Intent extra key (bool): did the alarm go off while the app was in the background?</summary>
        public const string BackgroundKey = "BackgroundKey";

        /// <summary>
        /// Start the alarm service.
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="playSound">Play the alarm sound?</param>
        /// <param name="vibrate">Vibrate?</param>
        /// <param name="background">
        /// Did the alarm go off while the app was in the background? If so, <see cref="IsBackgroundAlarmSounding"/>
        /// is set, and on SDK 37 and above the service runs as a foreground service, with its own silent
        /// notification. Starting a foreground service from the background is allowed here because it is triggered
        /// by an exact alarm.
        /// </param>
        public static void Start(
            Context context,
            bool playSound,
            bool vibrate,
            bool background = false)
        {
            var serviceIntent = new Intent(context, typeof(AlarmService));
            serviceIntent.PutExtra(Mocha.App.AlarmService.PlaySoundKey, playSound);
            serviceIntent.PutExtra(Mocha.App.AlarmService.VibrateKey, vibrate);
            serviceIntent.PutExtra(Mocha.App.AlarmService.BackgroundKey, background);
            if (background && NeedsForegroundService)
            {
                context.StartForegroundService(serviceIntent);
            }
            else
            {
                context.StartService(serviceIntent);
            }
        }


        /// <summary>
        /// Stop the alarm service, if it is running. If it is a foreground service, this also removes its
        /// notification.
        /// </summary>
        /// <param name="context">Context</param>
        public static void Stop(Context context)
        {
            // Clear this immediately, because the service stops asynchronously.
            Mocha.App.AlarmService.IsBackgroundAlarmSounding = false;

            var serviceIntent = new Intent(context, typeof(AlarmService));
            context.StopService(serviceIntent);
        }


        /// <summary>
        /// Is the alarm service running?
        /// </summary>
        /// <remarks>
        /// This is static state, so it only reflects the service in the current process.
        /// </remarks>
        public static bool IsRunning { get; private set; } = false;


        /// <summary>
        /// Is the service sounding an alarm that went off while the app was in the background, which hasn't yet timed
        /// out or been stopped?
        /// </summary>
        /// <remarks>
        /// This is static state, so it only reflects the service in the current process.
        /// </remarks>
        public static bool IsBackgroundAlarmSounding { get; private set; } = false;


        /// <summary>
        /// Does a background alarm need a foreground service? From Android 17 (SDK 37), background audio is silenced
        /// unless the app has a visible activity or a foreground service. Below that, a plain service is used, which
        /// avoids showing the foreground service's notification alongside the "coffee ready" notification.
        /// </summary>
        private static bool NeedsForegroundService => (int)Build.VERSION.SdkInt >= 37;


        /// <summary>
        /// How long the alarm sounds for, in milliseconds.
        /// </summary>
        public const int AlarmTimeoutMsec = 30 * 1000;

        private const int _vibrateTimeoutMsec = 10 * 1000;

        private readonly Handler _handler = new Handler(Looper.MainLooper!);
        private MediaPlayer? _mediaPlayer;
        private AudioFocusRequestClass? _audioFocusRequest;
        private bool _isForeground;


        /// <inheritdoc/>
        public override StartCommandResult OnStartCommand(
            Intent? intent,
            StartCommandFlags flags,
            int startId)
        {
            ArgumentNullException.ThrowIfNull(intent, nameof(intent));

            if (intent.GetBooleanExtra(BackgroundKey, false))
            {
                Mocha.App.AlarmService.IsBackgroundAlarmSounding = true;

                // If started with StartForegroundService() then we must call StartForeground() promptly, whatever
                // else happens, or the system stops the app.
                if (NeedsForegroundService)
                {
                    StartAsForeground();
                }
            }

            var playSound = intent.GetBooleanExtra(PlaySoundKey, false);
            var vibrate = intent.GetBooleanExtra(VibrateKey, false);

            // Check current DnD settings
            var dnd = this.GetDndAlarmSettings();
            playSound = playSound && dnd.allowSound;
            vibrate = vibrate && dnd.allowVibration;

            // Note: this assumes the service isn't already sounding the alarm. If it were started again while running,
            // StartAlarmSoundPlaying() would replace the media player and audio focus request without releasing the
            // old ones. No current code path does this: a running alarm is always stopped before it is started again.
            if (playSound)
            {
                StartAlarmSoundPlaying();
            }
            if (vibrate)
            {
                StartVibrating();
            }

            // End the alarm after a timeout. Vibration stops itself before this.
            this._handler.RemoveCallbacksAndMessages(null);
            this._handler.PostDelayed(EndAlarm, AlarmTimeoutMsec);

            Mocha.App.AlarmService.IsRunning = true;

            // The alarm is transient, so don't have the system restart the service if it is killed.
            // A sticky restart would also pass a null intent, which we don't handle.
            return StartCommandResult.NotSticky;
        }


        /// <inheritdoc/>
        public override void OnDestroy()
        {
            base.OnDestroy();

            this._handler.RemoveCallbacksAndMessages(null);
            StopAlarmSoundPlaying();
            StopVibrating();

            Mocha.App.AlarmService.IsRunning = false;
            Mocha.App.AlarmService.IsBackgroundAlarmSounding = false;
        }


        /// <summary>
        /// Binding is not supported. The service is only started and stopped.
        /// </summary>
        public override IBinder? OnBind(Intent? intent)
        {
            return null;
        }



        #region Implementation

        /// <summary>
        /// Make this a foreground service, with its own silent notification.
        /// </summary>
        private void StartAsForeground()
        {
            var notification = BuildServiceNotification();
            if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
            {
                // SDK 34 and above. The systemExempted type doesn't exist before this.
#pragma warning disable CA1416
                StartForeground(ThisApp.AlarmServiceNotificationId, notification, ForegroundService.TypeSystemExempted);
#pragma warning restore CA1416
            }
            else
            {
                // SDK 30 to 33
                StartForeground(ThisApp.AlarmServiceNotificationId, notification);
            }
            this._isForeground = true;
        }


        /// <summary>
        /// Build the foreground service notification. Android requires a foreground service to have one, but the
        /// user-facing "coffee ready" notification is posted separately by <see cref="AlarmReceiver"/>, as an ordinary
        /// notification. (A foreground service's notification is always ongoing, and when the "coffee ready"
        /// notification was used as the foreground service notification, a Wear OS 3 watch didn't show it.) So this
        /// one is silent and low priority. Tapping it, where it is shown, does the same as tapping the "coffee ready"
        /// notification.
        /// </summary>
        private Notification BuildServiceNotification()
        {
            // The builder's setters are bound as returning nullable, so call them as statements rather than chaining.
            var nb = new AndroidX.Core.App.NotificationCompat.Builder(this, ThisApp.TimerRunningNotificationChannelId);
            nb.SetSmallIcon(Resource.Drawable.ic_local_cafe);
            nb.SetContentTitle(this.Resources!.GetString(Resource.String.coffeeReadyMsg));
            nb.SetContentIntent(AlarmReceiver.CreateAlarmContentIntent(this));
            nb.SetCategory(AndroidX.Core.App.NotificationCompat.CategoryService);
            nb.SetPriority(AndroidX.Core.App.NotificationCompat.PriorityLow);
            nb.SetSilent(true);
            nb.SetOngoing(true);
            return nb.Build()!;
        }


        /// <summary>
        /// End the alarm when it times out, and stop the service.
        /// </summary>
        private void EndAlarm()
        {
            StopAlarmSoundPlaying();

            // If we're a foreground service then remove our notification. The "coffee ready" notification posted by
            // AlarmReceiver remains, so the user can still open or dismiss it.
            if (this._isForeground)
            {
                StopForeground(StopForegroundFlags.Remove);
                this._isForeground = false;
            }
            Mocha.App.AlarmService.IsBackgroundAlarmSounding = false;
            StopSelf();
        }


        /// <summary>
        /// Start playing the alarm sound, if there is one, and request audio focus.
        /// </summary>
        private void StartAlarmSoundPlaying()
        {
            var alarmSound = AlarmHelper.GetAlarmSound();
            if (alarmSound != null)
            {
                // Start playing the alarm sound.
                // The audio attributes must be passed to Create(), because they have no effect if set after the
                // player is prepared, which Create() does.
                var attribs = new AudioAttributes.Builder()!
                    .SetUsage(AudioUsageKind.Alarm)!
                    .SetContentType(AudioContentType.Sonification)!
                    .Build();
                int audioSessionId;
                using (var audioManager = (AudioManager)this.GetSystemService(Context.AudioService)!)
                {
                    audioSessionId = audioManager.GenerateAudioSessionId();

                    // Request transient audio focus so that other audio (e.g. music) pauses while the alarm sounds.
                    // Play the alarm even if this fails.
                    this._audioFocusRequest = new AudioFocusRequestClass.Builder(AudioFocus.GainTransient)
                        .SetAudioAttributes(attribs!)
                        .Build()!;
                    var focusResult = audioManager.RequestAudioFocus(this._audioFocusRequest);
                    Log.Debug("MOCHA", $"Alarm audio focus request result: {focusResult}");
                }
                this._mediaPlayer = MediaPlayer.Create(this, alarmSound, null, attribs, audioSessionId)!;
                _mediaPlayer.Start();
            }
        }


        /// <summary>
        /// Stop the alarm sound, if it is playing, and release the player and audio focus.
        /// </summary>
        private void StopAlarmSoundPlaying()
        {
            if (this._mediaPlayer != null)
            {
                // Release even if not playing, e.g. if the sound finished by itself.
                if (this._mediaPlayer.IsPlaying)
                {
                    this._mediaPlayer.Stop();
                }
                this._mediaPlayer.Release();
                this._mediaPlayer = null;
            }

            if (this._audioFocusRequest != null)
            {
                using (var audioManager = (AudioManager)this.GetSystemService(Context.AudioService)!)
                {
                    audioManager.AbandonAudioFocusRequest(this._audioFocusRequest);
                }
                this._audioFocusRequest = null;
            }
        }


        /// <summary>
        /// Start a single vibration, which stops by itself after the vibration timeout.
        /// </summary>
        private void StartVibrating()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                // SDK 33 and above
#pragma warning disable CA1416
                var attribs = new VibrationAttributes.Builder()
                                         .SetUsage((int)VibrationAttributesUsageType.Alarm)!
                                         .Build();
                var effect = VibrationEffect.CreateOneShot(_vibrateTimeoutMsec, VibrationEffect.DefaultAmplitude);
                GetVibrator()?.Vibrate(effect!, attribs);
#pragma warning restore CA1416
            }
            else
            {
                // SDK 30 to 32
#pragma warning disable CA1422
                var attribs = new AudioAttributes.Builder()
                         .SetUsage(AudioUsageKind.Alarm)!
                         .Build();
                var effect = VibrationEffect.CreateOneShot(_vibrateTimeoutMsec, VibrationEffect.DefaultAmplitude);
                GetVibrator()?.Vibrate(effect!, attribs);
#pragma warning restore CA1422
            }
        }


        /// <summary>
        /// Stop any vibration.
        /// </summary>
        private void StopVibrating()
        {
            GetVibrator()?.Cancel();
        }


        /// <summary>
        /// Get the device's default vibrator, using the API appropriate to the SDK version.
        /// </summary>
        /// <returns>Vibrator, or null if there isn't one.</returns>
        private Vibrator? GetVibrator()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                // SDK 31 and above
#pragma warning disable CA1416
                using (var vibratorManager = this.GetSystemService(Context.VibratorManagerService) as VibratorManager)
                {
                    return vibratorManager?.DefaultVibrator ?? null;
                }
#pragma warning restore CA1416
            }
            else
            {
                // SDK 30 and above
#pragma warning disable CA1422
                return this.GetSystemService(Context.VibratorService) as Vibrator;
#pragma warning restore CA1422
            }
        }


        #endregion Implementation
    }
}
