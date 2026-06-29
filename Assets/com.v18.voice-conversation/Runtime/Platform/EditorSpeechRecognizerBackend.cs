#if UNITY_EDITOR_WIN
using System;
using UnityEngine;
using UnityEngine.Windows.Speech;

namespace V18.VoiceConversation.Platform
{
    internal sealed class EditorSpeechRecognizerBackend : IPlatformSpeechRecognizer
    {
        private Action<SpeechBackendEvent> eventSink;
        private DictationRecognizer dictationRecognizer;
        private bool disposed;
        private bool listening;

        public bool IsSupported
        {
            get { return true; }
        }

        public string SupportMessage
        {
            get { return "Windows DictationRecognizer (Editor)"; }
        }

        public void Initialize(SpeechRecognizerSettings settings, Action<SpeechBackendEvent> sink)
        {
            eventSink = sink;
        }

        public void StartListening()
        {
            if (disposed || listening)
            {
                return;
            }

            try
            {
                if (dictationRecognizer == null)
                {
                    CreateDictationRecognizer();
                }

                if (dictationRecognizer.Status != SpeechSystemStatus.Running)
                {
                    dictationRecognizer.Start();
                    listening = true;
                    Emit(SpeechBackendEvent.Ready());
                }
            }
            catch (Exception exception)
            {
                Emit(SpeechBackendEvent.Error(-1,
                    "Failed to start Windows DictationRecognizer. "
                    + "Ensure Online Speech Recognition is enabled in Windows Settings > Privacy > Speech. "
                    + exception.Message));
            }
        }

        public void StopListening()
        {
            StopDictationRecognizer();
        }

        public void CancelListening()
        {
            StopDictationRecognizer();
        }

        public void Dispose()
        {
            disposed = true;
            DestroyDictationRecognizer();
            eventSink = null;
        }

        private void CreateDictationRecognizer()
        {
            dictationRecognizer = new DictationRecognizer();

            dictationRecognizer.DictationResult += OnDictationResult;
            dictationRecognizer.DictationHypothesis += OnDictationHypothesis;
            dictationRecognizer.DictationComplete += OnDictationComplete;
            dictationRecognizer.DictationError += OnDictationError;
        }

        private void StopDictationRecognizer()
        {
            listening = false;

            if (dictationRecognizer != null && dictationRecognizer.Status == SpeechSystemStatus.Running)
            {
                try
                {
                    dictationRecognizer.Stop();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[EditorSpeechRecognizer] Failed to stop DictationRecognizer: " + exception.Message);
                }
            }
        }

        private void DestroyDictationRecognizer()
        {
            StopDictationRecognizer();

            if (dictationRecognizer != null)
            {
                dictationRecognizer.DictationResult -= OnDictationResult;
                dictationRecognizer.DictationHypothesis -= OnDictationHypothesis;
                dictationRecognizer.DictationComplete -= OnDictationComplete;
                dictationRecognizer.DictationError -= OnDictationError;
                dictationRecognizer.Dispose();
                dictationRecognizer = null;
            }
        }

        private void OnDictationHypothesis(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                Emit(SpeechBackendEvent.Beginning());
                Emit(SpeechBackendEvent.Partial(text));
            }
        }

        private void OnDictationResult(string text, ConfidenceLevel confidence)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                Emit(SpeechBackendEvent.End());
                Emit(SpeechBackendEvent.Final(text));
            }
        }

        private void OnDictationComplete(DictationCompletionCause cause)
        {
            listening = false;

            switch (cause)
            {
                case DictationCompletionCause.Complete:
                    break;

                case DictationCompletionCause.TimeoutExceeded:
                    // Timeout is a normal completion — the recognizer auto-stops after silence.
                    // Emit as a recoverable error so the recognizer can auto-restart.
                    Emit(SpeechBackendEvent.Error(6, "No speech input (timeout)"));
                    break;

                default:
                    Emit(SpeechBackendEvent.Error(-1, "DictationRecognizer completed with cause: " + cause));
                    break;
            }
        }

        private void OnDictationError(string error, int hresult)
        {
            listening = false;
            Emit(SpeechBackendEvent.Error(-1,
                "Windows speech recognition error: " + error
                + " (HRESULT: 0x" + hresult.ToString("X8") + "). "
                + "Ensure Online Speech Recognition is enabled in Windows Settings > Privacy > Speech."));
        }

        private void Emit(SpeechBackendEvent speechEvent)
        {
            Action<SpeechBackendEvent> sink = eventSink;
            if (sink != null)
            {
                sink(speechEvent);
            }
        }
    }
}
#endif
