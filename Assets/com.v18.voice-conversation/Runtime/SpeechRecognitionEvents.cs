using System;
using UnityEngine.Events;

namespace V18.VoiceConversation
{
    [Serializable]
    public sealed class SpeechTextEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public sealed class SpeechStateEvent : UnityEvent<bool>
    {
    }

    [Serializable]
    public sealed class SpeechErrorEvent : UnityEvent<string>
    {
    }
}
