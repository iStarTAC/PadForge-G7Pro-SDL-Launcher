using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace PadForge.G7Pro.Launcher;

internal static class TelemetryControlClient
{
    private const string PipeName = "GameSirG7ProTelemetry.Control.v1";
    private const uint Magic = 0x32475050;
    private const ushort ProtocolVersion = 2;
    private const ushort PrepareCommand = 2;
    private const int ErrorInvalidData = 13;
    private const int ErrorNotSupported = 50;
    private const int ErrorDeviceNotConnected = 1167;
    private const int ErrorNotFound = 1168;
    private const int ErrorCancelled = 1223;
    private const int ErrorTimeout = 1460;

    public static string EnsureGipMode(uint expectedServiceProcessId)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        pipe.Connect(5000);

        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverProcessId))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not verify the telemetry service control pipe.");
        if (serverProcessId == 0 || serverProcessId != expectedServiceProcessId)
            throw new InvalidOperationException("The telemetry control pipe is not owned by the installed service process.");

        uint requestId = unchecked((uint)Environment.TickCount);
        byte[] request = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(request, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(request[4..], ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(request[6..], PrepareCommand);
        BinaryPrimitives.WriteUInt32LittleEndian(request[8..], requestId);

        byte[] response = new byte[24];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            ExchangeAsync(pipe, request, response, timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The telemetry service did not respond to the Xbox GIP mode request.");
        }

        uint signature = BinaryPrimitives.ReadUInt32LittleEndian(response);
        ushort protocol = BinaryPrimitives.ReadUInt16LittleEndian(response[4..]);
        ushort command = BinaryPrimitives.ReadUInt16LittleEndian(response[6..]);
        uint returnedRequestId = BinaryPrimitives.ReadUInt32LittleEndian(response[8..]);
        uint error = BinaryPrimitives.ReadUInt32LittleEndian(response[12..]);
        uint state = BinaryPrimitives.ReadUInt32LittleEndian(response[16..]);
        uint result = BinaryPrimitives.ReadUInt32LittleEndian(response[20..]);
        if (signature != Magic || protocol != ProtocolVersion || command != PrepareCommand ||
            returnedRequestId != requestId || state != 0)
            throw new InvalidDataException("The telemetry service returned an invalid Xbox GIP mode response.");
        if (error != 0) throw PrepareException(unchecked((int)error));

        return result switch
        {
            1 => "already active",
            2 => "entered successfully",
            _ => throw new InvalidDataException("The telemetry service returned an unknown Xbox GIP mode result."),
        };
    }

    private static async Task ExchangeAsync(
        NamedPipeClientStream pipe,
        byte[] request,
        byte[] response,
        CancellationToken cancellationToken)
    {
        await pipe.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        await pipe.ReadExactlyAsync(response, cancellationToken).ConfigureAwait(false);
        await pipe.WriteAsync(new byte[] { 0 }, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Exception PrepareException(int error) => error switch
    {
        ErrorInvalidData => new InvalidOperationException(
            "The installed telemetry service does not support Xbox GIP mode preparation. Reinstall the service from this launcher."),
        ErrorNotSupported => new InvalidOperationException(
            "Xbox GIP mode entry requires exactly one connected XInput controller. Disconnect other XInput controllers and try again."),
        ErrorDeviceNotConnected => new InvalidOperationException(
            "The GameSir G7 Pro device state is unhealthy or ambiguous. Reconnect the controller and dongle, then try again."),
        ErrorNotFound => new InvalidOperationException(
            "No supported GameSir G7 Pro was found. Connect the controller and dongle, then try again."),
        ErrorCancelled => new OperationCanceledException("Xbox GIP mode preparation was cancelled because the telemetry service stopped."),
        ErrorTimeout => new TimeoutException(
            "The GameSir G7 Pro did not enter Xbox GIP mode. Reconnect the controller and dongle, then try again."),
        _ => new Win32Exception(error, "The telemetry service could not prepare Xbox GIP mode."),
    };

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
