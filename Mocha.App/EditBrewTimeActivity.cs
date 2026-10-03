using Android.Content;

using AndroidX.AppCompat.App;

namespace Mocha.App
{
    /// <summary>
    /// Screen for editing a brewing method's brew time.
    /// Started for a result. On OK, returns the new brew time.
    /// </summary>
    [Activity]
    public class EditBrewTimeActivity : AppCompatActivity
    {
        /// <summary>Intent extra key (string, in): the method name, for display.</summary>
        public const string MethodNameKey = "MethodNameKey";

        /// <summary>Intent extra key (int, in and out): the brew time, in seconds.</summary>
        public const string MethodBrewTimeKey = "MethodBrewTimeKey";

        // Amount that the increment and decrement buttons change the brew time by.
        private const int _deltaSecs = 10;


        private string? _methodName;
        private TimeSpan _brewTime;

        /// <inheritdoc/>
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            SetTheme(Resource.Style.Theme_App);
            base.OnCreate(savedInstanceState);

            // Get the method details.
            this._methodName = this.Intent!.Extras!.GetString(MethodNameKey, "");
            this._brewTime = CapBrewTime(TimeSpan.FromSeconds(this.Intent!.Extras!.GetInt(MethodBrewTimeKey, 0)));

            // Set up the UI.
            SetContentView(Resource.Layout.edit_brew_time_activity);
            Refresh();
            var btn = FindViewById<ImageButton>(Resource.Id.incrButton)!;
            btn.Click += IncrBtn_Click;
            btn.LongClick += IncrBtn_LongClick;
            btn = FindViewById<ImageButton>(Resource.Id.decrButton)!;
            btn.Click += DecrBtn_Click;
            btn.LongClick += DecrBtn_LongClick;
            btn = FindViewById<ImageButton>(Resource.Id.okBtn)!;
            btn.Click += OkBtn_Click;
        }



        /// <summary>
        /// Increment button handler. Increases the brew time by <see cref="_deltaSecs"/>.
        /// </summary>
        private void IncrBtn_Click(object? sender, EventArgs e)
        {
            this._brewTime = CapBrewTime(this._brewTime.Add(TimeSpan.FromSeconds(_deltaSecs)));
            Refresh();
        }


        /// <summary>
        /// Increment button long-press handler. Increases the brew time to the next whole minute.
        /// </summary>
        private void IncrBtn_LongClick(object? sender, Android.Views.View.LongClickEventArgs e)
        {
            var mins = (int)this._brewTime.TotalMinutes + 1;
            this._brewTime = CapBrewTime(TimeSpan.FromMinutes(mins));
            Refresh();
        }


        /// <summary>
        /// Limit a brew time to <see cref="Method.MaxBrewTime"/>.
        /// </summary>
        private static TimeSpan CapBrewTime(TimeSpan brewTime)
        {
            return brewTime > Method.MaxBrewTime ? Method.MaxBrewTime : brewTime;
        }


        /// <summary>
        /// Decrement button handler. Decreases the brew time by <see cref="_deltaSecs"/>, to a minimum of zero.
        /// </summary>
        private void DecrBtn_Click(object? sender, EventArgs e)
        {
            this._brewTime = this._brewTime.Subtract(TimeSpan.FromSeconds(_deltaSecs));
            if (this._brewTime.TotalSeconds < 0)
            {
                this._brewTime = TimeSpan.Zero;
            }
            Refresh();
        }


        /// <summary>
        /// Decrement button long-press handler. Decreases the brew time to the previous whole minute, to a minimum
        /// of zero.
        /// </summary>
        private void DecrBtn_LongClick(object? sender, Android.Views.View.LongClickEventArgs e)
        {
            // If the time is a whole number of minutes then go down a minute. Otherwise drop the seconds, which takes
            // it down to the whole minute below.
            var mins = this._brewTime.Seconds == 0 ? (int)this._brewTime.TotalMinutes - 1 : Math.Round(this._brewTime.TotalMinutes, MidpointRounding.ToZero);
            if (mins > 0)
                this._brewTime = TimeSpan.FromMinutes(mins);
            else
                this._brewTime = TimeSpan.Zero;
            Refresh();
        }


        /// <summary>
        /// Update the display.
        /// </summary>
        private void Refresh()
        {
            FindViewById<TextView>(Resource.Id.methodName)!.Text = this._methodName;
            FindViewById<TextView>(Resource.Id.brewTime)!.Text = Beverage.FormatTime(this._brewTime);
        }


        /// <summary>
        /// OK button handler. Returns the brew time as the activity result.
        /// </summary>
        private void OkBtn_Click(object? sender, EventArgs e)
        {
            var intent = new Intent();
            intent.PutExtra(MethodBrewTimeKey, (int)this._brewTime.TotalSeconds);
            SetResult(Result.Ok, intent);
            this.Finish();
        }
    }
}
