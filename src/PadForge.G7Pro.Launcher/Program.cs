using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace PadForge.G7Pro.Launcher;

internal static class Program
{
    private const string ServiceName = "GameSirG7ProTelemetry";
    private const string HookVariable = "DOTNET_STARTUP_HOOKS";
    private const string OverrideVariable = "PADFORGE_SDL3_OVERRIDE";
    private const string LogVariable = "PADFORGE_G7PRO_HOOK_LOG";
    private const string VerifyVariable = "PADFORGE_G7PRO_VERIFY_HOOK";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            Paths paths = Paths.Resolve(options);
            string logPath = PrepareLog(paths, options);

            ValidateFile(paths.PadForge, "official PadForge executable");
            ValidateFile(paths.StartupHook, "startup hook");
            ValidateFile(paths.Sdl, "custom SDL3 library");
            ValidateArchitecture(paths.PadForge, 0x8664, "PadForge.exe");
            ValidateArchitecture(paths.Sdl, 0x8664, "SDL3.dll");

            if (!options.SkipService)
            {
                EnsureTelemetryService(paths);
            }

            WriteLog(logPath, $"PadForge={paths.PadForge}");
            WriteLog(logPath, $"StartupHook={paths.StartupHook}");
            WriteLog(logPath, $"SDL3={paths.Sdl} SHA256={Hash(paths.Sdl)}");
            WriteLog(logPath, $"Service={(options.SkipService ? "skipped" : "running")}");

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
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"PadForge exited with code {process.ExitCode}. See {logPath}");
                }
            }

            if (options.VerifyHook)
            {
                string log = File.ReadAllText(logPath);
                if (!log.Contains("Redirected SDL3", StringComparison.Ordinal) ||
                    !log.Contains("Verification completed successfully", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The official PadForge process exited without proving the SDL redirect.");
                }

                ShowInformation($"The official PadForge build loaded the custom SDL successfully.\n\nLog: {logPath}", options);
            }

            return 0;
        }
        catch (Exception error)
        {
            string message = error.GetBaseException().Message;
            try
            {
                string emergencyLog = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PadForgeG7ProLauncher",
                    "launcher-error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(emergencyLog)!);
                File.AppendAllText(emergencyLog, $"{DateTimeOffset.Now:O} {error}{Environment.NewLine}");
            }
            catch
            {
            }

            MessageBox.Show(message, "PadForge G7 Pro Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static Process StartPadForge(Paths paths, Options options, string logPath)
    {
        var start = new ProcessStartInfo
        {
            FileName = paths.PadForge,
            WorkingDirectory = Path.GetDirectoryName(paths.PadForge)!,
            UseShellExecute = false,
        };
        start.Environment[HookVariable] = paths.StartupHook;
        start.Environment[OverrideVariable] = paths.Sdl;
        start.Environment[LogVariable] = logPath;
        start.Environment[VerifyVariable] = options.VerifyHook ? "1" : "0";

        foreach (string argument in options.PadForgeArguments)
        {
            start.ArgumentList.Add(argument);
        }

        return Process.Start(start)
            ?? throw new InvalidOperationException("Windows did not start PadForge.exe.");
    }

    private static void EnsureTelemetryService(Paths paths)
    {
        ValidateFile(paths.TelemetryExecutable, "telemetry service executable");
        ValidateFile(paths.ServiceInstaller, "telemetry service installer");

        using ServiceHandle manager = ServiceHandle.OpenManager();
        using ServiceHandle? service = manager.TryOpenService(ServiceName);
        if (service is null)
        {
            RunInstaller(paths.ServiceInstaller, "Install");

            using ServiceHandle installed = manager.OpenService(ServiceName);
            installed.EnsureRunning();
            return;
        }

        string installedExecutable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ServiceName,
            "GameSirG7ProTelemetry.exe");
        if (!File.Exists(installedExecutable) ||
            !string.Equals(Hash(installedExecutable), Hash(paths.TelemetryExecutable), StringComparison.Ordinal))
        {
            service.Dispose();
            RunInstaller(paths.ServiceInstaller, "Repair");
            using ServiceHandle repaired = manager.OpenService(ServiceName);
            repaired.EnsureRunning();
            return;
        }

        service.EnsureRunning();
    }

    private static void RunInstaller(string installer, string action)
    {
        string powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var start = new ProcessStartInfo
        {
            FileName = powerShell,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installer)!,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(installer);
        start.ArgumentList.Add("-Action");
        start.ArgumentList.Add(action);

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Windows did not start the telemetry installer.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Telemetry service installation failed with exit code {process.ExitCode}.");
        }
    }

    private static string PrepareLog(Paths paths, Options options)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PadForgeG7ProLauncher",
            "logs");
        Directory.CreateDirectory(directory);
        string suffix = options.VerifyHook ? "verify" : "launch";
        string path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{suffix}.log");
        WriteLog(path, $"LaunchPadForge {typeof(Program).Assembly.GetName().Version}");
        WriteLog(path, $"BaseDirectory={paths.BaseDirectory}");
        return path;
    }

    private static void WriteLog(string path, string message) =>
        File.AppendAllText(path, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void ValidateFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The {description} was not found.", path);
        }
    }

    private static void ValidateArchitecture(string path, ushort expectedMachine, string name)
    {
        using FileStream stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        if (reader.ReadUInt16() != 0x5A4D)
        {
            throw new BadImageFormatException($"{name} is not a Windows PE image.");
        }

        stream.Position = 0x3c;
        int peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset > stream.Length - 6)
        {
            throw new BadImageFormatException($"{name} has an invalid PE header offset.");
        }

        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != expectedMachine)
        {
            throw new BadImageFormatException($"{name} is not an x64 Windows binary.");
        }
    }

    private static void ShowInformation(string message, Options options)
    {
        if (options.Quiet)
        {
            return;
        }

        MessageBox.Show(message, "PadForge G7 Pro Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

internal sealed record Paths(
    string BaseDirectory,
    string PadForge,
    string StartupHook,
    string Sdl,
    string TelemetryExecutable,
    string ServiceInstaller)
{
    public static Paths Resolve(Options options)
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string padForge = options.PadForgePath is null
            ? Path.Combine(baseDirectory, "PadForge.exe")
            : Path.GetFullPath(options.PadForgePath);
        return new Paths(
            baseDirectory,
            padForge,
            Path.Combine(baseDirectory, "PadForge.G7Pro.StartupHook.dll"),
            Path.Combine(baseDirectory, "SDL3.dll"),
            Path.Combine(baseDirectory, "GameSirG7ProTelemetry.exe"),
            Path.Combine(baseDirectory, "install-service.ps1"));
    }
}

internal sealed record Options(
    string? PadForgePath,
    bool SkipService,
    bool DryRun,
    bool VerifyHook,
    bool Wait,
    bool Quiet,
    IReadOnlyList<string> PadForgeArguments)
{
    public static Options Parse(string[] args)
    {
        string? padForge = null;
        bool skipService = false;
        bool dryRun = false;
        bool verifyHook = false;
        bool wait = false;
        bool quiet = false;
        var forwarded = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--padforge":
                    if (++i >= args.Length)
                    {
                        throw new ArgumentException("--padforge requires a path.");
                    }
                    padForge = args[i];
                    break;
                case "--skip-service":
                    skipService = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--verify-hook":
                    verifyHook = true;
                    break;
                case "--wait":
                    wait = true;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                case "--":
                    forwarded.AddRange(args[(i + 1)..]);
                    i = args.Length;
                    break;
                default:
                    throw new ArgumentException($"Unknown launcher argument: {args[i]}");
            }
        }

        return new Options(padForge, skipService, dryRun, verifyHook, wait, quiet, forwarded);
    }
}

internal sealed class ServiceHandle : SafeHandle
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const int ErrorServiceDoesNotExist = 1060;
    private const uint ServiceStopped = 1;
    private const uint ServiceRunning = 4;
    private const int ScStatusProcessInfo = 0;

    private ServiceHandle() : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    public static ServiceHandle OpenManager()
    {
        ServiceHandle manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the Windows service manager.");
        }
        return manager;
    }

    public ServiceHandle? TryOpenService(string name)
    {
        ServiceHandle service = OpenServiceW(this, name, ServiceQueryStatus | ServiceStart);
        if (!service.IsInvalid)
        {
            return service;
        }

        int error = Marshal.GetLastWin32Error();
        service.Dispose();
        if (error == ErrorServiceDoesNotExist)
        {
            return null;
        }
        throw new Win32Exception(error, $"Could not open service {name}.");
    }

    public ServiceHandle OpenService(string name) =>
        TryOpenService(name) ?? throw new InvalidOperationException($"Service {name} was not installed.");

    public void EnsureRunning()
    {
        ServiceStatusProcess status = QueryStatus();
        if (status.CurrentState == ServiceRunning)
        {
            return;
        }

        if (status.CurrentState == ServiceStopped && !StartServiceW(this, 0, null))
        {
            int error = Marshal.GetLastWin32Error();
            const int ErrorServiceAlreadyRunning = 1056;
            if (error != ErrorServiceAlreadyRunning)
            {
                throw new Win32Exception(error, $"Could not start service {ServiceNameForError()}.");
            }
        }

        Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            Thread.Sleep(200);
            status = QueryStatus();
            if (status.CurrentState == ServiceRunning)
            {
                return;
            }
            if (status.CurrentState == ServiceStopped)
            {
                break;
            }
        }

        throw new InvalidOperationException("GameSir G7 Pro telemetry service did not reach the running state.");
    }

    private ServiceStatusProcess QueryStatus()
    {
        int size = Marshal.SizeOf<ServiceStatusProcess>();
        if (!QueryServiceStatusEx(this, ScStatusProcessInfo, out ServiceStatusProcess status, size, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not query the telemetry service.");
        }
        return status;
    }

    private static string ServiceNameForError() => "GameSirG7ProTelemetry";

    protected override bool ReleaseHandle() => CloseServiceHandle(handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenServiceW(ServiceHandle manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        ServiceHandle service,
        int infoLevel,
        out ServiceStatusProcess status,
        int bufferSize,
        out int bytesNeeded);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceW(ServiceHandle service, int argumentCount, string[]? arguments);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr service);
}
