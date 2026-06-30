using System;

namespace V18.VoiceConversation.Platform
{
    internal interface IPlatformSpeechRecognizer : IDisposable
    {
        bool IsSupported { get; }
        string SupportMessage { get; }

        void Initialize(SpeechRecognizerSettings settings, Action<SpeechBackendEvent> eventSink);
        void StartListening();
        void StopListening();
        void CancelListening();
    }

    internal interface IUpdatableSpeechRecognizer
    {
        void Tick();
    }
}
