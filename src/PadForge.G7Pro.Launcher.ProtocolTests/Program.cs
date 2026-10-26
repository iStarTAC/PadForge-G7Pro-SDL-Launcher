using System.Buffers.Binary;
using System.IO.Pipes;

namespace PadForge.G7Pro.Launcher;

internal static class ProtocolTests
{
    private const string PipeName = "GameSirG7ProTelemetry.Control.v1";
    private const uint Magic = 0x32475050;
    private const ushort ProtocolVersion = 2;
    private const ushort PrepareCommand = 2;

    public static int Main(string[] args)
    {
        try
        {
            if (args is ["--probe", string sdlPath])
            {
                ControllerConnection connection = ControllerProbe.GetStatus(sdlPath);
                Console.WriteLine($"{connection}: {ControllerProbe.Describe(connection)}");
                return 0;
            }

            string result = Exchange(error: 0, state: 0, result: 1);
            Require(result == "already active", "Successful prepare response was not accepted.");
            Require(ControllerProbe.Classify(3, 0) == ControllerConnection.NormalMode,
                "Companion normal-mode entries were treated as ambiguous.");
            Require(ControllerProbe.Classify(2, 2) == ControllerConnection.XboxGipMode,
                "Xbox GIP mode did not take precedence during identity overlap.");

            try
            {
                Exchange(error: 170, state: 1, result: 0);
                throw new InvalidOperationException("Busy response was unexpectedly accepted.");
            }
            catch (InvalidOperationException error) when (error.Message.Contains("already in use", StringComparison.Ordinal))
            {
            }

            try
            {
                Exchange(error: 0, state: 1, result: 1);
                throw new InvalidOperationException("Invalid success state was unexpectedly accepted.");
            }
            catch (InvalidDataException error) when (error.Message.Contains("reader state 1", StringComparison.Ordinal))
            {
            }

            Console.WriteLine("Launcher telemetry control protocol tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string Exchange(uint error, uint state, uint result)
    {
        Task server = ServeOnceAsync(PrepareCommand, error, state, result);
        string response = TelemetryControlClient.EnsureGipMode((uint)Environment.ProcessId);
        server.GetAwaiter().GetResult();
        return response;
    }

    private static async Task ServeOnceAsync(ushort expectedCommand, uint error, uint state, uint result)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var pipe = new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous);
        await pipe.WaitForConnectionAsync(timeout.Token);

        byte[] request = new byte[16];
        await pipe.ReadExactlyAsync(request, timeout.Token);
        uint signature = BinaryPrimitives.ReadUInt32LittleEndian(request);
        ushort protocol = BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(4));
        ushort command = BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(6));
        uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(8));
        uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(12));
        Require(signature == Magic, "Request signature was not serialized.");
        Require(protocol == ProtocolVersion, "Request protocol was not serialized.");
        Require(command == expectedCommand, "Request command was not serialized.");
        Require(requestId != 0, "Request ID was not serialized.");
        Require(reserved == 0, "Request reserved field was not zero.");

        byte[] response = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(response, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(4), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(6), command);
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(8), requestId);
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(12), error);
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(16), state);
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(20), result);
        await pipe.WriteAsync(response, timeout.Token);
        await pipe.FlushAsync(timeout.Token);

        byte[] acknowledgement = new byte[1];
        await pipe.ReadExactlyAsync(acknowledgement, timeout.Token);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
