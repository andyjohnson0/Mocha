using Android.Content;
using Android.Util;

using AndroidX.Core.App;


namespace Mocha.App
{
    /// <summary>
    /// Handles the user dismissing the alarm notification posted by AlarmReceiver.
    /// This is a receiver rather than an activity so that dismissing the notification doesn't bring the app
    /// to the foreground.
    /// </summary>
    [BroadcastReceiver(Enabled = true, Exported = false)]
    public class AlarmNotificationDeletedReceiver : BroadcastReceiver
    {
        /// <inheritdoc/>
        public override void OnReceive(Context? context, Intent? _)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            Log.Debug("MOCHA", "In AlarmNotificationDeletedReceiver.OnReceive()");

            // Cancel any ongoing activity notification.
            OngoingActivityHelper.Cancel(context);

            // Stop the alarm service, which AlarmReceiver started to provide feedback.
            Mocha.App.AlarmService.Stop(context);
        }
    }
}
