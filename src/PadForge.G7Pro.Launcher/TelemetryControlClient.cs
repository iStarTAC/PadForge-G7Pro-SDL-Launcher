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
    private const int ErrorBusy = 170;
    private const int ErrorNotSupported = 50;
    private const int ErrorDeviceNotConnected = 1167;
    private const int ErrorNotFound = 1168;
    private const int ErrorCancelled = 1223;
    private const int ErrorTimeout = 1460;

    public static string EnsureGipMode(uint expectedServiceProcessId)
    {
        ControlResponse response = SendCommand(expectedServiceProcessId, PrepareCommand);
        if (response.Error != 0) throw PrepareException(unchecked((int)response.Error));
        if (response.State != 0)
            throw new InvalidDataException(
                $"The telemetry service reported reader state {response.State} after preparing Xbox GIP mode.");

        return response.Result switch
        {
            1 => "already active",
            2 => "entered successfully",
            _ => throw new InvalidDataException("The telemetry service returned an unknown Xbox GIP mode result."),
        };
    }

    private static ControlResponse SendCommand(uint expectedServiceProcessId, ushort requestedCommand)
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
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(6), requestedCommand);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), requestId);

        byte[] response = new byte[24];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            ExchangeAsync(pipe, request, response, timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The telemetry service did not respond to the control request.");
        }

        uint signature = BinaryPrimitives.ReadUInt32LittleEndian(response);
        ushort protocol = BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(4));
        ushort command = BinaryPrimitives.ReadUInt16LittleEndian(response.AsSpan(6));
        uint returnedRequestId = BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(8));
        uint error = BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(12));
        uint state = BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(16));
        uint result = BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(20));
        if (signature != Magic || protocol != ProtocolVersion || command != requestedCommand ||
            returnedRequestId != requestId)
            throw new InvalidDataException(
                $"The telemetry service returned an invalid control response " +
                $"(signature=0x{signature:X8}, protocol={protocol}, command={command}, " +
                $"request={returnedRequestId}, expected={requestId}).");
        return new ControlResponse(error, state, result);
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
        ErrorBusy => new InvalidOperationException(
            "The telemetry service is already in use by PadForge or another SDL client. Close it and try again."),
        ErrorNotSupported => new InvalidOperationException(
            "The installed telemetry service could not verify this Xbox GIP controller state. Reinstall the current service from this launcher."),
        ErrorDeviceNotConnected => new InvalidOperationException(
            "The telemetry service could not reliably inspect the GameSir G7 Pro interface. Reconnect the controller and dongle, then try again."),
        ErrorNotFound => new InvalidOperationException(
            "No active GameSir G7 Pro controller was found in normal or Xbox GIP mode. Connect or power on the controller and try again."),
        ErrorCancelled => new OperationCanceledException("Xbox GIP mode preparation was cancelled because the telemetry service stopped."),
        ErrorTimeout => new TimeoutException(
            "The GameSir G7 Pro was detected, but it did not complete a stable transition to Xbox GIP mode. Reconnect it and try again."),
        _ => new Win32Exception(error, "The telemetry service could not prepare Xbox GIP mode."),
    };

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    private readonly record struct ControlResponse(uint Error, uint State, uint Result);
}
