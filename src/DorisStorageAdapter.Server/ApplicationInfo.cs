using System.Diagnostics;

namespace DorisStorageAdapter.Server;

internal static class ApplicationInfo
{
    public static string Version { get; } =  
        FileVersionInfo
            .GetVersionInfo(typeof(ApplicationInfo).Assembly.Location)
            .ProductVersion ?? "";
}
