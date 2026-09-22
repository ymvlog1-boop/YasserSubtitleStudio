# Source integration status — v0.3.0-dev

## Implemented in this source package

A one-window **standalone desktop preview** (`src/Yasser.SubtitleDesktop`) uses the checkpointing core. Its `تحويل الكلام إلى نص / استئناف` button accepts media, `whisper-cli` and a multilingual model. It calls the real `AutoLanguageTranscriber`, displays completed cues, lets the user cancel, reopens the most recently selected saved project, and exports a partial SRT. This is not the pre-existing Subtitle Edit desktop window, and no runtime/model is bundled.

The core also now restores a validated `.bak` to the primary project file before returning a recovered project, preventing the next Save from copying a corrupt primary over the only good backup. A source regression test is included but cannot be run on this Linux container without the .NET SDK.

## NOT implemented

- Main-window menu/button injection into upstream Subtitle Edit; the full upstream repository was **not** downloaded in this environment (GitHub DNS unavailable inside the build container).
- Integration with upstream speech/translation UI state or its native engine/model selection.
- Full in-app subtitle manual-editing/video player.
- Translation from original language into another language (this prototype transcribes in original language).
- A tested Windows EXE / finished installer / published GitHub release. The code has not been compiled here.

## Exact upstream integration path

1. Clone upstream into its own git repository at a pinned release/commit. Preserve original attribution and third-party licenses.
2. Add `src/Yasser.ResumeCore` to upstream and a project reference to `src/ui/UI.csproj`.
3. Extend `src/ui/Features/Video/SpeechToText` view model's speech-to-text initiation command so that when language == Auto it passes the loaded media and installed multilingual model settings to the resumable backend. Do not patch by blind string replacement: confirm the host method signatures at the pinned revision first.
4. Use existing menu/view command registrations in `src/ui/Features/Main` to expose `استئناف المشروع` and `تصدير SRT جزئي`; wire media changes and subtitle-row edits to `ProjectStore` and `ResumeEngine` with stable cue-ID mapping and cancellation on project switch.
5. Run the source regression tests and test the GUI on Windows with a real multilingual model, then create an actual release without removing older releases.

Run `scripts/build-and-test-windows.ps1` on a Windows PC with .NET 10 SDK to compile/test/publish the **standalone preview**. You must additionally provide FFmpeg/FFprobe and `whisper-cli` plus a multilingual model to use automatic transcription. Inputs stay local unless you independently configure an external service.
