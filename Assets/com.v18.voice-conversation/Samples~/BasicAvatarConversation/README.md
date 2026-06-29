# Basic Avatar Conversation Sample

1. Add `Voice Conversation Recognizer` to a scene object.
2. Add `Avatar Speech Gate` to the avatar or audio object, assign the recognizer and the avatar `AudioSource`.
3. Add `Transcript Text Target` to a TextMeshPro/Unity UI text object, then assign the recognizer.
4. Import this sample and add `BasicAvatarConversationResponder` to any scene object.
5. Bind `VoiceConversationRecognizer.UserSpeechComplete(string)` to `BasicAvatarConversationResponder.OnUserSpeechComplete(string)`.
6. Assign a sample avatar reply clip. While that clip plays, recognition is blocked.

In the Editor, use the recognizer component's context menu item **Simulate Recognized Speech** to test the UnityEvent flow.
