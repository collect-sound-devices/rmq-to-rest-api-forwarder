using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace RmqToRestApiForwarder.Utils;

internal class VersionProvider : IVersionProvider
{
    public VersionProvider()
        : this(ReadVersionFromAssembly(), GetRuntimeDescription(), GetAppName())
    {
    }

    private VersionProvider((string? CodeVersion, string? LastCommitDate) version, string runtime, string appName)
    {
        AppName = appName;
        Runtime = runtime;
        CodeVersion = version.CodeVersion ?? "UnknownVersion";
        LastCommitDate = version.LastCommitDate ?? "UnknownDate";
    }

    public string AppName { get; }
    public string Runtime { get; }
    public string CodeVersion { get; }
    public string LastCommitDate { get; }

    private static (string? Version, string? LastCommitDate) ReadVersionFromAssembly()
    {
        var assembly = typeof(VersionProvider).Assembly;
        var versionAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var lastCommitDateAttribute = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>();
        return (versionAttribute?.InformationalVersion, lastCommitDateAttribute?.Description);
    }

    private static string GetRuntimeDescription()
    {
        var os = GetOsName();

        var cloud = DetectCloudEnvironment();

        var dotnetVersion = RuntimeInformation.FrameworkDescription;

        return $"{dotnetVersion} on {os}{(string.IsNullOrEmpty(cloud) ? "" : $" ({cloud})")} ";
    }

    private static string GetOsName()
    {
        if (OperatingSystem.IsWindows())
        {
            return GetWindowsOsName();
        }

        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (OperatingSystem.IsLinux())
        {
            return GetLinuxOsName();
        }

        return "Unknown OS";
    }

    [SupportedOSPlatform("windows")]
    private static string GetWindowsOsName()
    {
        const string fallback = "Windows, no version info";

        try
        {
            var (buildNumber, ubr, productName, displayVersion) = ReadWindowsRegistryValues();

            // On old Win11 builds (22000.x), ProductName still reads "Windows 10 ..."
            if (buildNumber >= 22000 && productName.StartsWith("Windows 10", StringComparison.Ordinal))
            {
                productName = productName.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
            }

            if (!string.IsNullOrEmpty(displayVersion))
            {
                productName += $" {displayVersion}";
            }

            return $"{productName} Build {buildNumber}.{ubr}";
        }
        catch (Exception ex)
        {
            return $"{fallback} ({ex.Message})";
        }
    }

    [SupportedOSPlatform("windows")]
    private static (int BuildNumber, int Ubr, string ProductName, string DisplayVersion) ReadWindowsRegistryValues()
    {
        using var key = Registry.LocalMachine
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")
            ?? throw new InvalidOperationException("Registry key SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion not found.");

        return (
            Environment.OSVersion.Version.Build,
            key.GetValue("UBR") as int? ?? 0,
            key.GetValue("ProductName") as string ?? string.Empty,
            key.GetValue("DisplayVersion") as string ?? string.Empty
        );
    }

    private static string GetLinuxOsName()
    {
        const string fallback = "Linux, no version info";

        try
        {
            var distroName = string.Empty;
            var distroVersion = string.Empty;

            if (File.Exists("/etc/os-release"))
            {
                var fields = File.ReadAllLines("/etc/os-release")
                    .Select(line => line.Split('=', 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0], parts => parts[1].Trim('"'));

                fields.TryGetValue("NAME", out distroName!);
                fields.TryGetValue("VERSION_ID", out distroVersion!);
                distroName ??= string.Empty;
                distroVersion ??= string.Empty;
            }

            var kernelRelease = RuntimeInformation.OSDescription
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ElementAtOrDefault(1) ?? string.Empty;

            if (!string.IsNullOrEmpty(distroName) && !string.IsNullOrEmpty(distroVersion))
            {
                return string.IsNullOrEmpty(kernelRelease)
                    ? $"{distroName} {distroVersion}"
                    : $"{distroName} {distroVersion} {kernelRelease}";
            }

            return string.IsNullOrEmpty(kernelRelease)
                ? fallback
                : $"Linux, kernel {kernelRelease}";
        }
        catch (Exception ex)
        {
            return $"{fallback} ({ex.Message})";
        }
    }

    private static string DetectCloudEnvironment()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")))
        {
            return "Microsoft Azure";
        }

        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CODESPACES")))
        {
            return "GitHub Codespace";
        }

        return string.Empty;
    }

    private static string GetAppName()
    {
        return typeof(VersionProvider).Assembly.GetName().Name ?? "UnknownAppName";
    }
}
