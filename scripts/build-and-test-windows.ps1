$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $root
try {
  dotnet --version
  dotnet run --project .\tests\Yasser.ResumeCore.SmokeTests\Yasser.ResumeCore.SmokeTests.csproj -c Release
  if ($LASTEXITCODE -ne 0) { throw 'Smoke tests failed' }
  dotnet publish .\src\Yasser.SubtitleDesktop\Yasser.SubtitleDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\out\win-x64-desktop
  if ($LASTEXITCODE -ne 0) { throw 'Desktop preview publish failed' }
  Write-Host 'Desktop PREVIEW in out\win-x64-desktop; not integrated into the upstream Subtitle Edit window.'
} finally { Pop-Location }
