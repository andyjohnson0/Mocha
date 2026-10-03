using Android;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Android.Speech;
using Android.Text;

using AndroidX.AppCompat.App;

using Kilsten.WearOS;
using Kilsten.WearOS.Controls;



namespace Mocha.App
{
    /// <summary>
    /// Screen for entering a brewing method name, by typing or by voice. Used both to rename a method and to name a
    /// new one. Started for a result. On OK, returns the name, which is non-empty and not one of the names in use.
    /// </summary>
    [Activity]
    public class EditMethodNameActivity : AppCompatActivity
    {
        /// <summary>Intent extra key (string, in and out): the method name.</summary>
        public const string MethodNameKey = "MethodNameKey";

        /// <summary>
        /// Intent extra key (string array, in): names that are already in use, which the result must not match.
        /// </summary>
        public const string MethodNamesInUseKey = "MethodNamesInUseKey";

        // Activity and permission request codes.
        private const int _speechReq = 1001;
        private const int _recordAudioPermissionReq = 1002;

        private string[] _methodNamesInUse = [];


        /// <inheritdoc/>
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            SetTheme(Resource.Style.Theme_App);
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.edit_method_name_activity);

            var methodNameEdt = FindViewById<EditText>(Resource.Id.methodNameEdt)!;
            var filters = new IInputFilter[] { new InputFilterLengthFilter(max: Method.MaxNameLen) };
            methodNameEdt.SetFilters(filters);

            methodNameEdt.Text = this.Intent!.GetStringExtra(EditMethodNameActivity.MethodNameKey);
            this._methodNamesInUse = this.Intent!.GetStringArrayExtra(EditMethodNameActivity.MethodNamesInUseKey) ?? [];
            FindViewById<ImageButton>(Resource.Id.voiceBtn)!.Click += VoiceBtn_Click;
            FindViewById<ImageButton>(Resource.Id.okBtn)!.Click += OkBtn_Click;
        }


        /// <summary>
        /// Handles the result of speech recognition, putting the recognised text in the name field.
        /// </summary>
        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
        {
            if (requestCode == EditMethodNameActivity._speechReq && resultCode == Result.Ok)
            {
                var results = data?.GetStringArrayListExtra(RecognizerIntent.ExtraResults);
                if (results?.Count > 0)
                {
                    var methodNameEdt = FindViewById<EditText>(Resource.Id.methodNameEdt)!;
                    methodNameEdt.Text = results[0] as string;
                }
                else
                {
                    Toast.MakeText(this, Resource.String.voiceInputRecognitionFailedMsg, ToastLength.Short)!.Show();
                }
            }
            else
            {
                base.OnActivityResult(requestCode, resultCode, data);
            }
        }


        /// <summary>
        /// Handles the result of requesting the record audio permission. If granted, starts voice input.
        /// </summary>
        public override void OnRequestPermissionsResult(
            int requestCode,
            string[] permissions,
            [GeneratedEnum] Permission[] grantResults)
        {
            if (requestCode == _recordAudioPermissionReq)
            {
                if ((grantResults.Length > 0) && grantResults[0] == Permission.Granted)
                {
                    // Permission granted.
                    FindViewById<ImageButton>(Resource.Id.voiceBtn)!.PerformClick();
                }
                else
                {
                    // Permission not granted. Explain to the user why we want the permission.
                    var adb = new WearableAlertDialog.Builder(this);
                    adb.SetCancelable(true);
                    adb.SetTitle(Resource.String.recordAudioPermissionPrompt);
                    adb.SetMessage(Resource.String.recordAudioPermissionRequestMsg);
                    adb.SetPositiveButton();
                    adb.Create().Show();
                }
            }
            else
            {
                base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            }
        }


        /// <summary>
        /// Check for the record audio permission needed for voice input. If it isn't granted then request it,
        /// explaining why first if appropriate.
        /// </summary>
        /// <returns>
        /// True if the permission is granted. False if it is being requested, in which case
        /// <see cref="OnRequestPermissionsResult"/> is called with the outcome.
        /// </returns>
        private bool CheckHasPermissions()
        {
            var permissions = new string[] 
            { 
                Manifest.Permission.RecordAudio 
            };

            if (this.CheckSelfPermission(permissions[0]) != Permission.Granted)
            { 
                if (this.ShouldShowRequestPermissionRationale(permissions[0]))
                {
                    // Explain to the user why we want the permission.
                    var adb = new WearableAlertDialog.Builder(this);
                    adb.SetCancelable(true);
                    adb.SetTitle(Resource.String.recordAudioPermissionPrompt);
                    adb.SetMessage(Resource.String.recordAudioPermissionRequestMsg);
                    adb.SetPositiveButton(handler: (sender, args) =>
                    {
                        // Request permission. This calls OnRequestPermissionsResult() with grant/deny.
                        this.RequestPermissions(permissions, _recordAudioPermissionReq);
                    });
                    adb.SetNegativeButton();
                    adb.Create().Show();
                    return false;
                }
                else
                {
                    // No need to explain. Just acquire the permission.
                    // System uses its own UI and calls OnRequestPermissionsResult() with grant/deny.
                    this.RequestPermissions(permissions, _recordAudioPermissionReq);
                    return false;
                }
            }

            return true;
        }


        /// <summary>
        /// Voice button handler. Starts speech recognition, if the record audio permission is granted.
        /// </summary>
        private void VoiceBtn_Click(object? sender, EventArgs e)
        {
            if (CheckHasPermissions())
            {
                var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
                intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
                this.StartActivityForResult(intent, EditMethodNameActivity._speechReq);
            }
        }   


        /// <summary>
        /// OK button handler. Returns the trimmed name as the activity result, if it is valid.
        /// </summary>
        private void OkBtn_Click(object? sender, EventArgs e)
        {
            var methodNameEdt = FindViewById<EditText>(Resource.Id.methodNameEdt)!;
            var methodName = methodNameEdt.Text!.Trim();

            // Method names must be non-empty and unique. If not then stay here so the user can change it.
            if (methodName.Length == 0)
            {
                Toast.MakeText(this, Resource.String.methodNameRequiredMsg, ToastLength.Short)!.Show();
                return;
            }
            if (this._methodNamesInUse.Contains(methodName, Beverage.MethodNameComparer))
            {
                Toast.MakeText(this, Resource.String.methodNameInUseMsg, ToastLength.Short)!.Show();
                return;
            }

            var intent = new Intent();
            intent.PutExtra(EditMethodNameActivity.MethodNameKey, methodName);
            this.SetResult(Result.Ok, intent);
            this.Finish();
        }   

    }
}

