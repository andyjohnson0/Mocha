using Android;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Graphics;
using Android.Content.PM;
using Android.Util;

using AndroidX.AppCompat.App;
using AndroidX.Core.Content;
using AndroidX.Core.App;

using Kilsten.Android;
using Kilsten.WearOS;
using Kilsten.WearOS.Controls;


namespace Mocha.App
{
    /// <summary>
    /// The main screen. Shows the current brewing method and its brew time, starts and stops the countdown, and
    /// sounds the alarm when the countdown ends while the app is in the foreground.
    /// </summary>
    /// <remarks>
    /// When the app is in the background, the alarm is handled by <see cref="AlarmReceiver"/> instead. The app is
    /// then reopened via the alarm notification, which is handled by <see cref="HandleActivityIntent"/>.
    /// </remarks>
    [Activity(Label = "@string/appName", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {

        // Working state. Loaded in OnCreate().
        private readonly ThisViewModel _viewModel = new ThisViewModel();
        private Beverage _beverage = null!;
        private Settings _settings = null!;

        // The timer runs the countdown display.
        // _timerHandler runs OnTimerTick(), which re-posts itself. While the countdown is running, ticks are aligned to
        // the displayed seconds (see GetNextTickDelayMs()). Otherwise they occur every _timerUpdatePeriod.
        private static readonly TimeSpan _timerUpdatePeriod = TimeSpan.FromSeconds(1);
        private readonly Handler _timerHandler = new Handler(Looper.MainLooper!);

        // Debug only: track the current locale so that OnCreate() can detect activity restarts due to locale changes.
#if DEBUG
        private static Java.Util.Locale? _currentLocale = null;
#endif

        // Activity request codes
        private const int _selectMethodReq = 1000;
        private const int _editMethodReq = 1001;
        private const int _addMethodReq = 1002;
        private const int _editBrewTimeReq = 1003;
        private const int _settingsReq = 1004;
        private const int _alarmActReq = 1005;

        // Permissions request codes
        private const int _postNotificationsPermReq = 2000;
        private const int _scheduleExactAlarmsPermReq = 2001;  // Activity request code for the exact alarm settings


        /// <inheritdoc/>
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // Handle the splash screen transition.
            if (this.Intent?.Action == Intent.ActionMain)
            {
                var splashScreen = AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);
            }

            // Theme and content
            SetTheme(Resource.Style.Theme_App);
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.main_activity);

            // Event handlers
            var tv = FindViewById<TextView>(Resource.Id.methodName)!;
            (tv.Parent as View)!.Click += MethodName_Click;
            (tv.Parent as View)!.LongClick += MethodName_LongClick;
            var btn = FindViewById<ImageButton>(Resource.Id.methodMenuBtn)!;
            btn.Click += MethodName_LongClick;
            btn.SetColorFilter(Color.Gray, PorterDuff.Mode.SrcIn);
            tv = FindViewById<TextView>(Resource.Id.timerCountdown)!;
            (tv.Parent as View)!.Click += Timer_Click;
#if DEBUG
            (tv.Parent as View)!.LongClick += Timer_LongClick;
#endif
            btn = FindViewById<ImageButton>(Resource.Id.settingsBtn)!;
            btn.Click += SettingsBtn_Click;
            btn.SetColorFilter(Color.Gray, PorterDuff.Mode.SrcIn);
            btn = FindViewById<ImageButton>(Resource.Id.startStopButton)!;
            btn.Click += StartStopButton_Click;

            // Detect locale change (debug only)
#if DEBUG
            if (MainActivity._currentLocale != null && MainActivity._currentLocale.ToString() != Java.Util.Locale.Default.ToString())
            {
                // The locale has changed. Re-load the methods.
                // We do this only in debug builds to make screenshotting easier.
                Beverage.Reset(this);
                Toast.MakeText(this, $"Locale changed from {MainActivity._currentLocale} to {Java.Util.Locale.Default}", ToastLength.Short)!.Show();
            }
            MainActivity._currentLocale = Java.Util.Locale.Default;
#endif

            // Load data
            this._beverage = Beverage.Load(this);
            this._settings = Settings.Load(this);
            this._viewModel.Load(this);
            if (this._viewModel.AlarmTime < DateTime.UtcNow)
            {
                // Any countdown ended while the activity wasn't running. The alarm was handled by AlarmReceiver.
                this._viewModel.TimerRunning = false;
                this._viewModel.Save(this);
            }

            // Update UI
            SelectMethod(this._viewModel.CurrentMethodIdx);
            UpdateTimerButton();

            // Handle the intent
            HandleActivityIntent(this.Intent);
        }


        /// <inheritdoc/>
        protected override void OnResume()
        {
            base.OnResume();

            // If AlarmReceiver has stopped the timer while we were in the background (because the alarm went off)
            // then pick that up, so that the tick below doesn't trigger the alarm again.
            // Only this change is loaded, rather than all the saved state, so that nothing else that has changed in
            // memory since OnPause() (e.g. in OnActivityResult(), which runs before here) can be overwritten.
            if (this._viewModel.TimerRunning && !ThisViewModel.LoadTimerRunning(this))
            {
                this._viewModel.TimerRunning = false;
                UpdateTimerDisplay();
                UpdateTimerButton();
            }

            // If the alarm went off in the background and is still sounding, and the app has been reopened some way
            // other than the alarm notification (e.g. by tapping the ongoing activity icon), handle it as if the
            // notification had been tapped. A notification tap is handled earlier, in HandleActivityIntent(), which
            // stops the alarm, so this doesn't happen twice. An alarm started by the alarm activity (the foreground
            // case) isn't a background alarm, so returning from the alarm activity doesn't trigger this.
            if (Mocha.App.AlarmService.IsBackgroundAlarmSounding)
            {
                AcknowledgeBackgroundAlarm();
            }

            // Start the timer ticks.
            // Tick now, so that the display is updated immediately rather than at the next tick. This also schedules
            // the following tick.
            OnTimerTick();
        }


        /// <inheritdoc/>
        protected override void OnPause()
        {
            base.OnPause();

            this._viewModel.Save(this);

            // Pause the timer ticks
            this._timerHandler.RemoveCallbacksAndMessages(null);
        }



        /// <summary>
        /// The activity's working state, persisted in shared preferences so that it survives the app being stopped.
        /// </summary>
        /// <remarks>
        /// This is a plain class rather than an AndroidX ViewModel, because it is loaded from shared preferences in
        /// OnCreate() and saved in OnPause(), so a ViewModel's retention across configuration changes adds nothing.
        /// </remarks>
        private sealed class ThisViewModel
        {
            /// <summary>
            /// Index of the current method in <see cref="Beverage.Methods"/>.
            /// </summary>
            public int CurrentMethodIdx { get; set; }

            /// <summary>
            /// Is the countdown running?
            /// Note that in foreground operation the timer handler runs even when this is false.
            /// </summary>
            public bool TimerRunning { get; set; }

            /// <summary>
            /// Alarm time, in UTC. Only meaningful while <see cref="TimerRunning"/> is true.
            /// </summary>
            public DateTime AlarmTime { get; set; }


            /// <summary>
            /// Load the saved state.
            /// </summary>
            public void Load(Context context)
            {
                ArgumentNullException.ThrowIfNull(context, nameof(context));

                using (var prefs = ThisViewModel.GetSharedPreferences(context))
                {
                    this.CurrentMethodIdx = prefs.GetInt(ThisViewModel._currentMethodKey, 0);
                    this.TimerRunning = prefs.GetBoolean(ThisViewModel._timerRunningKey, false);
                    this.AlarmTime = prefs.GetDateTime(ThisViewModel._alarmTimeKey, DateTime.Now);
                }
            }


            /// <summary>
            /// Save the state.
            /// </summary>
            public void Save(Context context)
            {
                ArgumentNullException.ThrowIfNull(context, nameof(context));

                using (var prefs = ThisViewModel.GetSharedPreferences(context))
                {
                    using (var editor = prefs.Edit()!)
                    {
                        editor.Clear();
                        editor.PutInt(ThisViewModel._schemaVersionKey, ThisViewModel._schemaVersionId);
                        editor.PutInt(ThisViewModel._currentMethodKey, this.CurrentMethodIdx);
                        editor.PutBoolean(ThisViewModel._timerRunningKey, this.TimerRunning);
                        editor.PutDateTime(ThisViewModel._alarmTimeKey, this.AlarmTime);
                        editor.Apply();
                    }
                }
            }


            /// <summary>
            /// Load only the saved timer running state.
            /// </summary>
            public static bool LoadTimerRunning(Context context)
            {
                using (var prefs = ThisViewModel.GetSharedPreferences(context))
                {
                    return prefs.GetBoolean(ThisViewModel._timerRunningKey, false);
                }
            }


            /// <summary>
            /// Save the timer as stopped, leaving the other saved state unchanged.
            /// </summary>
            public static void SaveTimerStopped(Context context)
            {
                using (var prefs = ThisViewModel.GetSharedPreferences(context))
                {
                    using (var editor = prefs.Edit()!)
                    {
                        editor.PutBoolean(ThisViewModel._timerRunningKey, false);
                        editor.Apply();
                    }
                }
            }


            private const string _schemaVersionKey = "_schema_version";
            private const int _schemaVersionId = 1;

            private const string _currentMethodKey = "currentMethodKey";
            private const string _timerRunningKey = "timerRunningKey";
            private const string _alarmTimeKey = "alarmTimeKey";


            private static ISharedPreferences GetSharedPreferences(Context context)
            {
                return context.GetSharedPreferences(context.PackageName + "MainActivityViewModelPrefs", FileCreationMode.Private)!;
            }
        }


        /// <summary>
        /// Record that the timer has stopped because its alarm went off in the background.
        /// Called by AlarmReceiver. This stops the timer tick from triggering the alarm again when the app is next
        /// foregrounded, which it would otherwise do once the alarm service had stopped.
        /// </summary>
        internal static void SaveTimerStopped(Context context)
        {
            ThisViewModel.SaveTimerStopped(context);
        }


        /// <inheritdoc/>
        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            HandleActivityIntent(intent);
        }


        /// <summary>
        /// Handle an intent invoking this activity, from <see cref="OnCreate"/> or <see cref="OnNewIntent"/>.
        /// Intents other than the alarm notification's (e.g. launching the app) need no handling.
        /// </summary>
        /// <param name="intent">Intent</param>
        private void HandleActivityIntent(Intent? intent)
        {
            if (intent == null || intent.Action == Intent.ActionMain)
            {
                return;
            }

            // We arrive here via the alarm notification posted when the alarm went off in the background. If the intent
            // action is AlarmReceiver.AlarmNotificationActionName then the user tapped the notification, and the alarm
            // service may still be sounding the alarm.
            // If the user instead dismisses the notification then it is handled by AlarmNotificationDeletedReceiver.
            if (intent.Action == AlarmReceiver.AlarmNotificationActionName)
            {
                // User opened the background notification ("open on watch")
                AcknowledgeBackgroundAlarm();
            }
        }


        /// <summary>
        /// Handle the user returning to the app after the alarm went off in the background: stop the alarm, stop the
        /// timer, remove the notifications, and show the "coffee is ready" screen without sounding the alarm again.
        /// </summary>
        /// <remarks>
        /// Called when the user taps the alarm notification, or reopens the app some other way (e.g. by tapping the
        /// ongoing activity icon, or from the launcher) while the alarm is still sounding.
        /// </remarks>
        private void AcknowledgeBackgroundAlarm()
        {
            // Cancel any ongoing activity notification.
            OngoingActivityHelper.Cancel(this);

            // Cancel the alarm notification.
            NotificationManagerCompat.From(this)!.Cancel(ThisApp.AlarmNotificationId);

            // The timer is over. Stop it here too, so that the timer tick doesn't trigger the alarm again.
            // The display is updated in OnActivityResult() when the alarm activity finishes.
            this._viewModel.TimerRunning = false;
            this._viewModel.Save(this);

            // Stop the alarm service, which the alarm receiver started to provide feedback.
            Mocha.App.AlarmService.Stop(this);

            // Start the alarm activity with no feedback
            var activityIntent = new Intent(this, typeof(AlarmActivity));
            activityIntent.PutExtra(AlarmActivity.PlaySoundKey, false);
            activityIntent.PutExtra(AlarmActivity.VibrateKey, false);
            StartActivityForResult(activityIntent, _alarmActReq);
        }



        /// <summary>
        /// Handles the results of the activities started by this one, identified by their request codes.
        /// </summary>
        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
        {
            // The activities started for these request codes always return data with Result.Ok, hence the data!.
            if (requestCode == _selectMethodReq && resultCode == Result.Ok)
            {
                var methodIdx = data!.GetIntExtra(SelectMethodActivity.MethodPositionKey, 0);
                SelectMethod(methodIdx);
            }
            else if (requestCode == _editMethodReq && resultCode == Result.Ok)
            {
                var newMethodName = data!.GetStringExtra(EditMethodNameActivity.MethodNameKey)!;
                var methodIdx = this._beverage.RenameMethod(this._viewModel.CurrentMethodIdx, newMethodName);
                this._beverage.Save(this);
                SelectMethod(methodIdx);
            }
            else if (requestCode == _addMethodReq && resultCode == Result.Ok)
            {
                // A new method's brew time starts at one minute.
                var newMethodName = data!.GetStringExtra(EditMethodNameActivity.MethodNameKey)!;
                this._viewModel.CurrentMethodIdx = this._beverage.AddMethod(newMethodName, new TimeSpan(0, 1, 0));
                this._beverage.Save(this);
                SelectMethod(this._viewModel.CurrentMethodIdx);
            }
            else if (requestCode == _editBrewTimeReq && resultCode == Result.Ok)
            {
                var brewTime = TimeSpan.FromSeconds(data!.GetIntExtra(EditBrewTimeActivity.MethodBrewTimeKey, 0));
                this._beverage.Methods[this._viewModel.CurrentMethodIdx].BrewTime = brewTime;
                this._beverage.Save(this);
                UpdateTimerDisplay();
                UpdateTimerButton();
            }
            else if (requestCode == _settingsReq && resultCode == Result.Ok)
            {
                this._settings.AlarmPlaySound = data!.GetBooleanExtra(SettingsActivity.AlarmPlaySoundKey, false);
                this._settings.AlarmVibrate = data!.GetBooleanExtra(SettingsActivity.AlarmVibrateKey, false);
                _settings.Save(this);
            }
            else if (requestCode == _scheduleExactAlarmsPermReq)
            {
                // Returning from the exact alarm settings. The result code doesn't indicate whether access was allowed,
                // so check again. If it was, start the timer.
                if (CanScheduleExactAlarms() && !_viewModel.TimerRunning)
                {
                    FindViewById<ImageButton>(Resource.Id.startStopButton)!.PerformClick();
                }
            }
            else if (requestCode == _alarmActReq)
            {
                // We're returning back from the alarm activity. Get the display ready for another go.
                UpdateTimerDisplay();
                UpdateTimerButton();
            }
            else
            {
                base.OnActivityResult(requestCode, resultCode, data);
            }
        }


        #region Permissions

        /// <summary>
        /// Handles the result of requesting the post notifications permission. If granted, starts the timer.
        /// </summary>
        public override void OnRequestPermissionsResult(
            int requestCode,
            string[] permissions,
            [GeneratedEnum] Permission[] grantResults)
        {
            if (requestCode == _postNotificationsPermReq)
            {
                // POST_NOTIFICATIONS
                if ((grantResults.Length > 0) && grantResults[0] == Permission.Granted)
                {
                    // Permission granted.
                    FindViewById<ImageButton>(Resource.Id.startStopButton)!.PerformClick();
                }
                else
                {
                    // Permission not granted.
                    if (this.ShouldShowRequestPermissionRationale(permissions[0]))
                    {
                        // The permission was denied without "don't ask again"

                        // Explain to the user why we want the permission.
                        var adb = new WearableAlertDialog.Builder(this);
                        adb.SetCancelable(true);
                        adb.SetTitle(Resource.String.postNotificationsPermissionPrompt);
                        adb.SetMessage(Resource.String.postNotificationsPermissionExplainMsg);
                        adb.SetPositiveButton();
                        adb.Create().Show();
                    }
                    else
                    {
                        // The permission has been permanently denied

                        // Prompt the user to enable the permission in the app settings
                        var adb = new WearableAlertDialog.Builder(this);
                        adb.SetCancelable(true);
                        adb.SetTitle(Resource.String.postNotificationsPermissionPrompt);
                        adb.SetMessage(Resource.String.postNotificationsPermissionEnableMsg);
                        adb.SetPositiveButton(handler: (sender, args) =>
                        {
                            var intent = new Intent(Android.Provider.Settings.ActionApplicationDetailsSettings);
                            intent.SetData(Android.Net.Uri.FromParts("package", this.PackageName, null));
                            StartActivity(intent);
                        });
                        adb.SetNegativeButton();
                        adb.Create().Show();
                    }
                }
            }
            else
            {
                base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            }
        }


        /// <summary>
        /// Check that the app can post notifications, which it needs to tell the user that their coffee is ready.
        /// If it can't then request the permission, explaining why first if appropriate.
        /// </summary>
        /// <returns>
        /// True if notifications are allowed. False if the permission is being requested, in which case
        /// <see cref="OnRequestPermissionsResult"/> is called with the outcome.
        /// </returns>
        private bool CheckHasNotificationPermission()
        {
            // Check for POST_NOTIFICATION permission. This is required on Wear OS 4 where notifications are disabled
            // by default. See https://developer.android.com/training/wearables/versions/4/changes#notification-permission

            // First check if notifications are enabled.
            // If notifications are enabled then we don't need to do anything more. This is the default on Wear OS 3, and
            // on Wear OS 4 it indicates that we have already acquired permission.
            using (var notificationMgr = (NotificationManager)this.GetSystemService(Context.NotificationService)!)
            {
                if (notificationMgr.AreNotificationsEnabled())
                {
                    return true;
                }
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                // SDK 33 and above
                var permissions = new string[]
                {
#pragma warning disable CA1416
                    Manifest.Permission.PostNotifications
#pragma warning restore CA1416
                };

                // If we get to here then we need to ask for permission.
                if (this.CheckSelfPermission(permissions[0]) != Permission.Granted)
                {
                    if (this.ShouldShowRequestPermissionRationale(permissions[0]))
                    {
                        // Explain to the user why we want the permission.
                        var adb = new WearableAlertDialog.Builder(this);
                        adb.SetCancelable(true);
                        adb.SetTitle(Resource.String.postNotificationsPermissionPrompt);
                        adb.SetMessage(Resource.String.postNotificationsPermissionRequestMsg);
                        adb.SetPositiveButton(handler: (sender, args) =>
                        {
                            // Request permission. This calls OnRequestPermissionsResult() with grant/deny.
                            this.RequestPermissions(permissions, _postNotificationsPermReq);
                        });
                        adb.SetNegativeButton();
                        adb.Create().Show();
                        return false;
                    }
                    else
                    {
                        // No need to explain. Just acquire the permission.
                        // System uses its own UI and calls OnRequestPermissionsResult() with grant/deny.
                        this.RequestPermissions(permissions, _postNotificationsPermReq);
                        return false;
                    }
                }
            }

            return true;
        }


        /// <summary>
        /// Check that the app can schedule exact alarms. If it can't then explain why it needs to, and offer to open
        /// the system settings to allow it.
        /// </summary>
        /// <returns>
        /// True if exact alarms are allowed. False if not, in which case <see cref="OnActivityResult"/> checks again
        /// when the user returns from the settings.
        /// </returns>
        private bool CheckHasScheduleAlarmPermission()
        {
            // Check for SCHEDULE_EXACT_ALARM permission. This is required when calling AlarmManager.SetExactAndAllowWhileIdle(),
            // which we do in AlarmHelper.SetCountdownAlarm(). Without it, that call throws a SecurityException.
            //
            // This is not a runtime permission: it is not granted by default to fresh installs on SDK 33 and above, and it
            // can't be acquired with RequestPermissions(). CheckSelfPermission() is also unreliable for it, as it reports
            // the manifest declaration rather than whether the access is actually allowed.
            // See https://developer.android.com/develop/background-work/services/alarms/schedule
            if (CanScheduleExactAlarms())
            {
                return true;
            }

            // Explain to the user why we want the permission, and send them to the system settings to allow it.
            // OnActivityResult() re-checks when they return.
            var adb = new WearableAlertDialog.Builder(this);
            adb.SetCancelable(true);
            adb.SetTitle(Resource.String.postScheduleExactAlarmsPermissionPrompt);
            adb.SetMessage(Resource.String.postScheduleExactAlarmsPermissionRequestMsg);
            adb.SetPositiveButton(handler: (sender, args) => { OpenScheduleExactAlarmSettings(); });
            adb.SetNegativeButton();
            adb.Create().Show();
            return false;
        }


        /// <summary>
        /// Can the app schedule exact alarms?
        /// </summary>
        private bool CanScheduleExactAlarms()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                // SDK 31 and above
#pragma warning disable CA1416
                using (var alarmManager = (AlarmManager)this.GetSystemService(Context.AlarmService)!)
                {
                    return alarmManager.CanScheduleExactAlarms();
                }
#pragma warning restore CA1416
            }
            else
            {
                // For SDK 30 and below we don't need permission.
                return true;
            }
        }


        /// <summary>
        /// Open the system settings page for allowing this app to schedule exact alarms.
        /// </summary>
        private void OpenScheduleExactAlarmSettings()
        {
            // The "Alarms & reminders" settings page may not exist on all Wear OS devices, so fall back to the
            // app details settings.
            var actions = new string[]
            {
#pragma warning disable CA1416
                Android.Provider.Settings.ActionRequestScheduleExactAlarm,   // best
#pragma warning restore CA1416
                Android.Provider.Settings.ActionApplicationDetailsSettings   // fallback
            };
            foreach (var action in actions)
            {
                var intent = new Intent(action);
                intent.SetData(Android.Net.Uri.FromParts("package", this.PackageName, null));
                if (intent.ResolveActivity(this.PackageManager!) != null)
                {
                    StartActivityForResult(intent, _scheduleExactAlarmsPermReq);
                    return;
                }
            }

            // If we get to here then we failed to invoke the settings with any of the above actions.
            Toast.MakeText(this, Resource.String.openSettingsErrorMsg, ToastLength.Short)!.Show();
        }

        #endregion Permissions


        /// <summary>
        /// Make a method the current one, and display it.
        /// </summary>
        /// <param name="idx">Method index. Clamped to the valid range.</param>
        private void SelectMethod(int idx)
        {
            // Clamp the index, which can be out of range after the last method is deleted or the methods are reset.
            idx = Math.Clamp(idx, 0, this._beverage.Methods.Length - 1);

            // Save the current method index. Saving writes all the view model's state, including whether the timer is
            // running. That's safe because OnCreate() calls this just after loading the state, and the other callers
            // can't run during a countdown (the method controls are ignored then). Callers that change the methods
            // save them before calling this.
            this._viewModel.CurrentMethodIdx = idx;
            this._viewModel.Save(this);

            var methodNameTv = FindViewById<TextView>(Resource.Id.methodName)!;
            methodNameTv.Text = _beverage.Methods[idx].Name;
            var timerTv = FindViewById<TextView>(Resource.Id.timerCountdown)!;
            timerTv.Text = Beverage.FormatTime(_beverage.Methods[this._viewModel.CurrentMethodIdx].BrewTime);

            UpdateTimerButton();
        }


        /// <summary>
        /// Start/stop button handler. Stops the countdown if it is running. Otherwise checks for the necessary
        /// permissions and starts it.
        /// </summary>
        private void StartStopButton_Click(object? sender, EventArgs e)
        {
            if (_viewModel.TimerRunning)
            {
                // Stop
                CancelAlarmAndOngoingActivity();
                StopResetTimer();
                UpdateTimerDisplay();
            }
            else
            {
                // A zero brew time can't be started. The button is disabled in this case, but we can still get
                // here via PerformClick() after a permission request, which ignores the enabled state.
                if (!CanStartTimer())
                {
                    return;
                }

                // Check permissions
                if (!CheckHasNotificationPermission())
                {
                    return;
                }
                if (!CheckHasScheduleAlarmPermission())
                {
                    return;
                }

                // Start
                StartTimer();
                SetAlarm();
            }

            UpdateTimerButton();
        }


        /// <summary>
        /// Method name click handler. Opens the method list to choose a different method.
        /// </summary>
        private void MethodName_Click(object? sender, EventArgs e)
        {
            if (_viewModel.TimerRunning)
                return;

            var intent = new Intent(this, typeof(SelectMethodActivity));
            intent.PutExtra(SelectMethodActivity.MethodPositionKey, this._viewModel.CurrentMethodIdx);
            StartActivityForResult(intent, _selectMethodReq);
        }


        /// <summary>
        /// Method name long-press and menu button handler. Shows the menu for renaming, deleting and adding methods.
        /// </summary>
        private void MethodName_LongClick(object? sender, EventArgs e)
        {
            if (_viewModel.TimerRunning)
                return;

            var dialogView = LayoutInflater.From(this)!.Inflate(Resource.Layout.method_context_menu, null)!;

            var contextDlg = new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.Theme_App_ContextDialog)
                .SetView(dialogView)!
                .Create()!;
            var btn = dialogView.FindViewById<ImageButton>(Resource.Id.editBtn)!;
            btn.Click += (o, e) =>
            {
                contextDlg.Dismiss();
                var intent = new Intent(this, typeof(EditMethodNameActivity));
                intent.PutExtra(EditMethodNameActivity.MethodNameKey, this._beverage.Methods[this._viewModel.CurrentMethodIdx].Name);
                // Exclude the method being renamed, so that keeping its current name is allowed.
                intent.PutExtra(EditMethodNameActivity.MethodNamesInUseKey, this._beverage.GetMethodNames(excludeIdx: this._viewModel.CurrentMethodIdx));
                StartActivityForResult(intent, _editMethodReq);
            };
            btn = dialogView.FindViewById<ImageButton>(Resource.Id.deleteBtn)!;
            btn.Enabled = this._beverage.Methods.Length > 1;
            btn.Click += (o, e) =>
            {
                contextDlg.Dismiss();

                Toast.MakeText(this, Resource.String.deletedMsg, ToastLength.Short)!.Show();

                this._beverage.DeleteMethod(this._viewModel.CurrentMethodIdx);
                this._beverage.Save(this);
                SelectMethod(this._viewModel.CurrentMethodIdx);
            };
            btn = dialogView.FindViewById<ImageButton>(Resource.Id.addBtn)!;
            btn.Click += (o, e) =>
            {
                contextDlg.Dismiss();
                var intent = new Intent(this, typeof(EditMethodNameActivity));
                intent.PutExtra(EditMethodNameActivity.MethodNameKey, Resources!.GetString(Resource.String.newMethodName));
                intent.PutExtra(EditMethodNameActivity.MethodNamesInUseKey, this._beverage.GetMethodNames());
                this.StartActivityForResult(intent, _addMethodReq);
            };
            contextDlg.Show();
        }


        /// <summary>
        /// Timer click handler. Opens the brew time editor for the current method.
        /// </summary>
        private void Timer_Click(object? sender, EventArgs e)
        {
            if (_viewModel.TimerRunning)
                return;

            var intent = new Intent(this, typeof(EditBrewTimeActivity));
            intent.PutExtra(EditBrewTimeActivity.MethodNameKey, this._beverage.Methods[this._viewModel.CurrentMethodIdx].Name);
            intent.PutExtra(EditBrewTimeActivity.MethodBrewTimeKey, (int)this._beverage.Methods[this._viewModel.CurrentMethodIdx].BrewTime.TotalSeconds);
            StartActivityForResult(intent, _editBrewTimeReq);
        }


#if DEBUG

        /// <summary>
        /// Timer long-press handler (debug builds only). Resets the methods to the defaults.
        /// </summary>
        private void Timer_LongClick(object? sender, EventArgs e)
        {
            if (_viewModel.TimerRunning)
                return;

            Beverage.Reset(this);
            this._beverage = Beverage.Load(this);
            this.SelectMethod(this._viewModel.CurrentMethodIdx);
            this.UpdateTimerDisplay();
            Toast.MakeText(this, $"Display reset", ToastLength.Short)!.Show();
        }

#endif


        /// <summary>
        /// Settings button handler. Opens the settings screen.
        /// </summary>
        private void SettingsBtn_Click(object? sender, EventArgs e)
        {
            var intent = new Intent(this, typeof(SettingsActivity));
            intent.PutExtra(SettingsActivity.AlarmPlaySoundKey, this._settings.AlarmPlaySound);
            intent.PutExtra(SettingsActivity.AlarmVibrateKey, this._settings.AlarmVibrate);
            StartActivityForResult(intent, _settingsReq);
        }


        #region Timer

        /// <summary>
        /// Timer tick. Schedules the next tick, updates the countdown display, and sounds the alarm when the
        /// countdown ends.
        /// </summary>
        private void OnTimerTick()
        {
            _timerHandler.PostDelayed(OnTimerTick, GetNextTickDelayMs());

            if (this._viewModel.TimerRunning)
            {
                Log.Debug("MOCHA", $"OnTimerTick({DateTime.UtcNow}): Alarm in {this._viewModel.AlarmTime - DateTime.UtcNow} at {this._viewModel.AlarmTime}");

                UpdateTimerDisplay();

                // If we're at the alarm time then trigger the alarm in the foreground.
                // The alarm receiver will have detected that the app is in the foreground and will have masked the alarm.
                if (DateTime.UtcNow >= this._viewModel.AlarmTime)
                {
                    Log.Debug("MOCHA", $"Alarm in foreground");

                    // Stop the timer.
                    // To avoid janky display updates before we immediately transition to the alarm activity (below) we don't
                    // also call UpdateTimerDisplay() or UpdateTimerButton() here. Instead this is done in OnActivityResult()
                    // when we return from the alarm activity.
                    CancelAlarmAndOngoingActivity();
                    StopResetTimer();

                    // Cancel any alarm notification that might have been created prior to the app being foregrounded.
                    // This doesn't call its delete intent.
                    NotificationManagerCompat.From(this)!.Cancel(ThisApp.AlarmNotificationId);

                    // Start the alarm activity.
                    // If the alarm service is already running then we stop it and tell the alarm activity _not_ to start it.
                    // This is a safeguard for the app being foregrounded after the alarm receiver has started the alarm
                    // service. That should now be rare, because the alarm receiver also marks the timer as stopped, which
                    // OnResume() picks up, so the countdown doesn't reach here.
                    var settings = Settings.Load(this);
                    if (Mocha.App.AlarmService.IsRunning)
                    {
                        // Alarm service already started by alarm receiver.
                        Mocha.App.AlarmService.Stop(this);
                        settings.AlarmPlaySound = settings.AlarmVibrate = false;
                    }
                    // Start alarm activity for visual feedback.
                    var intent = new Intent(this, typeof(AlarmActivity))
                        .PutExtra(AlarmActivity.PlaySoundKey, settings.AlarmPlaySound)
                        .PutExtra(AlarmActivity.VibrateKey, settings.AlarmVibrate);
                    this.StartActivityForResult(intent, _alarmActReq);
                }
            }
        }


        /// <summary>
        /// Get the delay until the next timer tick.
        /// While the countdown is running, ticks are aligned to the points where the displayed (whole second) time
        /// remaining changes, so that the final tick occurs at the alarm time rather than up to a tick period later.
        /// </summary>
        private long GetNextTickDelayMs()
        {
            if (this._viewModel.TimerRunning)
            {
                var remainingMs = (this._viewModel.AlarmTime - DateTime.UtcNow).TotalMilliseconds;
                if (remainingMs > 0)
                {
                    // Time until the remaining time next crosses a whole second. At least 1ms, so that if we're on a
                    // boundary we tick again just after it.
                    return Math.Max(1L, (long)Math.Ceiling(remainingMs % 1000));
                }
            }
            return (long)_timerUpdatePeriod.TotalMilliseconds;
        }


        /// <summary>
        /// Start the countdown, setting the alarm time from the current method's brew time.
        /// </summary>
        private void StartTimer()
        {
#if DEBUG
            if (this._viewModel.TimerRunning)
                throw new Exception("Timer already running");
#endif

            this._viewModel.TimerRunning = true;
            this._viewModel.AlarmTime = DateTime.UtcNow + this._beverage.Methods[this._viewModel.CurrentMethodIdx].BrewTime;
            this._viewModel.Save(this);
            UpdateTimerDisplay();
        }


        /// <summary>
        /// Set the system alarm for the alarm time, and show the brewing notification and ongoing activity.
        /// </summary>
        private void SetAlarm()
        {
            AlarmHelper.SetCountdownAlarm(this, this._viewModel.AlarmTime);
            OngoingActivityHelper.Create(this, _viewModel.AlarmTime, this._beverage.Methods[this._viewModel.CurrentMethodIdx].Name);
        }


        /// <summary>
        /// Stop the countdown.
        /// </summary>
        private void StopResetTimer()
        {
#if DEBUG
            if (!this._viewModel.TimerRunning)
                throw new Exception("Timer already stopped");
#endif

            this._viewModel.TimerRunning = false;
            this._viewModel.Save(this);
        }


        /// <summary>
        /// Cancel the system alarm, and remove the brewing notification and ongoing activity.
        /// </summary>
        private void CancelAlarmAndOngoingActivity()
        {
            AlarmHelper.CancelCountdownAlarm(this);
            OngoingActivityHelper.Cancel(this);
        }


        /// <summary>
        /// Update the timer display: the time remaining if the countdown is running, otherwise the brew time.
        /// </summary>
        private void UpdateTimerDisplay()
        {
            var timeRemaining = this._viewModel.TimerRunning ? this._viewModel.AlarmTime - DateTime.UtcNow : this._beverage.Methods[this._viewModel.CurrentMethodIdx].BrewTime;
            if (timeRemaining < TimeSpan.Zero)
                timeRemaining = TimeSpan.Zero;

            // Round up to a whole second, as countdowns conventionally do, so that 0:00 is only shown when the
            // time is up and a started countdown initially shows the full brew time.
            timeRemaining = TimeSpan.FromSeconds(Math.Ceiling(timeRemaining.TotalSeconds));

            var timerTv = FindViewById<TextView>(Resource.Id.timerCountdown)!;
            timerTv.Text = Beverage.FormatTime(timeRemaining);
        }


        /// <summary>
        /// Update the start/stop button's icon and enabled state.
        /// </summary>
        private void UpdateTimerButton()
        {
            var btn = FindViewById<ImageButton>(Resource.Id.startStopButton)!;
            if (_viewModel.TimerRunning)
            {
                btn.SetImageDrawable(ContextCompat.GetDrawable(this, Resource.Drawable.ic_stop));
            }
            else
            {
                btn.SetImageDrawable(ContextCompat.GetDrawable(this, Resource.Drawable.ic_play_arrow));
            }

            // The button's background has no disabled state, so dim it to show that it is disabled.
            btn.Enabled = _viewModel.TimerRunning || CanStartTimer();
            btn.Alpha = btn.Enabled ? 1.0f : 0.38f;
        }


        /// <summary>
        /// Can the countdown be started? Not if the current method's brew time is zero.
        /// </summary>
        private bool CanStartTimer()
        {
            return this._beverage.Methods[this._viewModel.CurrentMethodIdx].BrewTime > TimeSpan.Zero;
        }

        #endregion Timer
    }
}
