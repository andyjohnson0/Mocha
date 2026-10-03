using Android.Runtime;

using AndroidX.Core.App;
using AndroidX.Lifecycle;


namespace Mocha.App
{
    /// <summary>
    /// The application. Creates the notification channels, and tracks whether the app is in the foreground.
    /// </summary>
    [Application]
    public class ThisApp : Android.App.Application, IDefaultLifecycleObserver
    {
        /// <summary>
        /// Notification channel for the "coffee brewing" notification and ongoing activity.
        /// </summary>
        public const string TimerRunningNotificationChannelId = "TimerRunningChannelId";

        /// <summary>
        /// Notification ID for the "coffee brewing" notification and ongoing activity.
        /// </summary>
        public const int TimerRunningNotificationId = 10;

        /// <summary>
        /// Notification channel for the "coffee ready" alarm notification.
        /// </summary>
        public const string AlarmNotificationChannelId = "AlarmNotificationChannelId";

        /// <summary>
        /// Notification ID for the "coffee ready" alarm notification.
        /// </summary>
        public const int AlarmNotificationId = 11;

        /// <summary>
        /// Notification ID for the alarm service's foreground service notification, used on SDK 37 and above. This is
        /// separate from <see cref="AlarmNotificationId"/> so that the "coffee ready" notification can be an ordinary,
        /// non-ongoing notification.
        /// </summary>
        public const int AlarmServiceNotificationId = 12;


        /// <summary>
        /// Constructor, called by the .NET for Android runtime.
        /// </summary>
        public ThisApp(IntPtr handle, JniHandleOwnership ownerShip)
        : base(handle, ownerShip)
        {
            // An Application subclass must have this constructor. Android creates the Java application object, and the
            // .NET for Android runtime then uses this constructor to create the corresponding managed object.
        }


        /// <summary>
        /// Get the application instance.
        /// </summary>
        public static ThisApp Instance()
        {
            return (ThisApp)Application.Context;
        }


        /// <inheritdoc/>
        public override void OnCreate()
        {
            base.OnCreate();

            ProcessLifecycleOwner.Get().Lifecycle.AddObserver(this);

            // Create the notification channels. Creating a channel that already exists does nothing, so changes to a
            // channel's settings (e.g. its importance) here don't affect existing installs unless its ID changes.

            // The builder's setters are bound as returning nullable, so call them as statements rather than chaining.
            var notificationMgr = NotificationManagerCompat.From(this)!;

            // Create notification channel for continuing activity
            var cb = new NotificationChannelCompat.Builder(TimerRunningNotificationChannelId, NotificationManagerCompat.ImportanceDefault);
            cb.SetName(Resources!.GetString(Resource.String.continuingActivityChannelName));
            cb.SetDescription(Resources!.GetString(Resource.String.continuingActivityChannelDescription));
            notificationMgr.CreateNotificationChannel(cb.Build());

            // Create notification channel for the alarm
            cb = new NotificationChannelCompat.Builder(AlarmNotificationChannelId, NotificationManagerCompat.ImportanceHigh);
            cb.SetName(Resources!.GetString(Resource.String.alarmNotificationChannelName));
            cb.SetDescription(Resources!.GetString(Resource.String.alarmNotificationChannelDescription));
            notificationMgr.CreateNotificationChannel(cb.Build());
        }


        /// <summary>
        /// Is the app in the foreground (i.e. is one of its activities visible)?
        /// </summary>
        /// <remarks>
        /// Defaults to false because a process started in the background (e.g. by AlarmReceiver) never
        /// receives a lifecycle event, and must not be treated as foreground.
        /// </remarks>
        public bool IsForeground { get; private set; } = false;


        #region IDefaultLifecycleObserver

        // Use process lifecycle monitoring to note when we are in the foreground or background. ProcessLifecycleOwner
        // dispatches OnStart when the app's first activity starts, and OnStop shortly after its last activity stops.
        // The other IDefaultLifecycleObserver methods have default (empty) implementations.
        //
        // This replaces the deprecated approach of [Export] methods with [OnLifecycleEvent] annotations, which the
        // Lifecycle library called by reflection. That crashed in Release builds with an UnsatisfiedLinkError, because
        // the [Export] methods' native implementations weren't registered.

        /// <summary>
        /// Called when the app moves to the foreground.
        /// </summary>
        public void OnStart(ILifecycleOwner owner)
        {
            this.IsForeground = true;
        }


        /// <summary>
        /// Called when the app moves to the background.
        /// </summary>
        public void OnStop(ILifecycleOwner owner)
        {
            this.IsForeground = false;
        }

        #endregion
    }
}
