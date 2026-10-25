using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;

namespace PadForge.G7Pro.Launcher;

internal static class NativeMaintenance
{
    private const string TaskName = "PadForge G7 Pro Launcher";
    private const string ServiceExecutableName = "GameSirG7ProTelemetry.exe";

    public static void InstallService(Paths paths)
    {
        if (Program.GetServiceStatus() != "Not installed")
            throw new InvalidOperationException("The telemetry service is already installed.");
        ValidateFile(paths.TelemetryExecutable, "telemetry service executable");
        Run(paths.TelemetryExecutable, "--control-self-test");
        Run(paths.TelemetryExecutable, "--frame-self-test");

        string installDirectory = ServiceDirectory();
        string installedExecutable = Path.Combine(installDirectory, ServiceExecutableName);
        PrepareInstallDirectory(installDirectory);
        Directory.CreateDirectory(installDirectory);
        try
        {
            string userSid = WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("The current Windows account has no security identifier.");
            Run(SystemTool("icacls.exe"), installDirectory, "/inheritance:r", "/grant:r", "*S-1-5-18:(OI)(CI)F", "*S-1-5-32-544:(OI)(CI)F", $"*{userSid}:(OI)(CI)RX");

            File.Copy(paths.TelemetryExecutable, installedExecutable, overwrite: false);
            if (!HashesMatch(paths.TelemetryExecutable, installedExecutable))
                throw new InvalidOperationException("Installed telemetry executable hash mismatch.");
            File.WriteAllText(Path.Combine(installDirectory, "client.sid"), userSid, System.Text.Encoding.ASCII);

            string serviceCommand = $"\"{installedExecutable}\" --service";
            Run(SystemTool("sc.exe"), "create", Program.ServiceName, "binPath=", serviceCommand, "start=", "auto", "DisplayName=", "GameSir G7 Pro SDL telemetry");
            Run(SystemTool("sc.exe"), "sdset", Program.ServiceName, $"D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;LCRPWP;;;{userSid})");
            Program.StartService();
        }
        catch
        {
            if (Program.GetServiceStatus() != "Not installed") Run(SystemTool("sc.exe"), allowFailure: true, "delete", Program.ServiceName);
            TryDeleteKnownServiceFiles(installDirectory);
            throw;
        }
    }

    public static void UninstallService()
    {
        if (Program.GetServiceStatus() == "Not installed") return;

        string installDirectory = ServiceDirectory();
        string installedExecutable = Path.Combine(installDirectory, ServiceExecutableName);
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{Program.ServiceName}");
        string? imagePath = key?.GetValue("ImagePath") as string;
        string? account = key?.GetValue("ObjectName") as string;
        string expectedCommand = $"\"{installedExecutable}\" --service";
        if (!string.Equals(imagePath, expectedCommand, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(account, "LocalSystem", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected service configuration. Refusing removal.");

        Program.StopService();
        Run(SystemTool("sc.exe"), "delete", Program.ServiceName);
        TryDeleteKnownServiceFiles(installDirectory);
    }

    public static void InstallAutostart(Paths paths)
    {
        ValidateFile(paths.PadForge, "official PadForge executable");
        string launcher = Path.Combine(paths.BaseDirectory, "LaunchPadForge.exe");
        ValidateFile(launcher, "launcher executable");
        string user = WindowsIdentity.GetCurrent().Name;
        string taskCommand = $"{Quote(launcher)} --autostart --padforge {Quote(paths.PadForge)}";
        Run(SystemTool("schtasks.exe"), "/Create", "/SC", "ONLOGON", "/TN", TaskName, "/TR", taskCommand, "/RU", user, "/RL", "HIGHEST", "/IT", "/F");
    }

    public static void UninstallAutostart()
    {
        if (!Program.IsAutostartInstalled()) return;
        Run(SystemTool("schtasks.exe"), "/Delete", "/TN", TaskName, "/F");
    }

    private static string ServiceDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Program.ServiceName);
    private static string SystemTool(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static bool HashesMatch(string first, string second)
    {
        using FileStream firstStream = File.OpenRead(first);
        using FileStream secondStream = File.OpenRead(second);
        return SHA256.HashData(firstStream).AsSpan().SequenceEqual(SHA256.HashData(secondStream));
    }

    private static void ValidateFile(string path, string description)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"The {description} was not found.", path);
    }

    private static void TryDeleteKnownServiceFiles(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string file in Directory.EnumerateFiles(directory, "GameSirG7ProTelemetry.exe.previous-*")) File.Delete(file);
        foreach (string name in new[] { ServiceExecutableName, "client.sid" })
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path)) File.Delete(path);
        }
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private static void PrepareInstallDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;

        string[] unexpected = Directory.EnumerateFileSystemEntries(directory)
            .Where(path => !IsManagedServiceFile(path))
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
        if (unexpected.Length > 0)
        {
            throw new InvalidOperationException(
                $"The stale telemetry directory contains files not managed by this launcher, so it was left unchanged: {directory}{Environment.NewLine}{string.Join(", ", unexpected)}");
        }

        // A previous uninstall or interrupted install can leave the protected
        // directory behind after the service registration has disappeared.
        // Remove only our known payload and rebuild it with the current files.
        TryDeleteKnownServiceFiles(directory);
    }

    private static bool IsManagedServiceFile(string path)
    {
        if (!File.Exists(path)) return false;
        string name = Path.GetFileName(path);
        return name.Equals(ServiceExecutableName, StringComparison.OrdinalIgnoreCase) ||
               name.Equals("client.sid", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("GameSirG7ProTelemetry.exe.previous-", StringComparison.OrdinalIgnoreCase);
    }

    private static void Run(string executable, params string[] arguments) => Run(executable, allowFailure: false, arguments);

    private static void Run(string executable, bool allowFailure, params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Windows did not start {Path.GetFileName(executable)}.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (!allowFailure && process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException($"{Path.GetFileName(executable)} failed with exit code {process.ExitCode}. {detail.Trim()}");
        }
    }
}
