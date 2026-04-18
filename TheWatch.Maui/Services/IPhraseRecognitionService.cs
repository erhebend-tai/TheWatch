namespace TheWatch.Maui.Services
{
    public interface IPhraseRecognitionService
    {
        Task<bool> RequestPermission();
        void StartListening(string[] phrases);
        void StopListening();
        event EventHandler<PhraseRecognizedEventArgs> PhraseRecognized;
    }

    public class PhraseRecognizedEventArgs : EventArgs
    {
        public string Text { get; }
        public PhraseRecognizedEventArgs(string text)
        {
            Text = text;
        }
    }
}
