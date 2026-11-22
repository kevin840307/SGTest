using SGTest.Server.Visibility;
using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Transport;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class AesSocketChecks
{
    private static void Require(bool condition, string name) => Checks.Require(condition, name);

    private static ActorSnapshot Actor() =>
        new(77, 15, 4, 9, 0, 0, 24, 24, 1, false);

    public static void Verify()
    {
        Checks.Run("AES socket requires authorization before gameplay and roundtrips reliable receipt",
            () => AuthorizationGateAndReceipt().GetAwaiter().GetResult());
        Checks.Run("AES socket rejects authorization without exposing gameplay",
            () => AuthorizationRejected().GetAwaiter().GetResult());
        Checks.Run("AES socket rejects plaintext compact-binary without fallback",
            () => PlaintextRejected().GetAwaiter().GetResult());
        Checks.Run("AES socket retries retained Move under bounded World ingress backpressure",
            () => BackpressureRetry().GetAwaiter().GetResult());
        Checks.Run("AES socket forwards authenticated player-shop callback",
            () => PlayerShopCallbackForwarding().GetAwaiter().GetResult());
        Checks.Run("AES socket forwards authenticated nearby-chat callback",
            () => ChatCallbackForwarding().GetAwaiter().GetResult());
    }

    private static async Task AuthorizationGateAndReceipt()
    {
        var settings = CreateSettings();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptedSocket = listener.AcceptTcpClientAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await acceptedSocket;

        var authStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAuth = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commandSeen = new TaskCompletionSource<MoveTargetCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        GatewayConnection? bound = null;

        async ValueTask<GatewayConnection?> Authorize(
            Stream _, CancellationToken cancellationToken)
        {
            authStarted.TrySetResult();
            await releaseAuth.Task.WaitAsync(cancellationToken);
            bound = new GatewayConnection(Actor());
            return bound;
        }

        var serverTask = AesWireConnection.RunServerAsync(
            serverClient, settings, Authorize,
            command =>
            {
                commandSeen.TrySetResult(command);
                return true;
            });

        var clientStream = CreateClientStream(client);
        await authStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        byte[] move = WireProtocol.EncodeMoveTarget(
            new MoveTargetWire(9, 1, 6616, 4728));
        await clientStream.WriteAsync(move);
        await clientStream.FlushAsync();
        await Task.Delay(25);
        Require(!commandSeen.Task.IsCompleted,
            "gameplay was read before authorization/character binding completed");

        releaseAuth.TrySetResult();
        var command = await commandSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(command.ActorId == 77 && command.ConnectionEpoch == 4 &&
                command.MapEpoch == 9 && command.MovementSequence == 1,
            "AES gameplay escaped trusted Gateway binding");

        var gateway = bound ?? throw new Exception("authorization did not create Gateway");
        Require(gateway.QueueReceipt(new MoveReceipt(
                77, 4, 9, 1, MoveReceiptStatus.Accepted, 42)) ==
                GatewaySendStatus.Enqueued,
            "receipt enqueue failed");

        byte[] receipt = new byte[WireProtocol.MoveReceiptFrameBytes];
        await clientStream.ReadExactlyAsync(receipt);
        Require(WireProtocol.TryDecodeMoveReceipt(receipt, out var decoded) &&
                decoded.MapEpoch == 9 && decoded.MovementSequence == 1 &&
                decoded.Status == MoveReceiptStatus.Accepted && decoded.ServerTick == 42,
            "AES reliable receipt corrupt");

        await clientStream.DisposeAsync();
        client.Close();
        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == AesWireEndReason.CleanEof &&
                result.CommandsAccepted == 1 && result.ReliableFramesSent == 1,
            "AES session result mismatch");
    }

    private static async Task PlayerShopCallbackForwarding()
    {
        var settings = CreateSettings();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var accepting = listener.AcceptTcpClientAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await accepting;
        var seen = new TaskCompletionSource<TrustedPlayerShopCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        ValueTask<GatewayConnection?> Authorize(Stream _, CancellationToken __)
        {
            var gateway = new GatewayConnection(Actor());
            Require(gateway.QueueAoi(new(
                    AoiEventKind.WorldReady, 42, 9, Entity: Actor())) ==
                    GatewaySendStatus.Enqueued,
                "player-shop AES fixture WorldReady enqueue");
            return ValueTask.FromResult<GatewayConnection?>(gateway);
        }

        var serverTask = AesWireConnection.RunServerAsync(
            serverClient, settings, Authorize, _ => true,
            handlePlayerShop: request =>
            {
                seen.TrySetResult(request);
                return Task.CompletedTask;
            });

        var clientStream = CreateClientStream(client);

        byte[] ready = new byte[WireProtocol.WorldReadyFrameBytes];
        await clientStream.ReadExactlyAsync(ready);
        Require(WireProtocol.TryDecodeWorldReady(ready, out var worldReady) &&
                worldReady.ReceiverMapEpoch == 9 && worldReady.ServerTick == 42,
            "player-shop AES fixture WorldReady decode");
        await clientStream.WriteAsync(WireProtocol.EncodeWorldReadyAck(new(9, 42)));
        await clientStream.WriteAsync(WireProtocol.EncodePlayerShopCommand(new(
            9, 1, PlayerShopCommandAction.Open, 0, 0, 7001, 300)));
        await clientStream.FlushAsync();

        var command = await seen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(command.ActorId == 77 && command.ConnectionEpoch == 4 &&
                command.RequestMapEpoch == 9 && command.ShopSequence == 1 &&
                command.Action == PlayerShopCommandAction.Open &&
                command.ItemUid == 7001 && command.Price == 300,
            "player-shop callback was not forwarded through trusted AES ingress");

        await clientStream.DisposeAsync();
        client.Close();
        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == AesWireEndReason.CleanEof &&
                result.Fault is null,
            "player-shop callback forwarding closed AES session");
    }

    private static async Task ChatCallbackForwarding()
    {
        var settings = CreateSettings();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var accepting = listener.AcceptTcpClientAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await accepting;
        var seen = new TaskCompletionSource<TrustedChatCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        ValueTask<GatewayConnection?> Authorize(Stream _, CancellationToken __)
        {
            var gateway = new GatewayConnection(Actor());
            Require(gateway.QueueAoi(new(
                    AoiEventKind.WorldReady, 42, 9, Entity: Actor())) ==
                    GatewaySendStatus.Enqueued,
                "chat AES fixture WorldReady enqueue");
            return ValueTask.FromResult<GatewayConnection?>(gateway);
        }

        var serverTask = AesWireConnection.RunServerAsync(
            serverClient, settings, Authorize, _ => true,
            handleChat: request =>
            {
                seen.TrySetResult(request);
                return Task.CompletedTask;
            });

        var clientStream = CreateClientStream(client);
        byte[] ready = new byte[WireProtocol.WorldReadyFrameBytes];
        await clientStream.ReadExactlyAsync(ready);
        Require(WireProtocol.TryDecodeWorldReady(ready, out var worldReady) &&
                worldReady.ReceiverMapEpoch == 9 && worldReady.ServerTick == 42,
            "chat AES fixture WorldReady decode");

        await clientStream.WriteAsync(WireProtocol.EncodeWorldReadyAck(new(9, 42)));
        await clientStream.WriteAsync(WireProtocol.EncodeChatCommand(new(
            9, 7, ChatChannel.Nearby, 3, "附近測試")));
        await clientStream.FlushAsync();

        var command = await seen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(command.ActorId == 77 && command.ConnectionEpoch == 4 &&
                command.RequestMapEpoch == 9 && command.ChatSequence == 7 &&
                command.Channel == ChatChannel.Nearby &&
                command.BubbleType == 3 && command.Message == "附近測試",
            "chat callback was not forwarded through trusted AES ingress");

        await clientStream.DisposeAsync();
        client.Close();
        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == AesWireEndReason.CleanEof &&
                result.Fault is null && result.CommandsAccepted == 1,
            "chat callback forwarding closed AES session");
    }

    private static async Task AuthorizationRejected()
    {
        var settings = CreateSettings();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptedSocket = listener.AcceptTcpClientAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await acceptedSocket;
        int gameplayCalls = 0;

        ValueTask<GatewayConnection?> Reject(Stream _, CancellationToken __) =>
            ValueTask.FromResult<GatewayConnection?>(null);

        var serverTask = AesWireConnection.RunServerAsync(
            serverClient, settings, Reject,
            _ =>
            {
                Interlocked.Increment(ref gameplayCalls);
                return true;
            });

        var clientStream = CreateClientStream(client);
        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == AesWireEndReason.AuthorizationRejected &&
                result.CommandsAccepted == 0 && gameplayCalls == 0,
            "rejected authorization entered gameplay");

        await clientStream.DisposeAsync();
    }

    private static async Task PlaintextRejected()
    {
        var settings = CreateSettings();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptedSocket = listener.AcceptTcpClientAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await acceptedSocket;
        int authCalls = 0;

        ValueTask<GatewayConnection?> NeverAuthorize(
            Stream _, CancellationToken __)
        {
            Interlocked.Increment(ref authCalls);
            return ValueTask.FromResult<GatewayConnection?>(new GatewayConnection(Actor()));
        }

        var serverTask = AesWireConnection.RunServerAsync(
            serverClient, settings, NeverAuthorize, _ => true);

        byte[] plaintext = WireProtocol.EncodeMoveTarget(
            new MoveTargetWire(9, 1, 1, 1));
        await client.GetStream().WriteAsync(plaintext);
        await client.GetStream().FlushAsync();
        client.Client.Shutdown(SocketShutdown.Send);

        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == AesWireEndReason.TransportError &&
                result.CommandsAccepted == 0 && authCalls == 1,
            "unencrypted payload was accepted or reached gameplay");
    }

    private static async Task BackpressureRetry()
    {
        var settings = CreateSettings();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptedSocket = listener.AcceptTcpClientAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await acceptedSocket;
        var accepted = new TaskCompletionSource<MoveTargetCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;

        ValueTask<GatewayConnection?> Authorize(Stream _, CancellationToken __) =>
            ValueTask.FromResult<GatewayConnection?>(new GatewayConnection(Actor()));

        var serverTask = AesWireConnection.RunServerAsync(
            serverClient, settings, Authorize,
            command =>
            {
                int attempt = Interlocked.Increment(ref attempts);
                if (attempt < 3) return false;
                accepted.TrySetResult(command);
                return true;
            },
            new AesWireConnectionOptions(
                BackpressureRetryMilliseconds: 1,
                MaxBackpressureMilliseconds: 1000));

        var clientStream = CreateClientStream(client);
        await clientStream.WriteAsync(WireProtocol.EncodeMoveTarget(
            new MoveTargetWire(9, 5, 300, 400)));
        await clientStream.FlushAsync();

        var command = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(attempts == 3 && command.MovementSequence == 5 &&
                command.ActorId == 77 && command.ConnectionEpoch == 4,
            "backpressured frame was dropped, duplicated or rebound");

        await clientStream.DisposeAsync();
        client.Close();
        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == AesWireEndReason.CleanEof &&
                result.CommandsAccepted == 1,
            "backpressure recovery session result");
    }

    internal static readonly AesCbcSettings Settings = new(
        Convert.FromHexString("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"),
        Convert.FromHexString("000102030405060708090a0b0c0d0e0f"));

    internal static AesCbcSettings CreateSettings() => Settings;

    internal static AesCbcFrameStream CreateClientStream(TcpClient client) =>
        new(client.GetStream(), Settings);
}
