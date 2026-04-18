using AVFoundation;
using Foundation;
using Speech;
using System;
using System.Threading.Tasks;

namespace TheWatch.Maui.Services
{
    public class IosPhraseRecognitionService : IPhraseRecognitionService
    {
        private SFSpeechAudioBufferRecognitionRequest? _speechRequest;
        private SFSpeechRecognizer? _speechRecognizer;
        private SFSpeechRecognitionTask? _recognitionTask;
        private AVAudioEngine? _audioEngine;

        public event EventHandler<PhraseRecognizedEventArgs>? PhraseRecognized;

        public async Task<bool> RequestPermission()
        {
            var status = await SFSpeechRecognizer.RequestAuthorizationAsync();
            return status == SFSpeechRecognizerAuthorizationStatus.Authorized;
        }

        public void StartListening(string[] phrases)
        {
            if (_audioEngine?.Running ?? false) StopListening();

            _speechRecognizer = new SFSpeechRecognizer(new NSLocale("en-US"));
            _audioEngine = new AVAudioEngine();
            _speechRequest = new SFSpeechAudioBufferRecognitionRequest();

            var inputNode = _audioEngine.InputNode;
            var recordingFormat = inputNode.GetBusOutputFormat(0);

            inputNode.InstallTapOnBus(0, 1024, recordingFormat, (buffer, when) =>
            {
                _speechRequest.Append(buffer);
            });

            _audioEngine.Prepare();
            _audioEngine.StartAndReturnError(out var error);

            _recognitionTask = _speechRecognizer.GetRecognitionTask(_speechRequest, (result, err) =>
            {
                if (result != null)
                {
                    var bestString = result.BestTranscription.FormattedString;
                    foreach (var phrase in phrases)
                    {
                        if (bestString.ToLowerInvariant().Contains(phrase.ToLowerInvariant()))
                        {
                            PhraseRecognized?.Invoke(this, new PhraseRecognizedEventArgs(bestString));
                            StopListening();
                            StartListening(phrases); // Restart
                            return;
                        }
                    }
                }
            });
        }

        public void StopListening()
        {
            _audioEngine?.Stop();
            _speechRequest?.EndAudio();
            _recognitionTask?.Cancel();
            _recognitionTask = null;
            _speechRequest = null;
            _audioEngine = null;
            _speechRecognizer = null;
        }
    }
}
