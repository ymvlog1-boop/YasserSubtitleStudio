# Builds the complete upstream Subtitle Edit application plus the Yasser project window.
# This is an INTERNAL integration build, not a final release or a replacement for the stable preview.
param(
    [string]$UpstreamCommit = '7398eb9d63769753960bb25326d4bae53971b55b',
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Replace-Once([string]$file, [string]$old, [string]$replacement) {
    $content = [System.IO.File]::ReadAllText($file)
    Require ($content.Contains($old)) "Upstream integration anchor changed: $file"
    $first = $content.IndexOf($old, [StringComparison]::Ordinal)
    Require ($content.IndexOf($old, $first + $old.Length, [StringComparison]::Ordinal) -lt 0) "Ambiguous upstream integration anchor: $file"
    [System.IO.File]::WriteAllText($file, $content.Replace($old, $replacement), [System.Text.UTF8Encoding]::new($false))
}

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$working = Join-Path $repository 'out/integrated-host-source'
$publish = Join-Path $repository 'out/integrated-host-win-x64'
New-Item -ItemType Directory -Force -Path (Join-Path $repository 'out') | Out-Null
Require (-not (Test-Path $working)) 'Refusing to overwrite an existing integrated source tree; clean the CI workspace first.'
New-Item -ItemType Directory -Force -Path $working | Out-Null
& git -C $working init --quiet
Require ($LASTEXITCODE -eq 0) 'Could not initialize upstream checkout.'
& git -C $working remote add origin 'https://github.com/SubtitleEdit/subtitleedit.git'
Require ($LASTEXITCODE -eq 0) 'Could not configure upstream remote.'
& git -C $working fetch --quiet --depth 1 origin $UpstreamCommit
Require ($LASTEXITCODE -eq 0) 'Could not fetch the pinned original Subtitle Edit revision.'
& git -C $working checkout --quiet --detach FETCH_HEAD
Require ($LASTEXITCODE -eq 0) 'Could not check out upstream source.'
$actual = (& git -C $working rev-parse HEAD).Trim()
Require ($actual -eq $UpstreamCommit) 'Upstream source revision did not match the pinned hash.'

$upstreamUI = Join-Path $working 'src/ui'
$upstreamCore = Join-Path $working 'src/Yasser.ResumeCore'
Require (Test-Path (Join-Path $upstreamUI 'UI.csproj')) 'Pinned upstream UI project not found.'
Copy-Item -LiteralPath (Join-Path $repository 'src/Yasser.ResumeCore') -Destination $upstreamCore -Recurse
$integratedDir = Join-Path $upstreamUI 'YasserIntegrated'
New-Item -ItemType Directory -Force -Path $integratedDir | Out-Null
foreach ($name in @('MainWindow.cs', 'AutomaticToolSetup.cs')) {
    Copy-Item -LiteralPath (Join-Path $repository "src/Yasser.SubtitleDesktop/$name") -Destination (Join-Path $integratedDir $name)
}
# Keep the original Subtitle Edit editor window as the application's MainWindow.
$setupFile = Join-Path $integratedDir 'AutomaticToolSetup.cs'
Replace-Once $setupFile 'if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)' 'if (false) // The upstream editor must remain the app MainWindow.'
Replace-Once $setupFile 'desktop.MainWindow = main;' '// The new project window is a companion of the original editor.'
$globalUsings = @'
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using Avalonia;
'@
[System.IO.File]::WriteAllText((Join-Path $integratedDir 'YasserGlobalUsings.cs'), $globalUsings, [System.Text.UTF8Encoding]::new($false))

$menuSource = @'
using Avalonia.Controls;
using Yasser.SubtitleDesktop;

namespace Nikse.SubtitleEdit.Features.Main;

/// <summary>Separate project window inside the full upstream Subtitle Edit process.</summary>
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
        if (node is Panel panel)
            foreach (var child in panel.Children) SetInput(child, match, value);
        if (node is ContentControl control) SetInput(control.Content, match, value);
    }
}
'@
[System.IO.File]::WriteAllText((Join-Path $integratedDir 'YasserIntegratedMenu.cs'), $menuSource, [System.Text.UTF8Encoding]::new($false))

$projectFile = Join-Path $upstreamUI 'UI.csproj'
Replace-Once $projectFile '<AssemblyName>SubtitleEdit</AssemblyName>' '<AssemblyName>YasserSubtitleStudio</AssemblyName>'
Replace-Once $projectFile '<ProjectReference Include="..\libse\LibSE.csproj" />' ('<ProjectReference Include="..\libse\LibSE.csproj" />' + "`n`t  <ProjectReference Include=`"..\Yasser.ResumeCore\Yasser.ResumeCore.csproj`" />")
$viewFile = Join-Path $upstreamUI 'Features/Main/MainView.cs'
Replace-Once $viewFile 'InitMenu.Make(_vm);' ('InitMenu.Make(_vm);' + "`n        YasserIntegratedMenu.Add(_vm.Menu);")

# Keep upstream copyright and MIT license in the produced distribution.
Copy-Item -LiteralPath (Join-Path $working 'LICENSE') -Destination (Join-Path $working 'UPSTREAM-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repository 'NOTICE-UPSTREAM.txt') -Destination (Join-Path $working 'NOTICE-YASSER-UPSTREAM.txt')
Write-Host "Compiling original Subtitle Edit at $actual with Yasser menu and separate resumable project window."
& dotnet publish $projectFile --configuration Release --runtime $Runtime --self-contained true --output $publish
Require ($LASTEXITCODE -eq 0) 'Full Subtitle Edit host build failed.'
Require (Test-Path (Join-Path $publish 'YasserSubtitleStudio.exe')) 'Integrated Windows host executable was not produced.'
Copy-Item -LiteralPath (Join-Path $working 'UPSTREAM-LICENSE.txt') -Destination (Join-Path $publish 'UPSTREAM-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $working 'NOTICE-YASSER-UPSTREAM.txt') -Destination (Join-Path $publish 'NOTICE-YASSER-UPSTREAM.txt')
Write-Host 'PINNED UPSTREAM HOST BUILD PASSED (internal CI only; not a final release).'
