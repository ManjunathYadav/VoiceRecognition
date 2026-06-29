#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace V18.VoiceConversation.Platform
{
    internal sealed class AndroidSpeechRecognizerBackend : IPlatformSpeechRecognizer
    {
        private const string UnityPlayerClass = "com.unity3d.player.UnityPlayer";
        private const string SpeechRecognizerClass = "android.speech.SpeechRecognizer";
        private const string RecognizerIntentAction = "android.speech.action.RECOGNIZE_SPEECH";
        private const string ResultsRecognitionKey = "results_recognition";

        private const string ExtraLanguageModel = "android.speech.extra.LANGUAGE_MODEL";
        private const string ExtraPartialResults = "android.speech.extra.PARTIAL_RESULTS";
        private const string ExtraMaxResults = "android.speech.extra.MAX_RESULTS";
        private const string ExtraLanguage = "android.speech.extra.LANGUAGE";
        private const string ExtraPreferOffline = "android.speech.extra.PREFER_OFFLINE";
        private const string ExtraCompleteSilence = "android.speech.extra.SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS";
        private const string ExtraPossiblyCompleteSilence = "android.speech.extra.SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS";
        private const string LanguageModelFreeForm = "free_form";

        private readonly object stateLock = new object();

        private SpeechRecognizerSettings settings;
        private Action<SpeechBackendEvent> eventSink;
        private AndroidJavaObject activity;
        private AndroidJavaObject recognizer;
        private AndroidJavaObject recognizerIntent;
        private RecognitionListenerProxy listenerProxy;
        private bool disposed;
        private bool availabilityReported;

        public bool IsSupported
        {
            get { return true; }
        }

        public string SupportMessage
        {
            get { return "Android SpeechRecognizer backend"; }
        }

        public void Initialize(SpeechRecognizerSettings recognizerSettings, Action<SpeechBackendEvent> sink)
        {
            settings = recognizerSettings;
            eventSink = sink;
            RunOnAndroidUiThread(delegate { EnsureRecognizerOnUiThread(); });
        }

        public void StartListening()
        {
            RunOnAndroidUiThread(delegate
            {
                if (!EnsureRecognizerOnUiThread())
                {
                    return;
                }

                try
                {
                    recognizer.Call("startListening", recognizerIntent);
                }
                catch (Exception exception)
                {
                    Emit(SpeechBackendEvent.Error(-2, "Failed to start Android SpeechRecognizer: " + exception.Message));
                }
            });
        }

        public void StopListening()
        {
            RunOnAndroidUiThread(delegate
            {
                if (recognizer == null)
                {
                    return;
                }

                try
                {
                    recognizer.Call("stopListening");
                }
                catch (Exception exception)
                {
                    Emit(SpeechBackendEvent.Error(-3, "Failed to stop Android SpeechRecognizer: " + exception.Message));
                }
            });
        }

        public void CancelListening()
        {
            RunOnAndroidUiThread(delegate
            {
                if (recognizer == null)
                {
                    return;
                }

                try
                {
                    recognizer.Call("cancel");
                }
                catch (Exception exception)
                {
                    Emit(SpeechBackendEvent.Error(-4, "Failed to cancel Android SpeechRecognizer: " + exception.Message));
                }
            });
        }

        public void Dispose()
        {
            lock (stateLock)
            {
                disposed = true;
            }

            RunOnAndroidUiThread(delegate
            {
                if (recognizer != null)
                {
                    try
                    {
                        recognizer.Call("destroy");
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("Failed to destroy Android SpeechRecognizer: " + exception.Message);
                    }

                    recognizer.Dispose();
                    recognizer = null;
                }

                if (recognizerIntent != null)
                {
                    recognizerIntent.Dispose();
                    recognizerIntent = null;
                }

                listenerProxy = null;
                activity = null;
                eventSink = null;
            });
        }

        private bool EnsureRecognizerOnUiThread()
        {
            lock (stateLock)
            {
                if (disposed)
                {
                    return false;
                }
            }

            try
            {
                if (activity == null)
                {
                    using (AndroidJavaClass unityPlayer = new AndroidJavaClass(UnityPlayerClass))
                    {
                        activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    }
                }

                using (AndroidJavaClass speechRecognizerClass = new AndroidJavaClass(SpeechRecognizerClass))
                {
                    bool recognitionAvailable = speechRecognizerClass.CallStatic<bool>("isRecognitionAvailable", activity);
                    if (!recognitionAvailable)
                    {
                        if (!availabilityReported)
                        {
                            availabilityReported = true;
                            Emit(SpeechBackendEvent.Error(-5, "Android SpeechRecognizer is not available on this device. Quest firmware must provide a speech recognition service, or you need to swap in a Meta Voice SDK/cloud STT backend."));
                        }

                        return false;
                    }

                    if (recognizer == null)
                    {
                        recognizer = speechRecognizerClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", activity);
                        listenerProxy = new RecognitionListenerProxy(this);
                        recognizer.Call("setRecognitionListener", listenerProxy);
                    }
                }

                recognizerIntent = BuildRecognizerIntent();
                return true;
            }
            catch (Exception exception)
            {
                Emit(SpeechBackendEvent.Error(-6, "Failed to initialize Android SpeechRecognizer: " + exception.Message));
                return false;
            }
        }

        private AndroidJavaObject BuildRecognizerIntent()
        {
            if (recognizerIntent != null)
            {
                recognizerIntent.Dispose();
            }

            AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent", RecognizerIntentAction);
            intent.Call<AndroidJavaObject>("putExtra", ExtraLanguageModel, LanguageModelFreeForm);
            intent.Call<AndroidJavaObject>("putExtra", ExtraPartialResults, true);
            intent.Call<AndroidJavaObject>("putExtra", ExtraMaxResults, Mathf.Max(1, settings.MaxResults));
            intent.Call<AndroidJavaObject>("putExtra", ExtraPreferOffline, settings.PreferOfflineRecognition);

            if (!string.IsNullOrEmpty(settings.Language))
            {
                intent.Call<AndroidJavaObject>("putExtra", ExtraLanguage, settings.Language);
            }

            if (settings.CompleteSilenceMillis > 0)
            {
                intent.Call<AndroidJavaObject>("putExtra", ExtraCompleteSilence, settings.CompleteSilenceMillis);
            }

            if (settings.PossiblyCompleteSilenceMillis > 0)
            {
                intent.Call<AndroidJavaObject>("putExtra", ExtraPossiblyCompleteSilence, settings.PossiblyCompleteSilenceMillis);
            }

            return intent;
        }

        private void RunOnAndroidUiThread(Action action)
        {
            lock (stateLock)
            {
                if (disposed && recognizer == null)
                {
                    return;
                }
            }

            try
            {
                if (activity == null)
                {
                    using (AndroidJavaClass unityPlayer = new AndroidJavaClass(UnityPlayerClass))
                    {
                        activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    }
                }

                activity.Call("runOnUiThread", new AndroidRunnable(action));
            }
            catch (Exception exception)
            {
                Emit(SpeechBackendEvent.Error(-7, "Failed to run speech action on Android UI thread: " + exception.Message));
            }
        }

        private void Emit(SpeechBackendEvent speechEvent)
        {
            Action<SpeechBackendEvent> sink = eventSink;
            if (sink != null)
            {
                sink(speechEvent);
            }
        }

        private string ExtractRecognitionText(AndroidJavaObject bundle)
        {
            if (bundle == null)
            {
                return string.Empty;
            }

            try
            {
                using (AndroidJavaObject matches = bundle.Call<AndroidJavaObject>("getStringArrayList", ResultsRecognitionKey))
                {
                    if (matches == null)
                    {
                        return string.Empty;
                    }

                    int count = matches.Call<int>("size");
                    if (count <= 0)
                    {
                        return string.Empty;
                    }

                    return matches.Call<string>("get", 0);
                }
            }
            catch (Exception exception)
            {
                Emit(SpeechBackendEvent.Error(-8, "Failed to read recognition result: " + exception.Message));
                return string.Empty;
            }
        }

        private static string GetSpeechRecognizerErrorMessage(int errorCode)
        {
            switch (errorCode)
            {
                case 1:
                    return "Network timeout";
                case 2:
                    return "Network error";
                case 3:
                    return "Audio recording error";
                case 4:
                    return "Server error";
                case 5:
                    return "Client error";
                case 6:
                    return "No speech input";
                case 7:
                    return "No recognition match";
                case 8:
                    return "Recognition service busy";
                case 9:
                    return "Insufficient microphone permissions";
                case 10:
                    return "Too many recognition requests";
                case 11:
                    return "Recognition server disconnected";
                case 12:
                    return "Language is not supported";
                case 13:
                    return "Language is unavailable";
                default:
                    return "Unknown SpeechRecognizer error";
            }
        }

        private sealed class AndroidRunnable : AndroidJavaProxy
        {
            private readonly Action action;

            public AndroidRunnable(Action actionToRun) : base("java.lang.Runnable")
            {
                action = actionToRun;
            }

            public void run()
            {
                if (action != null)
                {
                    action();
                }
            }
        }

        private sealed class RecognitionListenerProxy : AndroidJavaProxy
        {
            private readonly AndroidSpeechRecognizerBackend backend;

            public RecognitionListenerProxy(AndroidSpeechRecognizerBackend owner) : base("android.speech.RecognitionListener")
            {
                backend = owner;
            }

            public void onReadyForSpeech(AndroidJavaObject parameters)
            {
                backend.Emit(SpeechBackendEvent.Ready());
            }

            public void onBeginningOfSpeech()
            {
                backend.Emit(SpeechBackendEvent.Beginning());
            }

            public void onRmsChanged(float rmsdB)
            {
            }

            public void onBufferReceived(byte[] buffer)
            {
            }

            public void onEndOfSpeech()
            {
                backend.Emit(SpeechBackendEvent.End());
            }

            public void onError(int error)
            {
                backend.Emit(SpeechBackendEvent.Error(error, GetSpeechRecognizerErrorMessage(error)));
            }

            public void onResults(AndroidJavaObject results)
            {
                backend.Emit(SpeechBackendEvent.Final(backend.ExtractRecognitionText(results)));
            }

            public void onPartialResults(AndroidJavaObject partialResults)
            {
                backend.Emit(SpeechBackendEvent.Partial(backend.ExtractRecognitionText(partialResults)));
            }

            public void onEvent(int eventType, AndroidJavaObject parameters)
            {
            }
        }
    }
}
#endif
