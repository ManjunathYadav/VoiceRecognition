using System.Collections;
using UnityEngine;
using V18.VoiceConversation;

public sealed class BasicAvatarConversationResponder : MonoBehaviour
{
    [SerializeField] private AvatarSpeechGate avatarSpeechGate;
    [SerializeField] private AudioClip sampleAvatarReply;
    [SerializeField] [Min(0f)] private float replyDelaySeconds = 0.25f;
    [SerializeField] private bool logTranscript = true;

    private Coroutine replyRoutine;

    private void Reset()
    {
        avatarSpeechGate = FindObjectOfType<AvatarSpeechGate>();
    }

    public void OnUserSpeechComplete(string transcript)
    {
        TriggerSampleFunction(transcript);

        if (replyRoutine != null)
        {
            StopCoroutine(replyRoutine);
        }

        replyRoutine = StartCoroutine(PlayAvatarReplyAfterDelay());
    }

    public void TriggerSampleFunction(string transcript)
    {
        if (logTranscript)
        {
            Debug.Log("Sample function triggered after user stopped talking. Transcript: " + transcript, this);
        }
    }

    private IEnumerator PlayAvatarReplyAfterDelay()
    {
        if (replyDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(replyDelaySeconds);
        }

        if (avatarSpeechGate != null && sampleAvatarReply != null)
        {
            avatarSpeechGate.PlayAvatarAudio(sampleAvatarReply);
        }

        replyRoutine = null;
    }
}
