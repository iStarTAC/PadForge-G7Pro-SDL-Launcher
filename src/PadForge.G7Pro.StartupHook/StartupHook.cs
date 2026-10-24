using System.Reflection;
using System.Runtime.InteropServices;

// The .NET host requires this exact global type and method name.
internal static class StartupHook
{
    private const string EngineAssemblyName = "PadForge.Engine";
    private const string OverrideVariable = "PADFORGE_SDL3_OVERRIDE";
    private const string LogVariable = "PADFORGE_G7PRO_HOOK_LOG";
    private const string VerifyVariable = "PADFORGE_G7PRO_VERIFY_HOOK";

    private static readonly object Sync = new();
    private static string? _overridePath;
    private static string? _logPath;
    private static IntPtr _sdlModule;
    private static bool _resolverInstalled;

    public static void Initialize()
    {
        _overridePath = Environment.GetEnvironmentVariable(OverrideVariable);
        _logPath = Environment.GetEnvironmentVariable(LogVariable);
        bool verify = Environment.GetEnvironmentVariable(VerifyVariable) == "1";
        if (string.IsNullOrWhiteSpace(_overridePath))
        {
            throw new InvalidOperationException($"{OverrideVariable} is not set.");
        }

        _overridePath = Path.GetFullPath(_overridePath);
        if (!File.Exists(_overridePath))
        {
            throw new FileNotFoundException("The requested SDL3 override does not exist.", _overridePath);
        }

        // Startup-hook variables are inherited by child processes. PadForge has
        // everything it needs now, so do not inject this hook into children it
        // may launch later.
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", null);
        Environment.SetEnvironmentVariable(OverrideVariable, null);
        Environment.SetEnvironmentVariable(LogVariable, null);
        Environment.SetEnvironmentVariable(VerifyVariable, null);

        Log($"Startup hook initialized. Override={_overridePath}");

        Assembly? engine = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(IsEngineAssembly);

        if (engine is null)
        {
            try
            {
                engine = Assembly.Load(new AssemblyName(EngineAssemblyName));
            }
            catch (FileNotFoundException)
            {
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
        }

        if (engine is not null)
        {
            InstallResolver(engine);
        }

        if (verify)
        {
            if (engine is null)
            {
                throw new InvalidOperationException("PadForge.Engine could not be loaded during hook verification.");
            }

            VerifyPadForgePInvoke(engine);
            Log("Verification completed successfully.");
            Environment.Exit(0);
        }
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        if (!IsEngineAssembly(args.LoadedAssembly))
        {
            return;
        }

        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        InstallResolver(args.LoadedAssembly);
    }

    private static bool IsEngineAssembly(Assembly assembly) =>
        string.Equals(assembly.GetName().Name, EngineAssemblyName, StringComparison.Ordinal);

    private static void InstallResolver(Assembly engine)
    {
        lock (Sync)
        {
            if (_resolverInstalled)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(engine, ResolveNativeLibrary);
            _resolverInstalled = true;
            Log($"Installed SDL resolver for {engine.FullName}.");
        }
    }

    private static IntPtr ResolveNativeLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "SDL3", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(libraryName, "SDL3.dll", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        lock (Sync)
        {
            if (_sdlModule == IntPtr.Zero)
            {
                _sdlModule = NativeLibrary.Load(_overridePath!);
                Log($"Redirected {libraryName} to {_overridePath}.");
            }

            return _sdlModule;
        }
    }

    private static void VerifyPadForgePInvoke(Assembly engine)
    {
        Type sdl = engine.GetType("SDL3.SDL", throwOnError: true)!;
        MethodInfo linkedVersion = sdl.GetMethod(
            "SDL_Linked_Version",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException("SDL3.SDL", "SDL_Linked_Version");

        object version = linkedVersion.Invoke(null, null)
            ?? throw new InvalidOperationException("SDL_Linked_Version returned no value.");
        Log($"PadForge SDL_Linked_Version returned {version}.");
    }

    private static void Log(string message)
    {
        string? path = _logPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(
                fullPath,
                $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never prevent PadForge from starting.
        }
    }
}
