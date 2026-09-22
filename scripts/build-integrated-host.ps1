# INTERNAL integration: original Subtitle Edit main editor plus Yasser resumable project tools.
param([string]$UpstreamCommit = '7398eb9d63769753960bb25326d4bae53971b55b', [string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Replace-Once([string]$file, [string]$old, [string]$replacement) {
    $content = [System.IO.File]::ReadAllText($file)
    $index = $content.IndexOf($old, [StringComparison]::Ordinal)
    Require ($index -ge 0) "Upstream integration anchor changed: $file"
    Require ($content.IndexOf($old, $index + $old.Length, [StringComparison]::Ordinal) -lt 0) "Ambiguous upstream integration anchor: $file"
    [System.IO.File]::WriteAllText($file, $content.Replace($old, $replacement), [System.Text.UTF8Encoding]::new($false))
}
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$working = Join-Path $repository 'out/integrated-host-source'
$publish = Join-Path $repository 'out/integrated-host-win-x64'
New-Item -ItemType Directory -Force -Path (Join-Path $repository 'out') | Out-Null
Require (-not (Test-Path $working)) 'Refusing to overwrite a previous integrated source tree.'
New-Item -ItemType Directory -Force -Path $working | Out-Null
& git -C $working init --quiet
Require ($LASTEXITCODE -eq 0) 'Could not initialize upstream checkout.'
& git -C $working remote add origin 'https://github.com/SubtitleEdit/subtitleedit.git'
Require ($LASTEXITCODE -eq 0) 'Could not configure upstream remote.'
& git -C $working fetch --quiet --depth 1 origin $UpstreamCommit
Require ($LASTEXITCODE -eq 0) 'Could not fetch pinned original Subtitle Edit revision.'
& git -C $working checkout --quiet --detach FETCH_HEAD
Require ($LASTEXITCODE -eq 0) 'Could not check out upstream source.'
$actual = (& git -C $working rev-parse HEAD).Trim()
Require ($actual -eq $UpstreamCommit) 'Upstream SHA did not match pinned revision.'
$upstreamUI = Join-Path $working 'src/ui'
Require (Test-Path (Join-Path $upstreamUI 'UI.csproj')) 'Original Subtitle Edit UI project missing.'
Copy-Item -LiteralPath (Join-Path $repository 'src/Yasser.ResumeCore') -Destination (Join-Path $working 'src/Yasser.ResumeCore') -Recurse
$integrationDir = Join-Path $upstreamUI 'YasserIntegrated'
New-Item -ItemType Directory -Force -Path $integrationDir | Out-Null
$localUsings = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
'@
foreach ($name in @('MainWindow.cs', 'AutomaticToolSetup.cs')) {
    $sourceFile = Join-Path $repository "src/Yasser.SubtitleDesktop/$name"
    $targetFile = Join-Path $integrationDir $name
    # Local imports only: GLOBAL imports break original Subtitle Edit Timer and Vector types.
    [System.IO.File]::WriteAllText($targetFile, $localUsings + "`n" + [System.IO.File]::ReadAllText($sourceFile), [System.Text.UTF8Encoding]::new($false))
}
# Companion window must not take over the original Subtitle Edit main editor.
$setupFile = Join-Path $integrationDir 'AutomaticToolSetup.cs'
Replace-Once $setupFile 'if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)' '// Keep original editor as application MainWindow.'
Replace-Once $setupFile 'desktop.MainWindow = main;' '// Keep original editor window as owner.'
$menuSource = @'
using Avalonia.Controls;
using Yasser.SubtitleDesktop;

namespace Nikse.SubtitleEdit.Features.Main;

internal static class YasserIntegratedMenu
{
    internal static void Add(Menu hostMenu)
    {
        var yasser = new MenuItem { Header = "Yasser Subtitle Studio" };
        var open = new MenuItem { Header = "فتح نافذة التفريغ والترجمة والاستئناف" };
        open.Click += (_, _) =>
        {
            var tool = new Yasser.SubtitleDesktop.MainWindow();
            if (AutomaticToolSetup.TryLocate(out var paths))
            {
                SetInput(tool.Content, "المسار إلى whisper-cli.exe", paths.Whisper);
                SetInput(tool.Content, "المسار إلى نموذج Whisper متعدد اللغات (.bin)", paths.Model);
                SetInput(tool.Content, "ffmpeg", paths.Ffmpeg);
                SetInput(tool.Content, "ffprobe", paths.Ffprobe);
            }
            tool.Show();
        };
        var setup = new MenuItem { Header = "تنزيل محرك التعرف على الصوت ومكوناته" };
        setup.Click += (_, _) => new AutomaticToolSetup().Show();
        yasser.Items.Add(open);
        yasser.Items.Add(setup);
        hostMenu.Items.Add(yasser);
    }
    private static void SetInput(object? node, string match, string value)
    {
        if (node is TextBox text && (text.Watermark?.ToString() == match || text.Text == match))
        {
            text.Text = value;
            return;
        }
        if (node is ScrollViewer viewer) SetInput(viewer.Content, match, value);
        if (node is Panel panel) foreach (var child in panel.Children) SetInput(child, match, value);
        if (node is ContentControl control) SetInput(control.Content, match, value);
    }
}
'@
[System.IO.File]::WriteAllText((Join-Path $integrationDir 'YasserIntegratedMenu.cs'), $menuSource, [System.Text.UTF8Encoding]::new($false))
$projectFile = Join-Path $upstreamUI 'UI.csproj'
# Avalonia XAML explicitly references assembly=SubtitleEdit; preserve the original assembly identity.
Replace-Once $projectFile '<ProjectReference Include="..\libse\LibSE.csproj" />' ('<ProjectReference Include="..\libse\LibSE.csproj" />' + "`n`t  <ProjectReference Include=`"..\Yasser.ResumeCore\Yasser.ResumeCore.csproj`" />")
$viewFile = Join-Path $upstreamUI 'Features/Main/MainView.cs'
Replace-Once $viewFile 'InitMenu.Make(_vm);' ('InitMenu.Make(_vm);' + "`n        YasserIntegratedMenu.Add(_vm.Menu);")
Write-Host "Building original Subtitle Edit revision $actual with original icon and Yasser project menu."
& dotnet publish $projectFile --configuration Release --runtime $Runtime --self-contained true --output $publish
Require ($LASTEXITCODE -eq 0) 'Original editor host build failed.'
Require (Test-Path (Join-Path $publish 'SubtitleEdit.exe')) 'Integrated host EXE missing.'
Copy-Item -LiteralPath (Join-Path $working 'LICENSE') -Destination (Join-Path $publish 'UPSTREAM-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repository 'NOTICE-UPSTREAM.txt') -Destination (Join-Path $publish 'NOTICE-YASSER-UPSTREAM.txt')
Write-Host 'INTERNAL PINNED HOST BUILD PASSED; not a final release.'
