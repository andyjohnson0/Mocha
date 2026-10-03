using Android.Content;
using Android.Views;

using AndroidX.RecyclerView.Widget;
using AndroidX.Wear.Widget;


namespace Mocha.App
{
    /// <summary>
    /// Screen for choosing a brewing method from a scrolling list.
    /// Started for a result. When a method is tapped, returns its index.
    /// </summary>
    [Activity]
    public class SelectMethodActivity : Activity
    {
        /// <summary>Intent extra key (int, in and out): index of the method. In, the list scrolls to it.</summary>
        public const string MethodPositionKey = "MethodPositionKey";

        /// <inheritdoc/>
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            SetTheme(Resource.Style.Theme_App);
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.select_method_activity);

            // Set up the list. Edge item centering suits a round watch screen.
            var rv = FindViewById<WearableRecyclerView>(Resource.Id.method_list)!;
            rv.EdgeItemsCenteringEnabled = true;
            rv.SetLayoutManager(new WearableLinearLayoutManager(this) { SmoothScrollbarEnabled = true});
            var adapter = new CoffeeListAdapter(this);
            rv.SetAdapter(adapter);

            // Scroll to the current method.
            var methodIdx = this.Intent!.Extras!.GetInt(MethodPositionKey, 0);
            rv.SmoothScrollToPosition(methodIdx);
        }


        /// <summary>
        /// List item click handler. Returns the method's index as the activity result.
        /// </summary>
        private void Item_Click(object? sender, CoffeeItemClickEventArgs ea)
        {
            var intent = new Intent();
            intent.PutExtra(MethodPositionKey, ea.Position);
            SetResult(Result.Ok, intent);
            Finish();
        }


        /// <summary>
        /// List adapter, providing an item for each brewing method.
        /// </summary>
        public class CoffeeListAdapter : RecyclerView.Adapter
        {
            /// <summary>
            /// Constructor. Loads the methods.
            /// </summary>
            /// <param name="context">The <see cref="SelectMethodActivity"/>, which handles item clicks.</param>
            public CoffeeListAdapter(Context context)
            {
                this._context = context;
                this._coffee = Beverage.Load(this._context);
            }

            private readonly Context _context;
            private readonly Beverage _coffee;


            /// <inheritdoc/>
            public override int ItemCount
            {
                get
                {
                    return this._coffee.Methods.Length;
                }
            }


            /// <inheritdoc/>
            public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
            {
                var v = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.method_list_item, parent, false)!;
                return new CoffeeItemViewHolder(v);
            }


            /// <inheritdoc/>
            public override void OnBindViewHolder(RecyclerView.ViewHolder? holder, int position)
            {
                if (holder == null)
                    throw new ArgumentNullException(nameof(holder));

                var vh = (CoffeeItemViewHolder)holder;

                // Reset the view holder's click event, because the RecyclerView reuses view holders, so it may
                // already have a handler from a previous binding.
                vh.ResetClickHandler();
                vh.Click += (_context as SelectMethodActivity)!.Item_Click;

                // Show the method.
                var txt = vh.ItemView.FindViewById<TextView>(Resource.Id.methodName)!;
                txt.Text = this._coffee.Methods[position].Name;
                txt = vh.ItemView.FindViewById<TextView>(Resource.Id.methodBrewTime)!;
                txt.Text = Beverage.FormatTime(this._coffee.Methods[position].BrewTime);
            }
        }


        /// <summary>
        /// View holder for a list item. Raises <see cref="Click"/> when the item is tapped.
        /// </summary>
        public class CoffeeItemViewHolder : RecyclerView.ViewHolder
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="view">The item's view.</param>
            public CoffeeItemViewHolder(View view)
                : base(view)
            {
                this.ItemView = view;
                this.ItemView.Click += (o, e) =>
                {
                    // BindingAdapterPosition is the item's position at the time of the click, which is correct even
                    // though view holders are reused.
                    this.Click?.Invoke(this, new CoffeeItemClickEventArgs() { Position = this.BindingAdapterPosition });
                };
            }


            /// <summary>
            /// Raised when the item is tapped.
            /// </summary>
            public event EventHandler<CoffeeItemClickEventArgs>? Click = null;


            /// <summary>
            /// Remove any <see cref="Click"/> handlers.
            /// </summary>
            public void ResetClickHandler()
            {
                this.Click = null;
            }
        }



        /// <summary>
        /// Event arguments for a list item click.
        /// </summary>
        public class CoffeeItemClickEventArgs : EventArgs
        {
            /// <summary>Index of the item, which is also the index of its method.</summary>
            public required int Position { get; init; }
        }
    }
}
