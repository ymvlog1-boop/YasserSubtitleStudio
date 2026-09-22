using Avalonia;

namespace Yasser.SubtitleDesktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
}
