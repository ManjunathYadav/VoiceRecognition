namespace V18.VoiceConversation.Platform
{
    internal enum SpeechBackendEventType
    {
        ReadyForSpeech,
        BeginningOfSpeech,
        EndOfSpeech,
        PartialResult,
        FinalResult,
        Error
    }

    internal struct SpeechBackendEvent
    {
        public SpeechBackendEventType Type;
        public string Text;
        public int ErrorCode;
        public string ErrorMessage;

        public static SpeechBackendEvent Ready()
        {
            return new SpeechBackendEvent { Type = SpeechBackendEventType.ReadyForSpeech };
        }

        public static SpeechBackendEvent Beginning()
        {
            return new SpeechBackendEvent { Type = SpeechBackendEventType.BeginningOfSpeech };
        }

        public static SpeechBackendEvent End()
        {
            return new SpeechBackendEvent { Type = SpeechBackendEventType.EndOfSpeech };
        }

        public static SpeechBackendEvent Partial(string text)
        {
            return new SpeechBackendEvent { Type = SpeechBackendEventType.PartialResult, Text = text };
        }

        public static SpeechBackendEvent Final(string text)
        {
            return new SpeechBackendEvent { Type = SpeechBackendEventType.FinalResult, Text = text };
        }

        public static SpeechBackendEvent Error(int errorCode, string errorMessage)
        {
            return new SpeechBackendEvent
            {
                Type = SpeechBackendEventType.Error,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage
            };
        }
    }
}
