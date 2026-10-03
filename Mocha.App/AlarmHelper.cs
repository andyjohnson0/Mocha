using Android.Content;
using Android.Media;
using Android.OS;
using Android.Util;


namespace Mocha.App
{
    /// <summary>
    /// Sets and cancels the system alarm that fires when the brew time is up, which is delivered to
    /// <see cref="AlarmReceiver"/>.
    /// </summary>
    public static class AlarmHelper
    {
        /// <summary>
        /// Intent action name for the alarm broadcast.
        /// </summary>
        public const string AlarmActionName = "uk.andyjohnson.Mocha.AlarmActionName";

        /// <summary>
        /// Set the countdown alarm, replacing any existing one. This requires exact alarm access.
        /// </summary>
        /// <param name="context">Context</param>
        /// <param name="alarmTime">Alarm time. Must be UTC, and in the future.</param>
        /// <exception cref="ArgumentException">The alarm time isn't UTC, or isn't in the future.</exception>
        public static void SetCountdownAlarm(
            Context context,
            DateTime alarmTime)
        {
            if (alarmTime.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Alarm time must be UTC");
            if (alarmTime < DateTime.UtcNow)
                throw new ArgumentException("Alarm time must be in the future");

            var alarmIntent = new Intent(context, typeof(AlarmReceiver));
            alarmIntent.SetAction(AlarmHelper.AlarmActionName);

            // The pending intent must match the one in CancelCountdownAlarm() (same request code and intent), because
            // that is how the alarm to cancel is identified. It also means that setting a new alarm replaces the old one.
            var pendingIntent = PendingIntent.GetBroadcast(context, 0, alarmIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

            using (var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService)!)
            {
                // Set the alarm.
                // Note that AlarmType.ElapsedRealtimeWakeup requires a value which is relative to the time since boot.
                // Using elapsed time rather than wall-clock time means the alarm isn't affected by clock changes.
                long alarmTimeMs = SystemClock.ElapsedRealtime() + (long)(alarmTime - DateTime.UtcNow).TotalMilliseconds;
                alarmManager.SetExactAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, alarmTimeMs, pendingIntent);
            }

            Log.Debug("MOCHA", $"Alarm set to {alarmTime} UTC, which is {alarmTime - DateTime.UtcNow} from now");
        }


        /// <summary>
        /// Cancel the countdown alarm, if there is one.
        /// </summary>
        /// <param name="context">Context</param>
        public static void CancelCountdownAlarm(Context context)
        {
            // Cancel the alarm. The pending intent must match the one in SetCountdownAlarm().
            var alarmIntent = new Intent(context, typeof(AlarmReceiver));
            alarmIntent.SetAction(AlarmHelper.AlarmActionName);

            var pendingIntent = PendingIntent.GetBroadcast(context, 0, alarmIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

            using (var alarmManager = (AlarmManager)context.GetSystemService(Context.AlarmService)!)
            {
                alarmManager.Cancel(pendingIntent);
            }

            Log.Debug("MOCHA", $"Alarm cancelled");
        }


        /// <summary>
        /// Get the sound to play for the alarm: the default alarm sound or, if there isn't one, the default
        /// notification sound.
        /// </summary>
        /// <returns>Sound URI, or null if there is no suitable sound.</returns>
        public static Android.Net.Uri? GetAlarmSound()
        {
            var alarmUri = RingtoneManager.GetDefaultUri(RingtoneType.Alarm);
            if (alarmUri == null)
            {
                // If there is no default alarm sound, get the notification sound
                alarmUri = RingtoneManager.GetDefaultUri(RingtoneType.Notification);
            }
            return alarmUri;
        }
    }
}
