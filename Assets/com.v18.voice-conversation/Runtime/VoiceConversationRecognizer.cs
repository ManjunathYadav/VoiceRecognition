using System.Collections.Generic;
using UnityEngine;
using V18.VoiceConversation.Platform;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace V18.VoiceConversation
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Voice Conversation/Voice Conversation Recognizer")]
    public sealed class VoiceConversationRecognizer : MonoBehaviour
    {
        [Header("Listening")]
        [SerializeField] private bool autoStartListening = true;
        [SerializeField] private bool restartAfterFinalResult = true;
        [SerializeField] private bool restartAfterRecoverableErrors = true;
        [SerializeField] [Min(0f)] private float restartDelaySeconds = 0.25f;
        [SerializeField] private string recognitionLanguage = "en-US";
        [SerializeField] private bool preferOfflineRecognition;

        [Header("Speech Endpointing")]
        [SerializeField] [Min(250)] private int completeSilenceMillis = 900;
        [SerializeField] [Min(250)] private int possiblyCompleteSilenceMillis = 650;
        [SerializeField] [Min(1)] private int maxResults = 1;

        [Header("Android Permission")]
        [SerializeField] private bool requestMicrophonePermissionOnEnable = true;

        [Header("Editor Test")]
        [SerializeField] private string simulatedEditorTranscript = "Hello avatar";

        [Header("Debug")]
        [SerializeField] private bool logDebugMessages;

        public SpeechTextEvent TranscriptUpdated = new SpeechTextEvent();
        public SpeechTextEvent FinalTranscript = new SpeechTextEvent();
        public SpeechTextEvent UserSpeechComplete = new SpeechTextEvent();
        public SpeechStateEvent ListeningStateChanged = new SpeechStateEvent();
        public SpeechStateEvent UserSpeakingStateChanged = new SpeechStateEvent();
        public SpeechStateEvent RecognitionBlockedChanged = new SpeechStateEvent();
        public SpeechErrorEvent RecognitionError = new SpeechErrorEvent();

        private readonly Queue<SpeechBackendEvent> pendingBackendEvents = new Queue<SpeechBackendEvent>();
        private readonly object pendingEventLock = new object();

        private IPlatformSpeechRecognizer backend;
        private bool backendInitialized;
        private bool unsupportedReported;
        private bool listening;
        private bool userSpeaking;
        private bool manualStopRequested;
        private bool manualRecognitionBlocked;
        private int recognitionBlockerCount;
        private float nextAllowedStartTime;
        private string lastTranscript = string.Empty;

#if UNITY_ANDROID && !UNITY_EDITOR
        private PermissionCallbacks permissionCallbacks;
        private bool microphonePermissionRequested;
#endif

        public bool IsListening
        {
            get { return listening; }
        }

        public bool IsUserSpeaking
        {
            get { return userSpeaking; }
        }

        public bool IsRecognitionBlocked
        {
            get { return manualRecognitionBlocked || recognitionBlockerCount > 0; }
        }

        public string LastTranscript
        {
            get { return lastTranscript; }
        }

        private void OnEnable()
        {
            manualStopRequested = !autoStartListening;
            InitializeBackendIfNeeded();

            if (autoStartListening)
            {
                ScheduleListeningRestart(0f);
            }
        }

        private void OnDisable()
        {
            DisposeBackend();
            SetListeningState(false);
            SetUserSpeakingState(false);

            lock (pendingEventLock)
            {
                pendingBackendEvents.Clear();
            }
        }

        private void Update()
        {
            DrainBackendEvents();

            if (!manualStopRequested && CanAttemptListeningNow())
            {
                TryStartListening();
            }
        }

        private void OnValidate()
        {
            restartDelaySeconds = Mathf.Max(0f, restartDelaySeconds);
            completeSilenceMillis = Mathf.Max(250, completeSilenceMillis);
            possiblyCompleteSilenceMillis = Mathf.Max(250, possiblyCompleteSilenceMillis);
            maxResults = Mathf.Max(1, maxResults);
        }

        public void StartListening()
        {
            manualStopRequested = false;
            ScheduleListeningRestart(0f);
            TryStartListening();
        }

        public void StopListening()
        {
            manualStopRequested = true;

            if (backend != null)
            {
                backend.StopListening();
            }

            SetListeningState(false);
        }

        public void CancelListening()
        {
            manualStopRequested = true;

            if (backend != null)
            {
                backend.CancelListening();
            }

            SetListeningState(false);
            SetUserSpeakingState(false);
        }

        public void SetRecognitionBlocked(bool blocked)
        {
            bool wasBlocked = IsRecognitionBlocked;
            manualRecognitionBlocked = blocked;
            ApplyRecognitionBlockChange(wasBlocked);
        }

        public void AddRecognitionBlocker()
        {
            bool wasBlocked = IsRecognitionBlocked;
            recognitionBlockerCount++;
            ApplyRecognitionBlockChange(wasBlocked);
        }

        public void RemoveRecognitionBlocker()
        {
            RemoveRecognitionBlockerAfter(0f);
        }

        public void RemoveRecognitionBlockerAfter(float resumeDelay)
        {
            bool wasBlocked = IsRecognitionBlocked;
            recognitionBlockerCount = Mathf.Max(0, recognitionBlockerCount - 1);
            ApplyRecognitionBlockChange(wasBlocked);

            if (!IsRecognitionBlocked)
            {
                ScheduleListeningRestart(resumeDelay);
            }
        }

        public void SimulateRecognizedSpeech(string transcript)
        {
            if (IsRecognitionBlocked)
            {
                Log("Ignoring simulated speech because recognition is blocked.");
                return;
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                return;
            }

            SetListeningState(true);
            SetUserSpeakingState(true);
            PublishTranscript(transcript);
            SetUserSpeakingState(false);
            CompleteUserSpeech(transcript);
            SetListeningState(false);

            if (autoStartListening && restartAfterFinalResult && !manualStopRequested)
            {
                ScheduleListeningRestart(restartDelaySeconds);
            }
        }

        [ContextMenu("Simulate Recognized Speech")]
        private void SimulateConfiguredRecognizedSpeech()
        {
            SimulateRecognizedSpeech(simulatedEditorTranscript);
        }

        private void InitializeBackendIfNeeded()
        {
            if (backendInitialized)
            {
                return;
            }

            backend = CreatePlatformBackend();
            backend.Initialize(BuildSettings(), EnqueueBackendEvent);
            backendInitialized = true;
        }

        private IPlatformSpeechRecognizer CreatePlatformBackend()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidSpeechRecognizerBackend();
#elif UNITY_EDITOR_WIN
            return new EditorSpeechRecognizerBackend();
#else
            return new UnsupportedSpeechRecognizerBackend();
#endif
        }

        private SpeechRecognizerSettings BuildSettings()
        {
            return new SpeechRecognizerSettings
            {
                Language = recognitionLanguage,
                PreferOfflineRecognition = preferOfflineRecognition,
                CompleteSilenceMillis = completeSilenceMillis,
                PossiblyCompleteSilenceMillis = possiblyCompleteSilenceMillis,
                MaxResults = maxResults
            };
        }

        private void DisposeBackend()
        {
            if (backend != null)
            {
                backend.Dispose();
                backend = null;
            }

            backendInitialized = false;
            unsupportedReported = false;
        }

        private bool CanAttemptListeningNow()
        {
            return isActiveAndEnabled
                && !listening
                && !IsRecognitionBlocked
                && Time.unscaledTime >= nextAllowedStartTime;
        }

        private void TryStartListening()
        {
            if (!CanAttemptListeningNow())
            {
                return;
            }

            InitializeBackendIfNeeded();

            if (backend == null)
            {
                return;
            }

            if (!backend.IsSupported)
            {
                if (!unsupportedReported)
                {
                    unsupportedReported = true;
                    RecognitionError.Invoke(backend.SupportMessage);
                    Log(backend.SupportMessage);
                }

                nextAllowedStartTime = float.PositiveInfinity;
                return;
            }

            if (!HasMicrophonePermission())
            {
                RequestMicrophonePermissionIfAllowed();
                ScheduleListeningRestart(1f);
                return;
            }

            SetListeningState(true);
            SetUserSpeakingState(false);
            backend.StartListening();
        }

        private bool HasMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
            return true;
#endif
        }

        private void RequestMicrophonePermissionIfAllowed()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!requestMicrophonePermissionOnEnable || microphonePermissionRequested)
            {
                return;
            }

            microphonePermissionRequested = true;
            permissionCallbacks = new PermissionCallbacks();
            permissionCallbacks.PermissionGranted += OnMicrophonePermissionGranted;
            permissionCallbacks.PermissionDenied += OnMicrophonePermissionDenied;
            permissionCallbacks.PermissionDeniedAndDontAskAgain += OnMicrophonePermissionDenied;
            Permission.RequestUserPermission(Permission.Microphone, permissionCallbacks);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void OnMicrophonePermissionGranted(string permissionName)
        {
            ScheduleListeningRestart(0f);
        }

        private void OnMicrophonePermissionDenied(string permissionName)
        {
            RecognitionError.Invoke("Microphone permission was denied. Enable microphone permission for this app in Quest settings.");
            Log("Microphone permission denied: " + permissionName);
        }
#endif

        private void EnqueueBackendEvent(SpeechBackendEvent speechEvent)
        {
            lock (pendingEventLock)
            {
                pendingBackendEvents.Enqueue(speechEvent);
            }
        }

        private void DrainBackendEvents()
        {
            while (true)
            {
                SpeechBackendEvent speechEvent;

                lock (pendingEventLock)
                {
                    if (pendingBackendEvents.Count == 0)
                    {
                        break;
                    }

                    speechEvent = pendingBackendEvents.Dequeue();
                }

                HandleBackendEvent(speechEvent);
            }
        }

        private void HandleBackendEvent(SpeechBackendEvent speechEvent)
        {
            if (IsRecognitionBlocked && speechEvent.Type != SpeechBackendEventType.Error)
            {
                return;
            }

            switch (speechEvent.Type)
            {
                case SpeechBackendEventType.ReadyForSpeech:
                    Log("Ready for speech.");
                    break;

                case SpeechBackendEventType.BeginningOfSpeech:
                    SetUserSpeakingState(true);
                    break;

                case SpeechBackendEventType.EndOfSpeech:
                    SetUserSpeakingState(false);
                    break;

                case SpeechBackendEventType.PartialResult:
                    if (!string.IsNullOrWhiteSpace(speechEvent.Text))
                    {
                        PublishTranscript(speechEvent.Text);
                    }
                    break;

                case SpeechBackendEventType.FinalResult:
                    SetListeningState(false);
                    SetUserSpeakingState(false);

                    if (!IsRecognitionBlocked && !string.IsNullOrWhiteSpace(speechEvent.Text))
                    {
                        PublishTranscript(speechEvent.Text);
                        CompleteUserSpeech(speechEvent.Text);
                    }

                    if (!manualStopRequested && autoStartListening && restartAfterFinalResult && !IsRecognitionBlocked)
                    {
                        ScheduleListeningRestart(restartDelaySeconds);
                    }
                    break;

                case SpeechBackendEventType.Error:
                    HandleRecognitionError(speechEvent);
                    break;
            }
        }

        private void HandleRecognitionError(SpeechBackendEvent speechEvent)
        {
            SetListeningState(false);
            SetUserSpeakingState(false);

            if (IsRecognitionBlocked && IsRecoverableAndroidError(speechEvent.ErrorCode))
            {
                return;
            }

            string message = "Speech recognition error";
            if (!string.IsNullOrEmpty(speechEvent.ErrorMessage))
            {
                message += ": " + speechEvent.ErrorMessage;
            }

            if (speechEvent.ErrorCode != 0)
            {
                message += " (" + speechEvent.ErrorCode + ")";
            }

            RecognitionError.Invoke(message);
            Log(message);

            if (!manualStopRequested
                && autoStartListening
                && restartAfterRecoverableErrors
                && !IsRecognitionBlocked
                && IsRecoverableAndroidError(speechEvent.ErrorCode))
            {
                ScheduleListeningRestart(GetErrorRestartDelay(speechEvent.ErrorCode));
            }
        }

        private bool IsRecoverableAndroidError(int errorCode)
        {
            return errorCode == 6
                || errorCode == 7
                || errorCode == 8
                || errorCode == 10;
        }

        private float GetErrorRestartDelay(int errorCode)
        {
            if (errorCode == 8 || errorCode == 10)
            {
                return Mathf.Max(0.75f, restartDelaySeconds);
            }

            return restartDelaySeconds;
        }

        private void PublishTranscript(string transcript)
        {
            lastTranscript = transcript;
            TranscriptUpdated.Invoke(transcript);
            Log("Transcript: " + transcript);
        }

        private void CompleteUserSpeech(string transcript)
        {
            FinalTranscript.Invoke(transcript);
            UserSpeechComplete.Invoke(transcript);
            Log("User speech complete: " + transcript);
        }

        private void ApplyRecognitionBlockChange(bool wasBlocked)
        {
            bool isBlocked = IsRecognitionBlocked;
            if (wasBlocked == isBlocked)
            {
                return;
            }

            RecognitionBlockedChanged.Invoke(isBlocked);

            if (isBlocked)
            {
                if (backend != null)
                {
                    backend.CancelListening();
                }

                SetListeningState(false);
                SetUserSpeakingState(false);
                Log("Recognition blocked.");
            }
            else
            {
                Log("Recognition unblocked.");

                if (!manualStopRequested && autoStartListening)
                {
                    ScheduleListeningRestart(restartDelaySeconds);
                }
            }
        }

        private void ScheduleListeningRestart(float delaySeconds)
        {
            nextAllowedStartTime = Time.unscaledTime + Mathf.Max(0f, delaySeconds);
        }

        private void SetListeningState(bool isListening)
        {
            if (listening == isListening)
            {
                return;
            }

            listening = isListening;
            ListeningStateChanged.Invoke(listening);
        }

        private void SetUserSpeakingState(bool isSpeaking)
        {
            if (userSpeaking == isSpeaking)
            {
                return;
            }

            userSpeaking = isSpeaking;
            UserSpeakingStateChanged.Invoke(userSpeaking);
        }

        private void Log(string message)
        {
            if (logDebugMessages)
            {
                Debug.Log("[VoiceConversationRecognizer] " + message, this);
            }
        }
    }
}
