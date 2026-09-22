# Windows preview: build and download

This repository currently contains a **standalone development preview**, not the full Subtitle Edit fork. The first Windows CI build passed the existing core smoke tests and published a self-contained win-x64 preview artifact on 2026-09-22:

- [Build run and preview download](https://github.com/ymvlog1-boop/YasserSubtitleStudio/actions/runs/35675136191)
- Workflow definition: `.github/workflows/windows-preview.yml`.

## Download and test

1. Sign in to GitHub, open the build-run link, scroll to **Artifacts**, and download `YasserSubtitleStudio-standalone-preview-win-x64`.
2. Extract the ZIP to a regular folder on Windows 10/11 x64. Open `YasserSubtitleStudio.exe` from that folder. Do not run it inside the ZIP.
3. For automatic speech transcription you must separately provide FFmpeg/FFprobe, `whisper-cli.exe`, and a compatible multilingual Whisper model. The program does not bundle them or fetch a model automatically; a `.en.bin` English-only model will not provide multilingual recognition.
4. Select your media and a `.yssproj` project path, select the Whisper executable and model, then press the speech-to-text/resume button. After at least one successful chunk, you can export the completed portion as SRT and reopen the same project later to continue.

**Important:** This GitHub Actions artifact is a *temporary preview build*, not a signed installer, a tested end-to-end transcription product, or an official Release. Automated tests verify the checkpoint core and build, not recognition accuracy, audio/model installation, or actual GUI behavior on the user's PC. The source is not yet integrated into the original Subtitle Edit window. Please preserve `.yssproj` and your source media when testing; do not rely on an experimental preview for irreplaceable work. GitHub Actions artifacts have limited retention; subsequent builds create new artifacts without deleting earlier source commits or published releases.
