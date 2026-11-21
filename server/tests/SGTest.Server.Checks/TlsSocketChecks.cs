using SGTest.Server.Visibility;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class TlsSocketChecks
{
    private static void Require(bool condition, string name) => Checks.Require(condition, name);

    private static ActorSnapshot Actor() =>
        new(77, 15, 4, 9, 0, 0, 24, 24, 1, false);

    public static void Verify()
    {
        Checks.Run("TLS socket requires authorization before gameplay and roundtrips reliable receipt",
            () => AuthorizationGateAndReceipt().GetAwaiter().GetResult());
        Checks.Run("TLS socket rejects authorization without exposing gameplay",
            () => AuthorizationRejected().GetAwaiter().GetResult());
        Checks.Run("TLS socket rejects plaintext compact-binary without fallback",
            () => PlaintextRejected().GetAwaiter().GetResult());
        Checks.Run("TLS socket retries retained Move under bounded World ingress backpressure",
            () => BackpressureRetry().GetAwaiter().GetResult());
        Checks.Run("TLS socket forwards authenticated player-shop callback",
            () => PlayerShopCallbackForwarding().GetAwaiter().GetResult());
        Checks.Run("TLS socket forwards authenticated nearby-chat callback",
            () => ChatCallbackForwarding().GetAwaiter().GetResult());
    }

    private static async Task AuthorizationGateAndReceipt()
    {
        using var certificate = CreateCertificate();
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
            SslStream _, CancellationToken cancellationToken)
        {
            authStarted.TrySetResult();
            await releaseAuth.Task.WaitAsync(cancellationToken);
            bound = new GatewayConnection(Actor());
            return bound;
        }

        var serverTask = TlsWireConnection.RunServerAsync(
            serverClient, certificate, Authorize,
            command =>
            {
                commandSeen.TrySetResult(command);
                return true;
            });

        var clientTls = CreateClientTls(client);
        await AuthenticateClient(clientTls);
        await authStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        byte[] move = WireProtocol.EncodeMoveTarget(
            new MoveTargetWire(9, 1, 6616, 4728));
        await clientTls.WriteAsync(move);
        await clientTls.FlushAsync();
        await Task.Delay(25);
        Require(!commandSeen.Task.IsCompleted,
            "gameplay was read before authorization/character binding completed");

        releaseAuth.TrySetResult();
        var command = await commandSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(command.ActorId == 77 && command.ConnectionEpoch == 4 &&
                command.MapEpoch == 9 && command.MovementSequence == 1,
            "TLS gameplay escaped trusted Gateway binding");

        var gateway = bound ?? throw new Exception("authorization did not create Gateway");
        Require(gateway.QueueReceipt(new MoveReceipt(
                77, 4, 9, 1, MoveReceiptStatus.Accepted, 42)) ==
                GatewaySendStatus.Enqueued,
            "receipt enqueue failed");

        byte[] receipt = new byte[WireProtocol.MoveReceiptFrameBytes];
        await clientTls.ReadExactlyAsync(receipt);
        Require(WireProtocol.TryDecodeMoveReceipt(receipt, out var decoded) &&
                decoded.MapEpoch == 9 && decoded.MovementSequence == 1 &&
                decoded.Status == MoveReceiptStatus.Accepted && decoded.ServerTick == 42,
            "TLS reliable receipt corrupt");

        await clientTls.DisposeAsync();
        client.Close();
        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == TlsWireEndReason.CleanEof &&
                result.Protocol is SslProtocols.Tls12 or SslProtocols.Tls13 &&
                result.CommandsAccepted == 1 && result.ReliableFramesSent == 1,
            "TLS session result mismatch");
    }

    private static async Task PlayerShopCallbackForwarding()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var accepting = listener.AcceptTcpClientAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await accepting;
        var seen = new TaskCompletionSource<TrustedPlayerShopCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        ValueTask<GatewayConnection?> Authorize(SslStream _, CancellationToken __)
        {
            var gateway = new GatewayConnection(Actor());
            Require(gateway.QueueAoi(new(
                    AoiEventKind.WorldReady, 42, 9, Entity: Actor())) ==
                    GatewaySendStatus.Enqueued,
                "player-shop TLS fixture WorldReady enqueue");
            return ValueTask.FromResult<GatewayConnection?>(gateway);
        }

        var serverTask = TlsWireConnection.RunServerAsync(
            serverClient, certificate, Authorize, _ => true,
            handlePlayerShop: request =>
            {
                seen.TrySetResult(request);
                return Task.CompletedTask;
            });

        var clientTls = CreateClientTls(client);
        await AuthenticateClient(clientTls);

        byte[] ready = new byte[WireProtocol.WorldReadyFrameBytes];
        await clientTls.ReadExactlyAsync(ready);
        Require(WireProtocol.TryDecodeWorldReady(ready, out var worldReady) &&
                worldReady.ReceiverMapEpoch == 9 && worldReady.ServerTick == 42,
            "player-shop TLS fixture WorldReady decode");
        await clientTls.WriteAsync(WireProtocol.EncodeWorldReadyAck(new(9, 42)));
        await clientTls.WriteAsync(WireProtocol.EncodePlayerShopCommand(new(
            9, 1, PlayerShopCommandAction.Open, 0, 0, 7001, 300)));
        await clientTls.FlushAsync();

        var command = await seen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(command.ActorId == 77 && command.ConnectionEpoch == 4 &&
                command.RequestMapEpoch == 9 && command.ShopSequence == 1 &&
                command.Action == PlayerShopCommandAction.Open &&
                command.ItemUid == 7001 && command.Price == 300,
            "player-shop callback was not forwarded through trusted TLS ingress");

        await clientTls.DisposeAsync();
        client.Close();
        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == TlsWireEndReason.CleanEof &&
                result.Fault is null,
            "player-shop callback forwarding closed TLS session");
    }

    private static async Task ChatCallbackForwarding()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var accepting = listener.AcceptTcpClientAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await accepting;
        var seen = new TaskCompletionSource<TrustedChatCommand>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        ValueTask<GatewayConnection?> Authorize(SslStream _, CancellationToken __)
        {
            var gateway = new GatewayConnection(Actor());
            Require(gateway.QueueAoi(new(
                    AoiEventKind.WorldReady, 42, 9, Entity: Actor())) ==
                    GatewaySendStatus.Enqueued,
                "chat TLS fixture WorldReady enqueue");
            return ValueTask.FromResult<GatewayConnection?>(gateway);
        }

        var serverTask = TlsWireConnection.RunServerAsync(
            serverClient, certificate, Authorize, _ => true,
            handleChat: request =>
            {
                seen.TrySetResult(request);
                return Task.CompletedTask;
            });

        var clientTls = CreateClientTls(client);
        await AuthenticateClient(clientTls);
        byte[] ready = new byte[WireProtocol.WorldReadyFrameBytes];
        await clientTls.ReadExactlyAsync(ready);
        Require(WireProtocol.TryDecodeWorldReady(ready, out var worldReady) &&
                worldReady.ReceiverMapEpoch == 9 && worldReady.ServerTick == 42,
            "chat TLS fixture WorldReady decode");

        await clientTls.WriteAsync(WireProtocol.EncodeWorldReadyAck(new(9, 42)));
        await clientTls.WriteAsync(WireProtocol.EncodeChatCommand(new(
            9, 7, ChatChannel.Nearby, 3, "附近測試")));
        await clientTls.FlushAsync();

        var command = await seen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(command.ActorId == 77 && command.ConnectionEpoch == 4 &&
                command.RequestMapEpoch == 9 && command.ChatSequence == 7 &&
                command.Channel == ChatChannel.Nearby &&
                command.BubbleType == 3 && command.Message == "附近測試",
            "chat callback was not forwarded through trusted TLS ingress");

        await clientTls.DisposeAsync();
        client.Close();
        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == TlsWireEndReason.CleanEof &&
                result.Fault is null && result.CommandsAccepted == 1,
            "chat callback forwarding closed TLS session");
    }

    private static async Task AuthorizationRejected()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptedSocket = listener.AcceptTcpClientAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await acceptedSocket;
        int gameplayCalls = 0;

        ValueTask<GatewayConnection?> Reject(SslStream _, CancellationToken __) =>
            ValueTask.FromResult<GatewayConnection?>(null);

        var serverTask = TlsWireConnection.RunServerAsync(
            serverClient, certificate, Reject,
            _ =>
            {
                Interlocked.Increment(ref gameplayCalls);
                return true;
            });

        var clientTls = CreateClientTls(client);
        await AuthenticateClient(clientTls);
        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == TlsWireEndReason.AuthorizationRejected &&
                result.Protocol is SslProtocols.Tls12 or SslProtocols.Tls13 &&
                result.CommandsAccepted == 0 && gameplayCalls == 0,
            "rejected authorization entered gameplay");

        await clientTls.DisposeAsync();
    }

    private static async Task PlaintextRejected()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptedSocket = listener.AcceptTcpClientAsync();
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        var serverClient = await acceptedSocket;
        int authCalls = 0;

        ValueTask<GatewayConnection?> NeverAuthorize(
            SslStream _, CancellationToken __)
        {
            Interlocked.Increment(ref authCalls);
            return ValueTask.FromResult<GatewayConnection?>(new GatewayConnection(Actor()));
        }

        var serverTask = TlsWireConnection.RunServerAsync(
            serverClient, certificate, NeverAuthorize, _ => true);

        byte[] plaintext = WireProtocol.EncodeMoveTarget(
            new MoveTargetWire(9, 1, 1, 1));
        await client.GetStream().WriteAsync(plaintext);
        await client.GetStream().FlushAsync();
        client.Client.Shutdown(SocketShutdown.Send);

        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == TlsWireEndReason.HandshakeFailed &&
                result.Protocol == SslProtocols.None && authCalls == 0,
            "plaintext fallback or pre-TLS authorization occurred");
    }

    private static async Task BackpressureRetry()
    {
        using var certificate = CreateCertificate();
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

        ValueTask<GatewayConnection?> Authorize(SslStream _, CancellationToken __) =>
            ValueTask.FromResult<GatewayConnection?>(new GatewayConnection(Actor()));

        var serverTask = TlsWireConnection.RunServerAsync(
            serverClient, certificate, Authorize,
            command =>
            {
                int attempt = Interlocked.Increment(ref attempts);
                if (attempt < 3) return false;
                accepted.TrySetResult(command);
                return true;
            },
            new TlsWireConnectionOptions(
                BackpressureRetryMilliseconds: 1,
                MaxBackpressureMilliseconds: 1000));

        var clientTls = CreateClientTls(client);
        await AuthenticateClient(clientTls);
        await clientTls.WriteAsync(WireProtocol.EncodeMoveTarget(
            new MoveTargetWire(9, 5, 300, 400)));
        await clientTls.FlushAsync();

        var command = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(attempts == 3 && command.MovementSequence == 5 &&
                command.ActorId == 77 && command.ConnectionEpoch == 4,
            "backpressured frame was dropped, duplicated or rebound");

        await clientTls.DisposeAsync();
        client.Close();
        var result = await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Require(result.Reason == TlsWireEndReason.CleanEof &&
                result.CommandsAccepted == 1,
            "backpressure recovery session result");
    }

    internal static SslStream CreateClientTls(TcpClient client) =>
        new(client.GetStream(), leaveInnerStreamOpen: false,
            (_, _, _, _) => true); // Self-signed certificate is test-only.

    internal static Task AuthenticateClient(SslStream tls) =>
        tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        });

    internal static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var ephemeral = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1));
        // Windows Schannel cannot always use an ephemeral private key for server TLS.
        // Re-import the test-only PFX into the current-user key store so this same
        // loopback check is portable across Windows and Linux CI.
        return new X509Certificate2(ephemeral.Export(X509ContentType.Pfx), string.Empty,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }
}
