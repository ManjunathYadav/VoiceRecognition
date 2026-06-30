#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace V18.VoiceConversation.Platform
{
    /// <summary>
    /// Offline speech recognition for standalone Quest builds. Quest does not
    /// normally expose Android's Google-backed SpeechRecognizer service, so the
    /// package ships a small Vosk model and decodes microphone PCM on-device.
    /// </summary>
    internal sealed class VoskSpeechRecognizerBackend : IPlatformSpeechRecognizer, IUpdatableSpeechRecognizer
    {
        private const string ModelAssetRoot = "vosk-model-small-en-us-0.15";
        private const string ModelInstallVersion = "en-us-small-0.15-v1";
        private const int MicrophoneBufferSeconds = 1;

        private static readonly string[] ModelFiles =
        {
            "am/final.mdl",
            "conf/mfcc.conf",
            "conf/model.conf",
            "graph/disambig_tid.int",
            "graph/Gr.fst",
            "graph/HCLr.fst",
            "graph/phones/word_boundary.int",
            "ivector/final.dubm",
            "ivector/final.ie",
            "ivector/final.mat",
            "ivector/global_cmvn.stats",
            "ivector/online_cmvn.conf",
            "ivector/splice.conf"
        };

        private readonly MonoBehaviour coroutineHost;

        private SpeechRecognizerSettings settings;
        private Action<SpeechBackendEvent> eventSink;
        private Coroutine prepareCoroutine;
        private AndroidJavaObject model;
        private AndroidJavaObject recognizer;
        private AudioClip microphoneClip;
        private int microphonePosition;
        private string lastPartial = string.Empty;
        private bool modelReady;
        private bool pendingStart;
        private bool capturing;
        private bool userSpeaking;
        private bool initializationFailed;
        private bool disposed;
        private string supportMessage = "Offline Vosk speech recognition for Meta Quest";

        public VoskSpeechRecognizerBackend(MonoBehaviour host)
        {
            coroutineHost = host;
        }

        public bool IsSupported
        {
            get { return !disposed && !initializationFailed; }
        }

        public string SupportMessage
        {
            get { return supportMessage; }
        }

        public void Initialize(SpeechRecognizerSettings recognizerSettings, Action<SpeechBackendEvent> sink)
        {
            settings = recognizerSettings;
            eventSink = sink;

            if (coroutineHost == null)
            {
                FailInitialization("Offline Quest speech recognition needs an active MonoBehaviour host.");
                return;
            }

            prepareCoroutine = coroutineHost.StartCoroutine(PrepareModel());
        }

        public void StartListening()
        {
            if (disposed || initializationFailed || capturing)
            {
                return;
            }

            if (!modelReady)
            {
                pendingStart = true;
                return;
            }

            StartMicrophoneCapture();
        }

        public void StopListening()
        {
            pendingStart = false;

            if (!capturing)
            {
                return;
            }

            string text = GetRecognizerText("getFinalResult", "text");
            StopCapture();
            Emit(SpeechBackendEvent.End());
            Emit(SpeechBackendEvent.Final(text));
        }

        public void CancelListening()
        {
            pendingStart = false;
            StopCapture();
        }

        public void Tick()
        {
            if (!capturing || microphoneClip == null || recognizer == null)
            {
                return;
            }

            int currentPosition = Microphone.GetPosition(null);
            if (currentPosition < 0)
            {
                FailSession(-23, "Quest microphone stopped unexpectedly.");
                return;
            }

            if (currentPosition != microphonePosition)
            {
                try
                {
                    if (currentPosition > microphonePosition)
                    {
                        ProcessMicrophoneFrames(microphonePosition, currentPosition - microphonePosition);
                    }
                    else
                    {
                        ProcessMicrophoneFrames(microphonePosition, microphoneClip.samples - microphonePosition);
                        ProcessMicrophoneFrames(0, currentPosition);
                    }

                    microphonePosition = currentPosition;
                }
                catch (Exception exception)
                {
                    FailSession(-24, "Offline speech decoder failed: " + exception.Message);
                    return;
                }
            }

        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pendingStart = false;

            if (prepareCoroutine != null && coroutineHost != null)
            {
                coroutineHost.StopCoroutine(prepareCoroutine);
                prepareCoroutine = null;
            }

            StopCapture();

            if (model != null)
            {
                try
                {
                    model.Call("close");
                }
                catch (Exception)
                {
                    // Disposal must not prevent the application from shutting down.
                }

                model.Dispose();
                model = null;
            }

            eventSink = null;
        }

        private IEnumerator PrepareModel()
        {
            string installedRoot = Path.Combine(Application.persistentDataPath, "VoskModels", ModelAssetRoot);
            string markerPath = Path.Combine(installedRoot, ".installed-" + ModelInstallVersion);

            if (!IsInstalledModelComplete(installedRoot, markerPath))
            {
                for (int index = 0; index < ModelFiles.Length; index++)
                {
                    string relativePath = ModelFiles[index];
                    string sourceUrl = Application.streamingAssetsPath.TrimEnd('/', '\\')
                        + "/" + ModelAssetRoot + "/" + relativePath;
                    string destinationPath = Path.Combine(installedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    string destinationDirectory = Path.GetDirectoryName(destinationPath);

                    if (!string.IsNullOrEmpty(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    using (UnityWebRequest request = UnityWebRequest.Get(sourceUrl))
                    {
                        yield return request.SendWebRequest();

                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            FailInitialization(
                                "Could not install the offline Quest speech model file '"
                                + relativePath + "': " + request.error);
                            yield break;
                        }

                        File.WriteAllBytes(destinationPath, request.downloadHandler.data);
                    }
                }

                File.WriteAllText(markerPath, ModelInstallVersion);
            }

            if (disposed)
            {
                yield break;
            }

            try
            {
                model = new AndroidJavaObject("org.vosk.Model", installedRoot);
                modelReady = true;
                prepareCoroutine = null;
            }
            catch (Exception exception)
            {
                FailInitialization(
                    "Could not load the offline Quest speech model. Ensure the Vosk Android libraries are included: "
                    + exception.Message);
                yield break;
            }

            if (pendingStart)
            {
                pendingStart = false;
                StartMicrophoneCapture();
            }
        }

        private static bool IsInstalledModelComplete(string root, string markerPath)
        {
            if (!File.Exists(markerPath))
            {
                return false;
            }

            for (int index = 0; index < ModelFiles.Length; index++)
            {
                string path = Path.Combine(root, ModelFiles[index].Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    return false;
                }
            }

            return true;
        }

        private void StartMicrophoneCapture()
        {
            try
            {
                int requestedSampleRate = Mathf.Clamp(settings.OfflineSampleRate, 8000, 48000);
                microphoneClip = Microphone.Start(null, true, MicrophoneBufferSeconds, requestedSampleRate);

                if (microphoneClip == null)
                {
                    throw new InvalidOperationException("Unity did not return a microphone AudioClip.");
                }

                // Android is allowed to substitute a hardware-supported rate.
                // Vosk must be told the AudioClip's actual rate, not only the request.
                recognizer = new AndroidJavaObject(
                    "org.vosk.Recognizer",
                    model,
                    (float)microphoneClip.frequency);

                microphonePosition = 0;
                lastPartial = string.Empty;
                userSpeaking = false;
                capturing = true;
                Emit(SpeechBackendEvent.Ready());
            }
            catch (Exception exception)
            {
                StopCapture();
                Emit(SpeechBackendEvent.Error(-22, "Could not start offline Quest speech recognition: " + exception.Message));
            }
        }

        private void ProcessMicrophoneFrames(int startFrame, int frameCount)
        {
            if (frameCount <= 0 || microphoneClip == null || recognizer == null)
            {
                return;
            }

            int channels = Mathf.Max(1, microphoneClip.channels);
            float[] samples = new float[frameCount * channels];
            if (!microphoneClip.GetData(samples, startFrame))
            {
                throw new InvalidOperationException("Could not read Quest microphone samples.");
            }

            byte[] pcm = new byte[frameCount * 2];
            for (int frame = 0; frame < frameCount; frame++)
            {
                float monoSample = 0f;
                int sampleOffset = frame * channels;

                for (int channel = 0; channel < channels; channel++)
                {
                    monoSample += samples[sampleOffset + channel];
                }

                monoSample = Mathf.Clamp(monoSample / channels, -1f, 1f);
                short pcmSample = (short)Mathf.RoundToInt(monoSample * short.MaxValue);
                pcm[frame * 2] = (byte)(pcmSample & 0xff);
                pcm[frame * 2 + 1] = (byte)((pcmSample >> 8) & 0xff);
            }

            bool endpoint = recognizer.Call<bool>("acceptWaveForm", pcm, pcm.Length);
            if (endpoint)
            {
                string text = GetRecognizerText("getResult", "text");
                CompleteRecognition(text);
                return;
            }

            string partial = GetRecognizerText("getPartialResult", "partial");
            if (!string.IsNullOrWhiteSpace(partial) && partial != lastPartial)
            {
                lastPartial = partial;

                if (!userSpeaking)
                {
                    userSpeaking = true;
                    Emit(SpeechBackendEvent.Beginning());
                }

                Emit(SpeechBackendEvent.Partial(partial));
            }
        }

        private void CompleteRecognition(string text)
        {
            bool wasSpeaking = userSpeaking;
            StopCapture();

            if (wasSpeaking)
            {
                Emit(SpeechBackendEvent.End());
            }

            Emit(SpeechBackendEvent.Final(text));
        }

        private void FailSession(int errorCode, string message)
        {
            bool wasSpeaking = userSpeaking;
            StopCapture();

            if (wasSpeaking)
            {
                Emit(SpeechBackendEvent.End());
            }

            Emit(SpeechBackendEvent.Error(errorCode, message));
        }

        private void StopCapture()
        {
            if (microphoneClip != null || capturing)
            {
                try
                {
                    Microphone.End(null);
                }
                catch (Exception)
                {
                    // The microphone may already have been released by Android.
                }
            }

            microphoneClip = null;
            microphonePosition = 0;
            capturing = false;
            userSpeaking = false;
            lastPartial = string.Empty;

            if (recognizer != null)
            {
                try
                {
                    recognizer.Call("close");
                }
                catch (Exception)
                {
                    // Keep cleanup best-effort after Android audio interruptions.
                }

                recognizer.Dispose();
                recognizer = null;
            }
        }

        private string GetRecognizerText(string methodName, string fieldName)
        {
            if (recognizer == null)
            {
                return string.Empty;
            }

            string json = recognizer.Call<string>(methodName);
            if (string.IsNullOrEmpty(json))
            {
                return string.Empty;
            }

            VoskResponse response = JsonUtility.FromJson<VoskResponse>(json);
            if (response == null)
            {
                return string.Empty;
            }

            return fieldName == "partial" ? response.partial : response.text;
        }

        private void FailInitialization(string message)
        {
            initializationFailed = true;
            modelReady = false;
            pendingStart = false;
            supportMessage = message;
            prepareCoroutine = null;
            Emit(SpeechBackendEvent.Error(-20, message));
        }

        private void Emit(SpeechBackendEvent speechEvent)
        {
            Action<SpeechBackendEvent> sink = eventSink;
            if (sink != null)
            {
                sink(speechEvent);
            }
        }

        [Serializable]
        private sealed class VoskResponse
        {
            public string text = string.Empty;
            public string partial = string.Empty;
        }
    }
}
#endif
