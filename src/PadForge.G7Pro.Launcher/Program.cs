using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace PadForge.G7Pro.Launcher;

internal static class Program
{
    internal const string ServiceName = "GameSirG7ProTelemetry";
    private const string HookVariable = "DOTNET_STARTUP_HOOKS";
    private const string OverrideVariable = "PADFORGE_SDL3_OVERRIDE";
    private const string LogVariable = "PADFORGE_G7PRO_HOOK_LOG";
    private const string VerifyVariable = "PADFORGE_G7PRO_VERIFY_HOOK";

    [STAThread]
    private static int Main(string[] args)
    {
        Options options;
        try { options = Options.Parse(args); }
        catch (Exception error) { ShowError(error); return 1; }

        Paths paths = Paths.Resolve(options);
        if (!options.RunImmediately)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new LauncherForm(paths));
            return 0;
        }
        return Launch(paths, options, showErrors: !options.AutoStart);
    }

    internal static int Launch(Paths paths, Options options, bool showErrors = true)
    {
        try
        {
            string logPath = PrepareLog(paths, options);
            ValidateLaunchFiles(paths);
            if (!options.SkipService) EnsureServiceRunning();

            WriteLog(logPath, $"PadForge={paths.PadForge}");
            WriteLog(logPath, $"StartupHook={paths.StartupHook}");
            WriteLog(logPath, $"SDL3={paths.Sdl} SHA256={Hash(paths.Sdl)}");
            if (options.DryRun)
            {
                WriteLog(logPath, "Dry-run validation completed successfully.");
                ShowInformation($"Validation succeeded.\n\nLog: {logPath}", options);
                return 0;
            }

            using Process process = StartPadForge(paths, options, logPath);
            WriteLog(logPath, $"Started PadForge pid={process.Id} verify={options.VerifyHook}.");
            if (options.VerifyHook || options.Wait)
            {
                process.WaitForExit();
                WriteLog(logPath, $"PadForge exited with code {process.ExitCode}.");
                if (process.ExitCode != 0) throw new InvalidOperationException($"PadForge exited with code {process.ExitCode}. See {logPath}");
            }
            if (options.VerifyHook)
            {
                string log = File.ReadAllText(logPath);
                if (!log.Contains("Redirected SDL3", StringComparison.Ordinal) || !log.Contains("Verification completed successfully", StringComparison.Ordinal))
                    throw new InvalidOperationException("The official PadForge process exited without proving the SDL redirect.");
                ShowInformation($"The official PadForge build loaded the custom SDL successfully.\n\nLog: {logPath}", options);
            }
            return 0;
        }
        catch (Exception error)
        {
            LogError(error);
            if (showErrors) ShowError(error);
            return 1;
        }
    }

    internal static void ValidateLaunchFiles(Paths paths)
    {
        ValidateFile(paths.PadForge, "official PadForge executable");
        ValidateFile(paths.StartupHook, "startup hook");
        ValidateFile(paths.Sdl, "custom SDL3 library");
        ValidateArchitecture(paths.PadForge, 0x8664, "PadForge.exe");
        ValidateArchitecture(paths.Sdl, 0x8664, "SDL3.dll");
    }

    internal static string GetServiceStatus()
    {
        using ServiceHandle manager = ServiceHandle.OpenManager();
        using ServiceHandle? service = manager.TryOpenService(ServiceName);
        return service?.StatusText ?? "Not installed";
    }

    internal static void StartService()
    {
        using ServiceHandle manager = ServiceHandle.OpenManager();
        using ServiceHandle service = manager.OpenService(ServiceName);
        service.EnsureRunning();
    }

    internal static void StopService()
    {
        using ServiceHandle manager = ServiceHandle.OpenManager();
        using ServiceHandle service = manager.OpenService(ServiceName);
        service.EnsureStopped();
    }

    internal static void RestartService() { StopService(); StartService(); }

    internal static void InstallService(Paths paths) => NativeMaintenance.InstallService(paths);
    internal static void UninstallService() => NativeMaintenance.UninstallService();
    internal static void InstallAutostart(Paths paths) => NativeMaintenance.InstallAutostart(paths);
    internal static void UninstallAutostart() => NativeMaintenance.UninstallAutostart();

    private static void EnsureServiceRunning()
    {
        using ServiceHandle manager = ServiceHandle.OpenManager();
        using ServiceHandle? service = manager.TryOpenService(ServiceName);
        if (service is null) throw new InvalidOperationException("The telemetry service is not installed. Install it from the launcher first.");
        service.EnsureRunning();
    }

    internal static bool IsAutostartInstalled()
    {
        var start = new ProcessStartInfo { FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"), UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/Query"); start.ArgumentList.Add("/TN"); start.ArgumentList.Add("PadForge G7 Pro Launcher");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not query Task Scheduler.");
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private static Process StartPadForge(Paths paths, Options options, string logPath)
    {
        var start = new ProcessStartInfo { FileName = paths.PadForge, WorkingDirectory = Path.GetDirectoryName(paths.PadForge)!, UseShellExecute = false };
        start.Environment[HookVariable] = paths.StartupHook;
        start.Environment[OverrideVariable] = paths.Sdl;
        start.Environment[LogVariable] = logPath;
        start.Environment[VerifyVariable] = options.VerifyHook ? "1" : "0";
        foreach (string argument in options.PadForgeArguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Windows did not start PadForge.exe.");
    }

    private static string PrepareLog(Paths paths, Options options)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PadForgeG7ProLauncher", "logs");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{(options.VerifyHook ? "verify" : "launch")}.log");
        WriteLog(path, $"LaunchPadForge {typeof(Program).Assembly.GetName().Version}");
        WriteLog(path, $"BaseDirectory={paths.BaseDirectory}");
        return path;
    }

    private static void WriteLog(string path, string message) => File.AppendAllText(path, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
    private static string Hash(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void ValidateFile(string path, string description) { if (!File.Exists(path)) throw new FileNotFoundException($"The {description} was not found.", path); }

    private static void ValidateArchitecture(string path, ushort expectedMachine, string name)
    {
        using FileStream stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        if (reader.ReadUInt16() != 0x5A4D) throw new BadImageFormatException($"{name} is not a Windows PE image.");
        stream.Position = 0x3c;
        int peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset > stream.Length - 6) throw new BadImageFormatException($"{name} has an invalid PE header offset.");
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != expectedMachine) throw new BadImageFormatException($"{name} is not an x64 Windows binary.");
    }

    private static void ShowInformation(string message, Options options) { if (!options.Quiet) MessageBox.Show(message, "PadForge G7 Pro Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    private static void ShowError(Exception error) => MessageBox.Show(error.GetBaseException().Message, "PadForge G7 Pro Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private static void LogError(Exception error)
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PadForgeG7ProLauncher", "launcher-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {error}{Environment.NewLine}");
        }
        catch { }
    }
}

internal sealed record Paths(string BaseDirectory, string PadForge, string StartupHook, string Sdl, string TelemetryExecutable)
{
    public static Paths Resolve(Options options)
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string? rememberedPadForge = ThemeSettings.LoadPadForgePath();
        string adjacentPadForge = Path.Combine(baseDirectory, "PadForge.exe");
        string padForge = options.PadForgePath is not null
            ? Path.GetFullPath(options.PadForgePath)
            : rememberedPadForge is not null && File.Exists(rememberedPadForge) ? rememberedPadForge
            : adjacentPadForge;
        string runtimeDirectory = Path.Combine(baseDirectory, "runtime");
        return new Paths(baseDirectory, padForge, Path.Combine(runtimeDirectory, "PadForge.G7Pro.StartupHook.dll"), Path.Combine(runtimeDirectory, "SDL3.dll"), Path.Combine(runtimeDirectory, "GameSirG7ProTelemetry.exe"));
    }
}

internal sealed record Options(string? PadForgePath, bool SkipService, bool DryRun, bool VerifyHook, bool Wait, bool Quiet, bool AutoStart, bool LaunchNow, IReadOnlyList<string> PadForgeArguments)
{
    public bool RunImmediately => AutoStart || LaunchNow || DryRun || VerifyHook || Wait || PadForgeArguments.Count > 0;
    public static Options ForLaunch(string padForge) => new(padForge, false, false, false, false, false, false, true, Array.Empty<string>());

    public static Options Parse(string[] args)
    {
        string? padForge = null;
        bool skipService = false, dryRun = false, verifyHook = false, wait = false, quiet = false, autoStart = false, launch = false;
        var forwarded = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--padforge": if (++i >= args.Length) throw new ArgumentException("--padforge requires a path."); padForge = args[i]; break;
                case "--skip-service": skipService = true; break;
                case "--dry-run": dryRun = true; break;
                case "--verify-hook": verifyHook = true; break;
                case "--wait": wait = true; break;
                case "--quiet": quiet = true; break;
                case "--autostart": autoStart = true; quiet = true; break;
                case "--launch": launch = true; break;
                case "--": forwarded.AddRange(args[(i + 1)..]); i = args.Length; break;
                default: throw new ArgumentException($"Unknown launcher argument: {args[i]}");
            }
        }
        return new Options(padForge, skipService, dryRun, verifyHook, wait, quiet, autoStart, launch, forwarded);
    }
}

internal sealed class ServiceHandle : SafeHandle
{
    private const uint ScManagerConnect = 0x0001, ServiceQueryStatus = 0x0004, ServiceStart = 0x0010, ServiceStop = 0x0020;
    private const int ErrorServiceDoesNotExist = 1060, ErrorServiceAlreadyRunning = 1056, ErrorServiceNotActive = 1062;
    private const uint ServiceStopped = 1, ServiceStartPending = 2, ServiceStopPending = 3, ServiceRunning = 4, ServiceControlStop = 1;
    private const int ScStatusProcessInfo = 0;
    private ServiceHandle() : base(IntPtr.Zero, true) { }
    public override bool IsInvalid => handle == IntPtr.Zero;

    public static ServiceHandle OpenManager()
    {
        ServiceHandle manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the Windows service manager.");
        return manager;
    }

    public ServiceHandle? TryOpenService(string name)
    {
        ServiceHandle service = OpenServiceW(this, name, ServiceQueryStatus | ServiceStart | ServiceStop);
        if (!service.IsInvalid) return service;
        int error = Marshal.GetLastWin32Error(); service.Dispose();
        if (error == ErrorServiceDoesNotExist) return null;
        throw new Win32Exception(error, $"Could not open service {name}.");
    }

    public ServiceHandle OpenService(string name) => TryOpenService(name) ?? throw new InvalidOperationException($"Service {name} is not installed.");
    public string StatusText => QueryStatus().CurrentState switch { ServiceStopped => "Stopped", ServiceStartPending => "Starting", ServiceStopPending => "Stopping", ServiceRunning => "Running", _ => "Installed" };

    public void EnsureRunning()
    {
        if (QueryStatus().CurrentState == ServiceRunning) return;
        if (!StartServiceW(this, 0, null))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorServiceAlreadyRunning) throw new Win32Exception(error, "Could not start the telemetry service.");
        }
        WaitForState(ServiceRunning, "running");
    }

    public void EnsureStopped()
    {
        if (QueryStatus().CurrentState == ServiceStopped) return;
        if (!ControlService(this, ServiceControlStop, out _))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorServiceNotActive) throw new Win32Exception(error, "Could not stop the telemetry service.");
        }
        WaitForState(ServiceStopped, "stopped");
    }

    private void WaitForState(uint desired, string description)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(20)) { Thread.Sleep(200); if (QueryStatus().CurrentState == desired) return; }
        throw new InvalidOperationException($"Telemetry service did not reach the {description} state.");
    }

    private ServiceStatusProcess QueryStatus()
    {
        int size = Marshal.SizeOf<ServiceStatusProcess>();
        if (!QueryServiceStatusEx(this, ScStatusProcessInfo, out ServiceStatusProcess status, size, out _)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not query the telemetry service.");
        return status;
    }

    protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatusProcess { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ServiceHandle OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ServiceHandle OpenServiceW(ServiceHandle manager, string serviceName, uint desiredAccess);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatusEx(ServiceHandle service, int infoLevel, out ServiceStatusProcess status, int bufferSize, out int bytesNeeded);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartServiceW(ServiceHandle service, int argumentCount, string[]? arguments);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ControlService(ServiceHandle service, uint control, out ServiceStatusProcess status);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr service);
}
