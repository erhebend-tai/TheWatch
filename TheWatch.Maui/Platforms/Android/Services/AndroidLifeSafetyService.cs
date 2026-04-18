using Android.Content;
using Android.OS;
using TheWatch.Maui.Services;
using System.Diagnostics;

namespace TheWatch.Maui.Platforms.Android.Services;

public class AndroidLifeSafetyService : ILifeSafetyService
{
    private readonly IPhraseRecognitionService _phraseRecognitionService;
    private bool _isSosActive;
    public bool IsSosActive => _isSosActive;
    private readonly string[] _phrases = { "Help me", "I'm in danger", "SOS" };


    public AndroidLifeSafetyService(IPhraseRecognitionService phraseRecognitionService)
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
        if (_isSosActive) return;

        _isSosActive = true;
        var context = MauiApplication.Current.ApplicationContext;
        var intent = new Intent(context, typeof(SosForegroundService));
        intent.SetAction(SosForegroundService.ActionStart);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
        Debug.WriteLine("[NATIVE-SOS] SOS Foreground Service Started on Android");

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
        if (!_isSosActive) return;

        _isSosActive = false;
        var context = MauiApplication.Current.ApplicationContext;
        var intent = new Intent(context, typeof(SosForegroundService));
        intent.SetAction(SosForegroundService.ActionStop);
        context.StartService(intent);
        Debug.WriteLine("[NATIVE-SOS] SOS Foreground Service Stopped on Android");

        _phraseRecognitionService.StopListening();
        Debug.WriteLine("[PHRASE-RECOGNIZED] Stopped listening for phrases.");
    }
}
