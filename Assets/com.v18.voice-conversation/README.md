# Quest Voice Conversation

Drop-in Unity Package Manager package for an XR Toolkit/Oculus Quest scene where:

- the headset user speaks,
- recognized speech is shown as text,
- a callback fires once the utterance is complete,
- recognition is blocked while the avatar audio is playing.

## Install

This repository already contains the package at:

`Assets/com.v18.voice-conversation`

To use it in another project, copy that folder into the target project's `Assets` folder.

The folder uses the Unity package layout but is embedded under `Assets` so its Android library and offline model are included automatically.

## Scene Setup

1. Create an empty scene object named `Voice Conversation`.
2. Add `Voice Conversation Recognizer`.
3. Add `Avatar Speech Gate` to the avatar audio object, or to the same `Voice Conversation` object.
4. Assign the recognizer to the gate.
5. Assign the avatar's `AudioSource` to the gate.
6. Add `Transcript Text Target` to your TextMeshPro or Unity UI text object.
7. Assign the recognizer to the text target.
8. Bind `VoiceConversationRecognizer.UserSpeechComplete(string)` to your chat/avatar function.

The sample script `BasicAvatarConversationResponder` shows the expected binding:

`UserSpeechComplete(string) -> BasicAvatarConversationResponder.OnUserSpeechComplete(string)`

## How The Flow Works

`VoiceConversationRecognizer` starts platform speech recognition and emits:

- `TranscriptUpdated(string)` for partial and final text.
- `FinalTranscript(string)` for final recognized text.
- `UserSpeechComplete(string)` after the recognizer has a final utterance.
- `RecognitionError(string)` for permission, device, or recognizer issues.

`AvatarSpeechGate` watches the avatar `AudioSource`.

- When avatar audio starts, it adds a recognition blocker and cancels the active recognition session.
- While audio is playing, recognizer results are ignored.
- When audio finishes, it removes the blocker and resumes recognition after `Resume Delay Seconds`.

## Quest Notes

Quest builds use the bundled Vosk recognizer and a small US-English model by default. Audio is decoded on the headset: no Google speech service, cloud key, or network connection is required. The Android library manifest requests `RECORD_AUDIO` and the recognizer asks for it at runtime.

On first launch, the app copies the bundled model (about 70 MB uncompressed) from the APK into the app's private storage, then loads it. This can take a few seconds. Later launches reuse that installed copy. The small model uses roughly 300 MB of memory while loaded.

The recognizer's **Quest Speech Backend** setting should remain **Offline Vosk** for Quest. **Android System** is retained as an opt-in compatibility option for ordinary Android devices that provide a system `RecognitionService`; Quest firmware normally does not.

The bundled model recognizes US English. To use another language, replace the files under `Runtime/Plugins/Android/VoiceConversation.androidlib/assets`, then update `ModelAssetRoot`, `ModelInstallVersion`, and `ModelFiles` in `VoskSpeechRecognizerBackend.cs`.

Recognition and model-loading errors are logged and are also sent through `RecognitionError`. `SpeakingStatusTextTarget` displays these errors in-headset, which makes device-only failures visible without Logcat.

## Editor Testing

### Windows Editor (Automatic)

On the Windows Unity Editor, real speech recognition is enabled automatically using Windows' built-in `DictationRecognizer`. Enter Play Mode and speak into your PC microphone — transcript events will fire just like on Quest.

**Requirement:** Online Speech Recognition must be enabled in **Windows Settings > Privacy > Speech**. If it is not enabled, the component will log a clear error message.

### Simulation Fallback (All Platforms)

If real speech recognition is not available (macOS/Linux editor, or Windows speech is disabled):

1. Select the object with `Voice Conversation Recognizer`.
2. Open the component context menu.
3. Choose `Simulate Recognized Speech`.

This triggers the same transcript and `UserSpeechComplete(string)` events, so you can test UI and avatar logic without a microphone.

