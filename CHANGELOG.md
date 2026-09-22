# Yasser Subtitle Studio development log

## v0.2.0-dev — 2026-09-22
- Added a standalone C# local transcription coordinator that extracts audio with FFmpeg and calls multilingual whisper.cpp with automatic language recognition per 30-second chunk.
- Added parser for Whisper JSON language and timestamp offsets.
- Added persisted source language/work IDs and safe generated-caption invalidation.
- Added console commands `transcribe-auto` and `export-transcript`.
- Added parser, checkpoint, invalidation and SRT export assertions to the C# smoke harness.
- No upstream desktop GUI integration, finished branding, installed models or Windows GUI installer yet. C# compilation and end-to-end Whisper execution remain unverified.

## v0.1.0-dev
- Checkpoint and manual subtitle editing/partial translation prototype.

## v0.3.0-dev — desktop preview source and recovery hardening
- Added independent Avalonia desktop preview with video, project, Whisper executable/model selectors, one-click automatic-language transcription/resume, cancellation, completed-cue display, and partial SRT export.
- Reopening last selected saved project pre-populates its media path.
- Fixed backup recovery so next save cannot copy a corrupt primary over the valid `.bak`; added source-level regression scenario.
- Windows build script now targets a desktop preview; not yet the upstream Subtitle Edit GUI.
