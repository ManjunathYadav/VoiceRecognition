using System;

namespace V18.VoiceConversation.Platform
{
    internal sealed class UnsupportedSpeechRecognizerBackend : IPlatformSpeechRecognizer
    {
        private Action<SpeechBackendEvent> eventSink;

        public bool IsSupported
        {
            get { return false; }
        }

        public string SupportMessage
        {
            get { return "Runtime speech recognition is only implemented for Android device builds. Use SimulateRecognizedSpeech in the Editor to test UnityEvent wiring."; }
        }

        public void Initialize(SpeechRecognizerSettings settings, Action<SpeechBackendEvent> sink)
        {
            eventSink = sink;
        }

        public void StartListening()
        {
            if (eventSink != null)
            {
                eventSink(SpeechBackendEvent.Error(-1, SupportMessage));
            }
        }

        public void StopListening()
        {
        }

        public void CancelListening()
        {
        }

        public void Dispose()
        {
            eventSink = null;
        }
    }
}
