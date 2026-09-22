# Yasser.ResumeCore (development module)

A .NET 10, dependency-free checkpoint engine intended to be integrated into a **future** Subtitle Edit fork. **It is not integrated into the upstream editor and includes an external-tool whisper.cpp speech-recognition adapter; translation to a second language is not connected.**

- JSON `.yssproj` checkpoint with `.bak` recovery, stable cue IDs, progress and work statuses.
- Manual edits are protected from automated overwrites.
- Work is skipped when matching input/engine fingerprint has completed; interrupted tasks are eligible for retry.
- SRT partial export retains timestamps; audio transcription may use external FFmpeg and a multilingual whisper.cpp executable/model; upstream GUI wiring and translation adapters remain TODO.
- Does not save API keys or other credentials.

Add a ProjectReference to `src/Yasser.ResumeCore/Yasser.ResumeCore.csproj` in the fork's `src/ui/UI.csproj`, then use `ProjectService`, `ResumeEngine` in actual UI and translation handlers. The exact integration points must be adapted against the pinned upstream release and tested on Windows.

Auto language transcription backend: `AutoLanguageTranscription.cs` exposes `AutoLanguageTranscriber.RunAsync` with FFmpeg, FFprobe, and whisper-cli injected as paths; segments are automatically detected and checkpointed. Upstream UI integration is not present yet.
