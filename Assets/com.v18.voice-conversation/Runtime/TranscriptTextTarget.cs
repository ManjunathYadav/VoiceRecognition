using System.Reflection;
using UnityEngine;

namespace V18.VoiceConversation
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Voice Conversation/Transcript Text Target")]
    public sealed class TranscriptTextTarget : MonoBehaviour
    {
        [SerializeField] private VoiceConversationRecognizer recognizer;
        [SerializeField] private Component textComponent;
        [SerializeField] private bool updateWithPartialTranscript = true;
        [SerializeField] private bool updateWithFinalTranscript = true;
        [SerializeField] private string initialText = string.Empty;

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

            if (!string.IsNullOrEmpty(initialText))
            {
                SetText(initialText);
            }

            if (recognizer == null)
            {
                return;
            }

            if (updateWithPartialTranscript)
            {
                recognizer.TranscriptUpdated.AddListener(SetText);
            }

            if (updateWithFinalTranscript && !updateWithPartialTranscript)
            {
                recognizer.FinalTranscript.AddListener(SetText);
            }
        }

        private void OnDisable()
        {
            if (recognizer == null)
            {
                return;
            }

            recognizer.TranscriptUpdated.RemoveListener(SetText);
            recognizer.FinalTranscript.RemoveListener(SetText);
        }

        public void SetText(string value)
        {
            if (!ResolveTextProperty())
            {
                if (!warnedAboutMissingTextTarget)
                {
                    warnedAboutMissingTextTarget = true;
                    Debug.LogWarning("TranscriptTextTarget could not find a writable string property named 'text'. Drag a TextMeshPro or Unity UI Text component into Text Component.", this);
                }

                return;
            }

            textProperty.SetValue(resolvedTextComponent, value, null);
        }

        public void Clear()
        {
            SetText(string.Empty);
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
