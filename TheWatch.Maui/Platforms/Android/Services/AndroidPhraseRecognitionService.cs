using Android.Content;
using Android.Media;
using Android.Speech;
using Microsoft.Maui.ApplicationModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TheWatch.Maui.Services
{
    public class AndroidPhraseRecognitionService : IPhraseRecognitionService
    {
        private SpeechRecognizer? _speechRecognizer;
        private Intent? _speechIntent;
        private RecognitionListener? _listener;

        public event EventHandler<PhraseRecognizedEventArgs>? PhraseRecognized;

        public async Task<bool> RequestPermission()
        {
            var status = await Permissions.RequestAsync<Permissions.Microphone>();
            return status == PermissionStatus.Granted;
        }

        public void StartListening(string[] phrases)
        {
            if (_speechRecognizer != null) StopListening();

            _speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(Platform.AppContext);
            _listener = new RecognitionListener();
            _listener.ResultsReady += (sender, results) =>
            {
                foreach (var result in results)
                {
                    foreach (var phrase in phrases)
                    {
                        if (result.ToLowerInvariant().Contains(phrase.ToLowerInvariant()))
                        {
                            PhraseRecognized?.Invoke(this, new PhraseRecognizedEventArgs(result));
                            // Restart listening
                            StartListening(phrases);
                            return;
                        }
                    }
                }
                // If no match, continue listening
                StartListening(phrases);
            };

            _speechRecognizer.SetRecognitionListener(_listener);

            _speechIntent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            _speechIntent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            _speechIntent.PutExtra(RecognizerIntent.ExtraCallingPackage, Platform.AppContext.PackageName);
            _speechIntent.PutExtra(RecognizerIntent.ExtraPartialResults, true);

            _speechRecognizer.StartListening(_speechIntent);
        }

        public void StopListening()
        {
            _speechRecognizer?.StopListening();
            _speechRecognizer?.Destroy();
            _speechRecognizer = null;
            _listener = null;
        }

        private class RecognitionListener : Java.Lang.Object, IRecognitionListener
        {
            public event EventHandler<List<string>>? ResultsReady;

            public void OnBeginningOfSpeech() { }
            public void OnBufferReceived(byte[]? buffer) { }
            public void OnEndOfSpeech() { }
            public void OnError(SpeechRecognizerError error) { }
            public void OnEvent(int eventType, Bundle? @params) { }
            public void OnPartialResults(Bundle? partialResults) { }
            public void OnReadyForSpeech(Bundle? @params) { }
            public void OnRmsChanged(float rmscDb) { }
            public void OnResults(Bundle? results)
            {
                var matches = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
                if (matches != null)
                {
                    ResultsReady?.Invoke(this, new List<string>(matches));
                }
            }
        }
    }
}
