using System.Reflection;

namespace DorisStorageAdapter.Server;

internal static class ApplicationInfo
{
    public static string Version { get; } =
        typeof(ApplicationInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "";
}
