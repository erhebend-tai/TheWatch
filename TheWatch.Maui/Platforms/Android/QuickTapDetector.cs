using Android.Views;
using System;

namespace TheWatch.Maui.Platforms.Android
{
    /// <summary>
    /// Detects rapid key presses (e.g., triple-press of volume down)
    /// to trigger an action, like SOS.
    /// </summary>
    public class QuickTapDetector
    {
        private const int RequiredTaps = 3;
        private const int TimeThresholdMs = 1500; // 1.5 seconds

        private readonly Action _onTapsDetected;
        private readonly List<long> _tapTimestamps = new();

        public QuickTapDetector(Action onTapsDetected)
        {
            _onTapsDetected = onTapsDetected;
        }

        public bool OnKeyEvent(KeyEvent e)
        {
            // We only care about Volume Down presses
            if (e.Action == KeyEventActions.Down && e.KeyCode == Keycode.VolumeDown)
            {
                var now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                _tapTimestamps.Add(now);

                // Remove timestamps older than the threshold
                _tapTimestamps.RemoveAll(ts => now - ts > TimeThresholdMs);

                if (_tapTimestamps.Count >= RequiredTaps)
                {
                    _tapTimestamps.Clear();
                    _onTapsDetected?.Invoke();
                    return true; // Consume the event
                }
            }
            return false;
        }
    }
}
