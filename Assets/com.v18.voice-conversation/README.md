# Quest Voice Conversation

Drop-in Unity Package Manager package for an XR Toolkit/Oculus Quest scene where:

- the headset user speaks,
- recognized speech is shown as text,
- a callback fires once the utterance is complete,
- recognition is blocked while the avatar audio is playing.

## Install

This repository already contains the package at:

`Packages/com.v18.voice-conversation`

To use it in another project, copy that folder into the target project's `Packages` folder, or add it from Unity with:

`Window > Package Manager > + > Add package from disk...`

Then choose:

`Packages/com.v18.voice-conversation/package.json`

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

`VoiceConversationRecognizer` starts Android speech recognition and emits:

- `TranscriptUpdated(string)` for partial and final text.
- `FinalTranscript(string)` for final recognized text.
- `UserSpeechComplete(string)` after the recognizer has a final utterance.
- `RecognitionError(string)` for permission, device, or recognizer issues.

`AvatarSpeechGate` watches the avatar `AudioSource`.

- When avatar audio starts, it adds a recognition blocker and cancels the active recognition session.
- While audio is playing, recognizer results are ignored.
- When audio finishes, it removes the blocker and resumes recognition after `Resume Delay Seconds`.

## Quest Notes

This package uses Android's native `SpeechRecognizer` in Android device builds and includes a small Android library manifest that requests `RECORD_AUDIO`.

Important: some Quest OS/device configurations do not expose an Android speech recognition service. If `RecognitionError` says `Android SpeechRecognizer is not available`, the Unity side is wired correctly, but the headset does not have a native speech provider available. In that case, keep these scene components and replace the backend with Meta Voice SDK, Wit.ai, Azure, Google, or another STT provider.

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

