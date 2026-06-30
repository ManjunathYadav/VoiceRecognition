using System.Reflection;
using UnityEngine;

namespace V18.VoiceConversation
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Voice Conversation/Speaking Status Text Target")]
    public sealed class SpeakingStatusTextTarget : MonoBehaviour
    {
        [SerializeField] private VoiceConversationRecognizer recognizer;
        [SerializeField] private Component textComponent;
        [SerializeField] private string stoppedTalkingText = "User stopped talking...";

        private PropertyInfo textProperty;
        private Component resolvedTextComponent;
        private bool warnedAboutMissingTextTarget;

        private void Reset()
        {
            recognizer = FindObjectOfType<VoiceConversationRecognizer>();
            textComponent = FindWritableTextComponent();
        }

        private void OnEnable()
        {
            if (recognizer == null)
            {
                recognizer = FindObjectOfType<VoiceConversationRecognizer>();
            }

            if (recognizer == null)
            {
                return;
            }

            recognizer.UserSpeakingStateChanged.AddListener(OnUserSpeakingStateChanged);
            recognizer.TranscriptUpdated.AddListener(OnTranscriptUpdated);
            recognizer.FinalTranscript.AddListener(OnFinalTranscript);
            recognizer.RecognitionError.AddListener(OnRecognitionError);
        }

        private void OnDisable()
        {
            if (recognizer == null)
            {
                return;
            }

            recognizer.UserSpeakingStateChanged.RemoveListener(OnUserSpeakingStateChanged);
            recognizer.TranscriptUpdated.RemoveListener(OnTranscriptUpdated);
            recognizer.FinalTranscript.RemoveListener(OnFinalTranscript);
            recognizer.RecognitionError.RemoveListener(OnRecognitionError);
        }

        private void OnUserSpeakingStateChanged(bool isSpeaking)
        {
            if (isSpeaking)
            {
                SetText(string.Empty);
            }
            else
            {
                SetText(stoppedTalkingText);
            }
        }

        private void OnTranscriptUpdated(string transcript)
        {
            // Clear the status text while the user is actively speaking.
            // This covers cases where BeginningOfSpeech was not emitted.
            SetText(string.Empty);
        }

        private void OnFinalTranscript(string transcript)
        {
            // Always show the stopped text when a final result arrives.
            // This covers cases where EndOfSpeech was not emitted.
            SetText(stoppedTalkingText);
        }

        private void OnRecognitionError(string message)
        {
            SetText(message);
        }

        private void SetText(string value)
        {
            if (!ResolveTextProperty())
            {
                if (!warnedAboutMissingTextTarget)
                {
                    warnedAboutMissingTextTarget = true;
                    Debug.LogWarning("SpeakingStatusTextTarget could not find a writable string property named 'text'. Drag a TextMeshPro or Unity UI Text component into Text Component.", this);
                }

                return;
            }

            textProperty.SetValue(resolvedTextComponent, value, null);
        }

        private bool ResolveTextProperty()
        {
            if (textComponent == null)
            {
                textComponent = FindWritableTextComponent();
            }

            if (textComponent == null)
            {
                return false;
            }

            if (resolvedTextComponent == textComponent && textProperty != null)
            {
                return true;
            }

            PropertyInfo property = textComponent.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(string) || !property.CanWrite)
            {
                return false;
            }

            resolvedTextComponent = textComponent;
            textProperty = property;
            return true;
        }

        private Component FindWritableTextComponent()
        {
            Component[] components = GetComponents<Component>();
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null || component == this)
                {
                    continue;
                }

                PropertyInfo property = component.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (property != null && property.PropertyType == typeof(string) && property.CanWrite)
                {
                    return component;
                }
            }

            return null;
        }
    }
}
