namespace V18.VoiceConversation.Platform
{
    internal struct SpeechRecognizerSettings
    {
        public string Language;
        public bool PreferOfflineRecognition;
        public int CompleteSilenceMillis;
        public int PossiblyCompleteSilenceMillis;
        public int MaxResults;
        public int OfflineSampleRate;
    }
}
