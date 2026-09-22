using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Yasser.SubtitleDesktop;

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var setup = new AutomaticToolSetup();
            string iconPath = Path.Combine(AppContext.BaseDirectory, "SE.ico");
            if (File.Exists(iconPath))
            {
                // The build embeds SE.ico in the executable and publishes the same file
                // alongside it for Avalonia's window chrome. Do not alter saved projects.
                using var iconStream = File.OpenRead(iconPath);
                setup.Icon = new WindowIcon(iconStream);
            }
            desktop.MainWindow = setup;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
