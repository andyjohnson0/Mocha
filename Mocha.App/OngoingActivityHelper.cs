using Android.Content;
using Android.OS;

using AndroidX.Core.App;
using AndroidX.Wear.Ongoing;


namespace Mocha.App
{
    /// <summary>
    /// Manages the "coffee brewing" notification and its associated Wear OS ongoing activity, which shows a
    /// countdown on the watch face and in the launcher's Recents section while the timer runs.
    /// </summary>
    public static class OngoingActivityHelper
    {
        /// <summary>
        /// Show the brewing notification and ongoing activity, counting down to the alarm time.
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="alarmTime">Alarm time. Must be UTC, and in the future.</param>
        /// <param name="title">Notification text, which is the brewing method name.</param>
        /// <exception cref="ArgumentException">The alarm time isn't UTC, or isn't in the future.</exception>
        public static void Create(
            Context context,
            DateTime alarmTime,
            string title)
        {
            if (alarmTime.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Alarm time must be UTC");
            if (alarmTime < DateTime.UtcNow)
                throw new ArgumentException("Alarm time must be in the future");

            // Create an ongoing activity notification
            // See:
            // - https://developer.android.com/training/wearables/notifications/ongoing-activity
            // Also see:
            // --https://github.com/android/codelab-ongoing-activity/blob/main/finished/src/main/java/com/android/example/wear/ongoingactivity/ForegroundOnlyWalkingWorkoutService.kt
            // - https://stackoverflow.com/a/54254232/67316
            // - https://stackoverflow.com/a/38107532/67316

            var nowMs = SystemClock.ElapsedRealtime();

            // Create intent.
            // This is a main/launcher intent, so tapping the notification brings the existing app task to the front,
            // like launching the app, and MainActivity.HandleActivityIntent() ignores it.
            var notificationIntent = new Intent(context, typeof(MainActivity))
                .SetAction(Intent.ActionMain)
                .AddCategory(Intent.CategoryLauncher)
                .AddFlags(ActivityFlags.NewTask);
            var pendingIntent = PendingIntent.GetActivity(context, 0, notificationIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            // Create notification builder
            // The builders' setters are bound as returning nullable, so call them as statements rather than chaining.
            var nb = new NotificationCompat.Builder(context, ThisApp.TimerRunningNotificationChannelId);
            nb.SetCategory(NotificationCompat.CategoryAlarm);
            nb.SetSmallIcon(Resource.Drawable.ic_local_cafe);
            nb.SetContentTitle(context.Resources!.GetString(Resource.String.coffeeBrewingMsg));
            nb.SetContentText(title);
            nb.SetContentIntent(pendingIntent);
            nb.SetOngoing(true);
            nb.SetShowWhen(true);
            // SetWhen() takes wall-clock time. An elapsed-realtime value looks decades old, and from Android 16
            // a notification more than 14 days old is ignored. (The timer part below does use elapsed realtime.)
            nb.SetWhen(Java.Lang.JavaSystem.CurrentTimeMillis());

            // Create status. The template's #countdown# is replaced by the part named "countdown": a timer counting down
            // to the alarm time. TimerPart's arguments are the end time (in elapsed realtime), the time it was paused
            // (-1 for not paused) and the total duration.
            var remainingMs = (long)(alarmTime - DateTime.UtcNow).TotalMilliseconds;
            var sb = new Status.Builder();
            sb.AddTemplate("#countdown#");
            sb.AddPart("countdown", new Status.TimerPart(nowMs + remainingMs, -1, remainingMs));

            // Create ongoing activity
            var oab = new OngoingActivity.Builder(context, ThisApp.TimerRunningNotificationId, nb);
            oab.SetCategory(NotificationCompat.CategoryAlarm);
            oab.SetStaticIcon(Resource.Drawable.ic_local_cafe);
            oab.SetStatus(sb.Build());
            oab.Build()!.Apply(context);

            // Show the notification (and the associated ongoing activity)
            NotificationManagerCompat.From(context)!.Notify(ThisApp.TimerRunningNotificationId, nb.Build());
        }


        /// <summary>
        /// Change the ongoing activity's countdown to a "coffee ready" message, if it is showing.
        /// Used when the alarm goes off in the background, leaving the indicator in place until the user responds.
        /// </summary>
        /// <param name="context">Context</param>
        public static void Clear(Context context)
        {
            var oa = OngoingActivity.RecoverOngoingActivity(context);
            if (oa != null)
            {
                var sb = new Status.Builder();
                sb.AddTemplate("#countdown#");
                sb.AddPart("countdown", new Status.TextPart(context.Resources!.GetString(Resource.String.coffeeReadyMsg)));
                oa.Update(context, sb.Build());
            }
        }


        /// <summary>
        /// Remove the brewing notification and ongoing activity, if they are showing.
        /// </summary>
        /// <param name="context">Context</param>
        public static void Cancel(Context context)
        {
            // Cancelling the notification also removes its associated ongoing activity.
            NotificationManagerCompat.From(context)!.Cancel(ThisApp.TimerRunningNotificationId);
        }
    }
}
