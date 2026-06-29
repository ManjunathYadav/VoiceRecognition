using UnityEngine;
using UnityEngine.Events;

namespace V18.VoiceConversation
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Voice Conversation/Avatar Speech Gate")]
    public sealed class AvatarSpeechGate : MonoBehaviour
    {
        [SerializeField] private VoiceConversationRecognizer recognizer;
        [SerializeField] private AudioSource avatarAudioSource;
        [SerializeField] private bool monitorAudioSource = true;
        [SerializeField] [Min(0f)] private float resumeDelaySeconds = 0.2f;

        public UnityEvent AvatarSpeechStarted = new UnityEvent();
        public UnityEvent AvatarSpeechFinished = new UnityEvent();

        private bool avatarSpeaking;
        private bool blockerApplied;

        public bool IsAvatarSpeaking
        {
            get { return avatarSpeaking; }
        }

        private void Reset()
        {
            recognizer = FindObjectOfType<VoiceConversationRecognizer>();
            avatarAudioSource = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            if (monitorAudioSource && avatarAudioSource != null && avatarAudioSource.isPlaying)
            {
                SetAvatarSpeaking(true);
            }
        }

        private void OnDisable()
        {
            ReleaseBlocker(0f);
            avatarSpeaking = false;
        }

        private void Update()
        {
            if (!monitorAudioSource || avatarAudioSource == null)
            {
                return;
            }

            bool isPlaying = avatarAudioSource.isPlaying;
            if (isPlaying != avatarSpeaking)
            {
                SetAvatarSpeaking(isPlaying);
            }
        }

        public void AvatarStartedSpeaking()
        {
            SetAvatarSpeaking(true);
        }

        public void AvatarFinishedSpeaking()
        {
            SetAvatarSpeaking(false);
        }

        public void PlayAvatarAudio(AudioClip clip)
        {
            if (avatarAudioSource == null)
            {
                Debug.LogWarning("AvatarSpeechGate needs an AudioSource before it can play avatar audio.", this);
                return;
            }

            if (clip != null)
            {
                avatarAudioSource.clip = clip;
            }

            SetAvatarSpeaking(true);
            avatarAudioSource.Play();
        }

        public void StopAvatarAudio()
        {
            if (avatarAudioSource != null)
            {
                avatarAudioSource.Stop();
            }

            SetAvatarSpeaking(false);
        }

        private void SetAvatarSpeaking(bool speaking)
        {
            if (avatarSpeaking == speaking)
            {
                return;
            }

            avatarSpeaking = speaking;

            if (avatarSpeaking)
            {
                ApplyBlocker();
                AvatarSpeechStarted.Invoke();
            }
            else
            {
                AvatarSpeechFinished.Invoke();
                ReleaseBlocker(resumeDelaySeconds);
            }
        }

        private void ApplyBlocker()
        {
            if (blockerApplied || recognizer == null)
            {
                return;
            }

            blockerApplied = true;
            recognizer.AddRecognitionBlocker();
        }

        private void ReleaseBlocker(float resumeDelay)
        {
            if (!blockerApplied || recognizer == null)
            {
                blockerApplied = false;
                return;
            }

            blockerApplied = false;
            recognizer.RemoveRecognitionBlockerAfter(resumeDelay);
        }
    }
}
