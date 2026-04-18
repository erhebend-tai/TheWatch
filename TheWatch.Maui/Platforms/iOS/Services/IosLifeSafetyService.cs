using Foundation;
using UIKit;
using TheWatch.Maui.Services;
using System.Diagnostics;

namespace TheWatch.Maui.Platforms.iOS.Services;

public class IosLifeSafetyService : ILifeSafetyService
{
    private readonly IPhraseRecognitionService _phraseRecognitionService;
    private bool _isSosActive;
    public bool IsSosActive => _isSosActive;
    private readonly string[] _phrases = { "Help me", "I'm in danger", "SOS" };

    public IosLifeSafetyService(IPhraseRecognitionService phraseRecognitionService)
    {
        _phraseRecognitionService = phraseRecognitionService;
        _phraseRecognitionService.PhraseRecognized += OnPhraseRecognized;
    }

    private void OnPhraseRecognized(object? sender, PhraseRecognizedEventArgs e)
    {
        Debug.WriteLine($"[PHRASE-RECOGNIZED] Phrase detected: {e.Text}. A real app would trigger a secondary action here.");
        // We could, for example, send this transcript to the ResponseService API.
    }

    public async void TriggerSos()
    {
        _isSosActive = true;
        // In a real implementation, this would use UNNotificationContentExtension
        // and CallKit to handle the emergency state.
        Debug.WriteLine("[NATIVE-SOS] SOS Triggered on iOS");

        if (await _phraseRecognitionService.RequestPermission())
        {
            _phraseRecognitionService.StartListening(_phrases);
            Debug.WriteLine("[PHRASE-RECOGNIZED] Started listening for phrases.");
        }
        else
        {
            Debug.WriteLine("[PHRASE-RECOGNIZED] Microphone permission not granted.");
        }
    }

    public void CancelSos()
    {
        _isSosActive = false;
        Debug.WriteLine("[NATIVE-SOS] SOS Cancelled on iOS");

        _phraseRecognitionService.StopListening();
        Debug.WriteLine("[PHRASE-RECOGNIZED] Stopped listening for phrases.");
    }
}
