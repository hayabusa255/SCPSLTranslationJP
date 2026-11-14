using System.Reflection;

namespace SCPSLInstaller;

internal static class AppInfo
{
    public static string Version { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "2.0.0")
        .Split('+')[0];
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int Error = 1;
    public const int BadArgs = 2;
    public const int GameNotFound = 3;
    public const int Cancelled = 130;
}
