using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.OS;

using AndroidX.Core.App;
using AndroidX.Core.Content;


namespace Mocha.App
{
    /// <summary>
    /// Receives the countdown alarm set by <see cref="AlarmHelper"/>. If the app is in the background, sounds the
    /// alarm and shows the "coffee ready" notification. If the app is in the foreground, does nothing, because
    /// <see cref="MainActivity"/> handles the alarm itself.
    /// </summary>
    [BroadcastReceiver(Enabled = true, Exported = false)]
    [IntentFilter(new[] { AlarmHelper.AlarmActionName })]
    public class AlarmReceiver : BroadcastReceiver
    {
        /// <summary>
        /// Intent action name for <see cref="MainActivity"/> being opened from the "coffee ready" notification.
        /// </summary>
        public const string AlarmNotificationActionName = "uk.andyjohnson.Mocha.AlarmNotificationActionName";


        private const long _wakeLockTimeoutMsec = 6000L;


        /// <inheritdoc/>
        public override void OnReceive(Context? context, Intent? _)
        {
            // Broadcast has been received from the Alarm started by AlarmHelper

            ArgumentNullException.ThrowIfNull(context, nameof(context));

            Log.Debug("MOCHA", "In AlarmReceiver.OnReceive()");

            if (ThisApp.Instance().IsForeground)
            {
                // App is in the foreground. Do nothing and let the tick loop alert the user.
                Log.Debug("MOCHA", "App is in foreground. Masking alarm.");
                return;
            }

            // We're in the background and Android 10+ puts restrictions on starting an activity from here.
            // see https://developer.android.com/guide/components/activities/background-starts.
            //
            // One option is to use the approach described at https://www.giorgosneokleous.com/post/full-screen-intent-notifications-android/
            // to create a high-priority, full-screen notification to foreground the app. Also see https://stackoverflow.com/a/57869617/67316
            // and https://slack-chats.kotlinlang.org/t/13254400/hi-i-want-to-be-able-to-display-fullscreen-notification-to-d.
            // But I can't get this to work on Wear OS.
            //
            // Another option is to display the AlarmIntent on the lock screen as described at
            // https://hackernoon.com/ditch-the-notification-and-show-an-activity-on-your-android-lock-screen-instead.
            // But I can't get this to work on Wear OS either.
            //
            // So for now we just display a high-importance notification that invokes the MainActivity, which
            // falls-through to the same intent-handling that the foreground case (above) uses. This seems to be
            // what the Wear OS Timer app does.
            //
            // To sound the alarm we use the approach  of using a background serivce described at
            // https://www.pagerduty.com/eng/android-oreo-notifications/

            Log.Debug("MOCHA", "App is in background. Starting the alarm service, which sounds the alarm and shows a notification.");

            // Wake the device up.
            // A partial wake lock keeps the CPU running (not the screen) while the alarm service starts. It isn't
            // released explicitly: the timeout releases it.
            using (var powerManager = (PowerManager)context.GetSystemService(Context.PowerService)!)
            {
                PowerManager.WakeLock wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial, "uk.andyjohnson.Mocha.WakeLockTag")!;
                wakeLock.Acquire(_wakeLockTimeoutMsec);
            }

            // Clear the ongoing activity. It gets cancelled in the handler for the notification we build below.
            OngoingActivityHelper.Clear(context);

            // Record that the timer has stopped, so that the main activity doesn't trigger the alarm again when it
            // is next foregrounded.
            MainActivity.SaveTimerStopped(context);

            // Start the alarm service, which sounds the alarm. On SDK 37 and above it runs as a foreground service.
            var settings = Settings.Load(context);
            Mocha.App.AlarmService.Start(context, settings.AlarmPlaySound, settings.AlarmVibrate, background: true);

            // Show the "coffee ready" notification, which invokes the main activity. The main activity's intent handler
            // will stop the alarm.
            // This is an ordinary notification, separate from any foreground service notification, which is always
            // ongoing. When the "coffee ready" notification was used as the foreground service notification, a
            // Wear OS 3 watch didn't show it.
            NotificationManagerCompat.From(context)!.Notify(ThisApp.AlarmNotificationId, BuildAlarmNotification(context));
        }


        /// <summary>
        /// Create a pending intent that opens the main activity to handle an alarm that went off in the background.
        /// </summary>
        internal static PendingIntent CreateAlarmContentIntent(Context context)
        {
            var alarmIntent = new Intent(context, typeof(MainActivity));
            alarmIntent.SetAction(AlarmReceiver.AlarmNotificationActionName);
            alarmIntent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            return PendingIntent.GetActivity(context, 0, alarmIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        }


        /// <summary>
        /// Build the "coffee ready" notification that is shown when the alarm goes off in the background.
        /// </summary>
        private static Notification BuildAlarmNotification(Context context)
        {
            // Create an intent to invoke the main activity and wrap it in a pending intent
            var alarmPendingIntent = CreateAlarmContentIntent(context);

            // Create a deletion (cancellation) intent for the above, and wrap it in a pending intent.
            // This is a broadcast so that dismissing the notification doesn't bring the app to the foreground.
            var alarmDeletedIntent = new Intent(context, typeof(AlarmNotificationDeletedReceiver));
            var alarmDeletedPendingIntent = PendingIntent.GetBroadcast(context, 0, alarmDeletedIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

            // Build a high-priority notification.
            // The builder's setters are bound as returning nullable, so call them as statements rather than chaining.
            var nb = new NotificationCompat.Builder(context, ThisApp.AlarmNotificationChannelId);
            nb.SetColor(ContextCompat.GetColor(context, Resource.Color.colorAccent));
            nb.SetSmallIcon(Resource.Drawable.ic_local_cafe);
            nb.SetLargeIcon(BitmapFactory.DecodeResource(context.Resources, Resource.Mipmap.ic_launcher));
            nb.SetContentTitle(context.Resources!.GetString(Resource.String.coffeeReadyMsg));
            nb.SetDefaults((int)NotificationDefaults.All);
            nb.SetContentIntent(alarmPendingIntent);  // <-- needed
            nb.SetCategory(Notification.CategoryAlarm);
            nb.SetPriority((int)NotificationPriority.High);
            nb.SetDeleteIntent(alarmDeletedPendingIntent);
            nb.SetAutoCancel(true);
            return nb.Build()!;
        }
    }
}
