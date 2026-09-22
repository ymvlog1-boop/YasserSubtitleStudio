# Yasser Subtitle Studio — integration status (2026-09-22)

## Working standalone Windows preview

`src/Yasser.SubtitleDesktop` has passed Windows CI and the owner has confirmed the original-language speech-to-text, automatic spoken-language recognition, project saving, and SRT output on their Windows PC. Automatic first-run FFmpeg/Whisper/model provisioning and online Arabic text translation with per-cue resume were added to the independent preview. CI tests for online translation use a mock HTTP service; a real provider's accuracy, quotas and connectivity are not guaranteed. The online translator sends subtitle text, not source audio or video, to the external provider.

## Full original Subtitle Edit source integration (in development)

`scripts/build-integrated-host.ps1` fetches the exact upstream Subtitle Edit source revision `7398eb9d63769753960bb25326d4bae53971b55b`, preserves its original MIT license, original `SE.ico`, original Avalonia assembly identity and editor controls, adds `Yasser.ResumeCore` as a project reference, and inserts a Yasser menu into the original editor. That menu opens the existing Yasser transcription/translation/resume interface **as a separate companion window in the same original-editor process** and opens its automatic engine setup. This is a first source-level integration milestone, NOT complete integration of native subtitle rows or video-player state.

`.github/workflows/full-host-integration.yml` tests core checkpoints and online translation with a mock HTTP provider and attempts to compile the pinned full original editor on Windows. It deliberately publishes **no preview artifact or release**. Inspect its current GitHub Actions status rather than claiming it passed without checking it.

## Still required before a genuine final release

- Compile the full upstream original-host integration successfully and verify the real UI starts with the original icon on Windows.
- Connect original editor subtitle-row edits, selected video, player position and native translation features to `.yssproj` checkpoint records. The current project-window SRT is not automatically reflected in the upstream editor grid.
- Allow target-language selection and provide robust language-provider choice/errors/limits; current independent translator targets Arabic with a free third-party provider.
- Verify first-run tool installation, real speech recognition, online translation, cancellation/reopening/resume, original editor functionality, output fidelity and recovery in a genuine Windows GUI session.
- Ensure correct product branding without breaking upstream Avalonia XAML's `assembly=SubtitleEdit` resource references, package the full app, check third-party redistributable licenses and preserve all previously published releases.

**Do not call the intermediate build final or ask the owner to repeatedly install test previews.** Source attribution for the upstream icon and source is in `NOTICE-UPSTREAM.txt`.
