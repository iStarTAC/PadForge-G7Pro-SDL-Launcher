using System.Runtime.InteropServices;

namespace PadForge.G7Pro.Launcher;

internal enum ControllerConnection
{
    NotDetected,
    NormalMode,
    XboxGipMode,
}

internal static class ControllerProbe
{
    private const uint SdlInitJoystick = 0x00000200;
    private const ushort GameSirVendor = 0x3537;
    private const ushort NormalProduct = 0x100A;
    private const ushort WiredGipProduct = 0x109B;
    private const ushort WirelessGipProduct = 0x109C;

    public static ControllerConnection GetStatus(string sdlPath)
    {
        nint library = NativeLibrary.Load(sdlPath);
        SdlQuitSubSystem? quit = null;
        bool initialized = false;
        try
        {
            var init = Export<SdlInitSubSystem>(library, "SDL_InitSubSystem");
            quit = Export<SdlQuitSubSystem>(library, "SDL_QuitSubSystem");
            var update = Export<SdlUpdateJoysticks>(library, "SDL_UpdateJoysticks");
            var getJoysticks = Export<SdlGetJoysticks>(library, "SDL_GetJoysticks");
            var getVendor = Export<SdlGetJoystickVendorForId>(library, "SDL_GetJoystickVendorForID");
            var getProduct = Export<SdlGetJoystickProductForId>(library, "SDL_GetJoystickProductForID");
            var free = Export<SdlFree>(library, "SDL_free");
            var getError = Export<SdlGetError>(library, "SDL_GetError");

            if (!init(SdlInitJoystick))
                throw new InvalidOperationException(
                    "SDL could not inspect connected controllers: " +
                    (Marshal.PtrToStringUTF8(getError()) ?? "unknown SDL error"));
            initialized = true;
            update();

            nint ids = getJoysticks(out int count);
            try
            {
                int normal = 0, gip = 0;
                for (int index = 0; index < count; ++index)
                {
                    uint id = unchecked((uint)Marshal.ReadInt32(ids, index * sizeof(uint)));
                    if (getVendor(id) != GameSirVendor) continue;
                    ushort product = getProduct(id);
                    if (product == NormalProduct) ++normal;
                    else if (product is WiredGipProduct or WirelessGipProduct) ++gip;
                }

                return Classify(normal, gip);
            }
            finally
            {
                if (ids != 0) free(ids);
            }
        }
        finally
        {
            if (initialized) quit!(SdlInitJoystick);
            NativeLibrary.Free(library);
        }
    }

    public static void EnterXboxGipMode(string sdlPath)
    {
        nint library = NativeLibrary.Load(sdlPath);
        SdlQuitSubSystem? quit = null;
        bool initialized = false;
        try
        {
            var init = Export<SdlInitSubSystem>(library, "SDL_InitSubSystem");
            quit = Export<SdlQuitSubSystem>(library, "SDL_QuitSubSystem");
            var getJoysticks = Export<SdlGetJoysticks>(library, "SDL_GetJoysticks");
            var getVendor = Export<SdlGetJoystickVendorForId>(library, "SDL_GetJoystickVendorForID");
            var getProduct = Export<SdlGetJoystickProductForId>(library, "SDL_GetJoystickProductForID");
            var open = Export<SdlOpenJoystick>(library, "SDL_OpenJoystick");
            var rumble = Export<SdlRumbleJoystick>(library, "SDL_RumbleJoystick");
            var close = Export<SdlCloseJoystick>(library, "SDL_CloseJoystick");
            var free = Export<SdlFree>(library, "SDL_free");
            var getError = Export<SdlGetError>(library, "SDL_GetError");

            if (!init(SdlInitJoystick))
                throw new InvalidOperationException(
                    "SDL could not initialize the controller for Xbox GIP mode entry: " +
                    (Marshal.PtrToStringUTF8(getError()) ?? "unknown SDL error"));
            initialized = true;

            nint ids = getJoysticks(out int count);
            nint joystick = 0;
            try
            {
                uint normalId = 0;
                for (int index = 0; index < count; ++index)
                {
                    uint id = unchecked((uint)Marshal.ReadInt32(ids, index * sizeof(uint)));
                    if (normalId == 0 && getVendor(id) == GameSirVendor && getProduct(id) == NormalProduct)
                        normalId = id;
                }
                if (normalId == 0)
                    throw new InvalidOperationException(
                        "Xbox GIP mode entry requires an active GameSir G7 Pro in normal mode.");

                joystick = open(normalId);
                if (joystick == 0)
                    throw new InvalidOperationException(
                        "SDL could not open the GameSir G7 Pro for Xbox GIP mode entry: " +
                        (Marshal.PtrToStringUTF8(getError()) ?? "unknown SDL error"));

                ReadOnlySpan<byte> sequence = [
                    (byte)'g', (byte)'a', 0, 0, (byte)'m', (byte)'e', 0, 0,
                    (byte)'s', (byte)'i', 0, 0, (byte)'r', (byte)'a', 0, 0,
                    (byte)'p', (byte)'p', 0, 0
                ];
                for (int index = 0; index < sequence.Length; index += 2)
                {
                    ushort low = (ushort)(sequence[index] * 0x0101);
                    ushort high = (ushort)(sequence[index + 1] * 0x0101);
                    if (!rumble(joystick, low, high, 15)) break; // Re-enumeration can interrupt the final send.
                    Thread.Sleep(15);
                }
            }
            finally
            {
                if (joystick != 0) close(joystick);
                if (ids != 0) free(ids);
            }
        }
        finally
        {
            if (initialized) quit!(SdlInitJoystick);
            NativeLibrary.Free(library);
        }
    }

    public static string Describe(ControllerConnection connection) => connection switch
    {
        ControllerConnection.NotDetected => "Not detected",
        ControllerConnection.NormalMode => "Connected in normal mode",
        ControllerConnection.XboxGipMode => "Connected in Xbox GIP mode",
        _ => "Unknown",
    };

    internal static ControllerConnection Classify(int normalCount, int gipCount)
    {
        if (gipCount > 0) return ControllerConnection.XboxGipMode;
        if (normalCount > 0) return ControllerConnection.NormalMode;
        return ControllerConnection.NotDetected;
    }

    private static T Export<T>(nint library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlInitSubSystem(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlQuitSubSystem(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlUpdateJoysticks();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint SdlGetJoysticks(out int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort SdlGetJoystickVendorForId(uint instanceId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ushort SdlGetJoystickProductForId(uint instanceId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint SdlOpenJoystick(uint instanceId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SdlRumbleJoystick(nint joystick, ushort lowFrequency, ushort highFrequency, uint durationMilliseconds);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlCloseJoystick(nint joystick);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlFree(nint memory);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint SdlGetError();
}
