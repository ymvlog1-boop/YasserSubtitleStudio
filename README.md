# Yasser Subtitle Studio — development starter, v0.3.0-dev

This is a **.NET 10 checkpoint/transcription module, a console prototype, and source for an independent desktop preview**, NOT the full Subtitle Edit fork or a compiled Windows GUI/EXE download. It is intentionally not represented as an installed change to Subtitle Edit. The v0.2 console command can invoke externally installed FFmpeg and multilingual whisper.cpp; no speech engine or model is bundled.

## Implemented in source

- Import SRT; create local `.yssproj` project with stable cue IDs.
- Save/reload playback position, selected cue, human edits and per-cue translation checkpoints.
- Mark interrupted processing as retryable; only queue uncached or mismatched translation work.
- Split transcription into independent time ranges, call installed FFmpeg and whisper.cpp with automatic per-chunk language recognition, and checkpoint timestamp-bounded output.
- Export a partial SRT containing currently completed translations without overwriting original SRT.
- One backup per project (`.bak`), temporary file before replacement, restore backup if primary project is corrupt.
- Console smoke-test harness without extra NuGet test dependencies.

## Upstream source and version

- Upstream: https://github.com/SubtitleEdit/subtitleedit
- Upstream `main` snapshot inspected on 2026-09-22: `7398eb9d63769753960bb25326d4bae53971b55b`.
- Upstream `src/ui/UI.csproj` targets .NET 10 and Avalonia, with `AssemblyName=SubtitleEdit` and `ApplicationIcon=SE.ico` at the inspected snapshot.
- IMPORTANT: The ZIP does **not** include the upstream repository or its binaries. This is the original add-on module only; clone the upstream separately and pin a release/commit when making the actual fork.

## Build / manual test on Windows

Install .NET 10 SDK. In PowerShell, from this folder, run:

```powershell
.\scripts\build-and-test-windows.ps1
```

This runs the C# smoke tests and publishes the **independent Avalonia desktop preview** to `out\win-x64-desktop`. It is not integrated with the original Subtitle Edit main window.

## Prototype commands

```powershell
dotnet run --project .\src\Yasser.ResumeCli\Yasser.ResumeCli.csproj -- init ".\example.yssproj" ".\input.srt" "C:\Videos\wedding.mp4"
dotnet run --project .\src\Yasser.ResumeCli\Yasser.ResumeCli.csproj -- status ".\example.yssproj"
# Copy a cue UUID displayed in status, then manually edit it:
dotnet run --project .\src\Yasser.ResumeCli\Yasser.ResumeCli.csproj -- edit ".\example.yssproj" "<cue-guid>" "ترجمة مكتملة"
dotnet run --project .\src\Yasser.ResumeCli\Yasser.ResumeCli.csproj -- export-partial ".\example.yssproj" ".\wedding.partial.srt"
```

`queue` and `complete` are also available to demonstrate mocked / externally supplied automatic translation checkpoints; **automatic translation into a target language is not yet connected**.

## Integration work remaining before shipping a Subtitle Edit fork

1. Clone upstream into the user-owned new repository and pin the base commit; preserve upstream copyright/license notices.
2. Copy `src/Yasser.ResumeCore` under upstream `src/`; add `<ProjectReference Include="..\Yasser.ResumeCore\Yasser.ResumeCore.csproj" />` to upstream `src/ui/UI.csproj`.
3. Add actual UI menu commands, project-open recovery dialog, event subscriptions and debounced save into upstream MainViewModel and subtitle-row editing logic. Map native subtitle models to stable cue IDs; protect changes made in the editor after a provider request starts.
4. Add explicit job adapters to upstream auto-translation and speech-to-text tasks. Validate engine settings and input fingerprint before applying each asynchronous response; cancel in-flight tasks on project switch; do not reprocess completed matching units.
5. Integrate the new audio-slicing/transcription backend with upstream UI and its downloaded engine/model paths; evaluate overlapping speech, mixed languages, diarization, and timing on real media.
6. Add genuine logo/icon assets supplied or approved by the owner; set assembly title/icon/product branding and check redistribution license obligations of included third-party dependencies.
7. Build and verify GUI and media playback on Windows; publish tested installers/releases, preserving all older releases.

## Limitations and safety

Not tested with the .NET compiler or a real Whisper model in this environment: .NET SDK and whisper.cpp were not installed here. The smoke tests are provided for execution on a Windows development machine. Backups are a best-effort recovery aid, not a substitute for external backup or full crash-safety on every filesystem. **Do not store API secrets in the project JSON.** Never overwrite the input subtitle file using a partial export.

## Repository publishing

Intended destination: `ymvlog1-boop/YasserSubtitleStudio`. That repository did not exist when checked, and the currently available GitHub connector does not offer a `create repository` operation. Create a distinct empty repository in the user's GitHub account; do NOT push this work to unrelated existing repositories. Retain tags and releases after each update.

## v0.2.0-dev: automatic language recognition with real local tools (source code)

**Implemented in this development module, not yet wired to the Subtitle Edit desktop UI:**
- `AutoLanguageTranscriber` slices a media file using an installed FFmpeg, invokes a locally installed multilingual `whisper-cli` with `-l auto -ojf` for every ≤30s chunk, and parses JSON offsets and recognized language per chunk. Output is transcribed in the **original language** (the `--translate` flag is never supplied).
- Every successful recognition chunk is saved immediately in the `.yssproj` file. A restart queues missing chunks only. Language code, source work ID and timestamps are persisted with each cue. Changing media content or engine settings invalidates prior generated chunks; manually edited chunks are protected from destructive replacement.
- `export-transcript` exports the currently completed original-language transcript to a separate partial SRT, without overwriting input media, source SRT or project file.
- The code uses FFmpeg/FFprobe and whisper.cpp as *external* executables. Their binaries, model files and licenses are **not included** in this ZIP. Use a **multilingual** Whisper model, not `*.en.bin`.
- Local language recognition is probabilistic, not guaranteed for every language/dialect or short/noisy clip. Language changes can be recognized by 30s segment, not at each individual word. For accurate Arabic/Kurdish evaluation, use representative recorded samples and a suitable multilingual model; unsupported Kurdish dialects may be misidentified.

### Prototype use on Windows after installing .NET 10 SDK, FFmpeg, and whisper.cpp

```powershell
# This is a console prototype; the GUI button does not exist yet.
dotnet run --project .\src\Yasser.ResumeCli\Yasser.ResumeCli.csproj -- transcribe-auto ".\wedding.yssproj" "C:\Videos\wedding.mp4" "C:\whisper\whisper-cli.exe" "C:\whisper\models\ggml-large-v3.bin" "C:\ffmpeg\bin\ffmpeg.exe" "C:\ffmpeg\bin\ffprobe.exe"
# Rerun the same command to resume missing checkpoints.
dotnet run --project .\src\Yasser.ResumeCli\Yasser.ResumeCli.csproj -- export-transcript ".\wedding.yssproj" ".\wedding.partial.srt"
```

FFmpeg and whisper-cli are invoked with an argument list, not through a shell. No online service or secret is required. This implementation has not been run end-to-end against an installed Whisper model in this environment.

**Next integration milestone:** hook the SpeechToText view/menu command in upstream `src/ui/Features/Video/SpeechToText` to this coordinator with the engine/model paths managed by Subtitle Edit; add configurable automatic default and clear language uncertainty warnings; test model/runtime and UI on Windows before distributing an EXE.

## v0.3.0-dev: desktop preview source (not yet merged into Subtitle Edit)

Added `src/Yasser.SubtitleDesktop`, an Avalonia desktop application with a real one-click transcription/resume button backed by the same checkpoint core, FFmpeg, and `whisper-cli` (you must install binaries and a multilingual model yourself). You can select a video and existing project, see saved captions, cancel processing, and export partial SRT. The last project path is remembered locally. Run `scripts/build-and-test-windows.ps1` to compile the preview on Windows with .NET 10 SDK and NuGet access. See `INTEGRATION-STATUS.md` for what remains before this becomes a change **inside** Subtitle Edit itself. No compiled `.exe` or original upstream source is included.
