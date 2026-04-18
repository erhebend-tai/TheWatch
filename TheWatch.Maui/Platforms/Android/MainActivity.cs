using Android.App;
using Android.OS;
using Android.Views;
using TheWatch.Maui.Services;

namespace TheWatch.Maui.Platforms.Android
{
    [Activity(Label = "TheWatch.Maui", MainLauncher = true)]
    public class MainActivity : MauiAppCompatActivity
    {
        private QuickTapDetector? _quickTapDetector;
        private ILifeSafetyService? _lifeSafetyService;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Resolve the life safety service from the DI container
            _lifeSafetyService = MauiApplication.Current.Services.GetService<ILifeSafetyService>();
            
            if (_lifeSafetyService != null)
            {
                _quickTapDetector = new QuickTapDetector(() => 
                {
                    // Action to perform on triple-press
                    _lifeSafetyService.TriggerSos();
                    
                    // Optional: Bring app to foreground
                    var intent = new Intent(this, typeof(MainActivity));
                    intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
                    StartActivity(intent);
                });
            }
        }

        public override bool DispatchKeyEvent(KeyEvent? e)
        {
            if (e != null && _quickTapDetector != null)
            {
                if (_quickTapDetector.OnKeyEvent(e))
                {
                    return true; // Event was consumed by the detector
                }
            }
            return base.DispatchKeyEvent(e);
        }
    }
}
