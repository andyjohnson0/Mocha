using Android.Content.PM;
using Android.Views;

using Kilsten.WearOS;


namespace Mocha.App
{
    /// <summary>
    /// The About screen, showing the app version and author, with a link to the author's website.
    /// </summary>
    [Activity]
    public class AboutActivity : Activity
    {
        /// <inheritdoc/>
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            SetTheme(Resource.Style.Theme_App);
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.about_activity);

            this.FindViewById<ImageView>(Resource.Id.icon)!.Click += async (o, e) => await OpenVendorUrlAsync();
#pragma warning disable CS0618 // Type or member is obsolete
            var pkgInfo = this.PackageManager!.GetPackageInfo(this.PackageName!, PackageInfoFlags.MetaData);
#pragma warning restore CS0618 // Type or member is obsolete
            this.FindViewById<TextView>(Resource.Id.subTitle)!.Text = Resources!.GetString(Resource.String.appVersion, pkgInfo!.VersionName!)!;
            this.FindViewById<TextView>(Resource.Id.info)!.Text = Resources.GetString(Resource.String.appCopyright, Resources.GetString(Resource.String.vendorName)!);
            (this.FindViewById<TextView>(Resource.Id.link)!.Parent as View)!.Click += async (o, e) => await OpenVendorUrlAsync();
        }


        /// <summary>
        /// Open the author's website. A watch has no browser, so this opens it on the paired phone, telling the user
        /// to continue there.
        /// </summary>
        private async Task OpenVendorUrlAsync()
        {
            await ExternalUrl.OpenAsync(this,
                this.Resources!.GetString(Resource.String.continueOnPhoneMsg),
                Android.Net.Uri.Parse(this.Resources!.GetString(Resource.String.vendorUrl))!);
        }
    }
}
