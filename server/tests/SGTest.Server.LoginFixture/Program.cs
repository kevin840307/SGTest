using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class Program
{
    private const string Username = "fixture_user";
    private const string Password = "Fixture-Password-123!";

    public static async Task<int> Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "sgtest_login_fixture_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string mapDir = CreateMap(root, 15);
            var grid = BlockingGrid.Load(mapDir, 15);
            await using var world = new ServerWorldHost(new[] { grid });
            await world.StartAsync();
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync();
            long accountId = (await store.CreateAccountAsync(Username, Password)).AccountId;
            var created = await store.CreateCharacterAsync(accountId, 0, "FixtureHero", new CharacterSpawn(15, 8, 8));
            long characterId = created.Character?.CharacterId ?? throw new InvalidOperationException("fixture character create failed");
            var authorization = new IdentityTlsAuthorization(store, world);
            var resultRouter = new WorldResultRouter();

            using var certificate = CreateCertificate();
            string certPath = Path.Combine(root, "fixture-cert.pem");
            WriteCertificatePem(certPath, certificate);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine($"LOGIN_FIXTURE_READY port={port} cert={certPath} username={Username} character={characterId}");
                Console.Out.Flush();
                using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(15));
                var serverTask = authorization.RunAuthenticatedSocketAsync(client,
                    certificate, resultRouter,
                    new TlsWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000));

                using var receiptCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var receipt = await WaitForMoveAsync(world, resultRouter, characterId, receiptCts.Token);
                if (receipt.Status != MoveReceiptStatus.Accepted)
                    throw new InvalidOperationException("fixture move rejected: " + receipt.Status);
                if (resultRouter.Statistics.ReceiptsQueued != 1)
                    throw new InvalidOperationException("World result router did not route exactly one reliable ACK");
                Console.WriteLine($"LOGIN_FIXTURE_MOVE_PASS actor={receipt.ActorId} map_epoch={receipt.MapEpoch} sequence={receipt.MovementSequence}");
                Console.WriteLine($"LOGIN_FIXTURE_RECEIPT_ROUTED actor={receipt.ActorId} sequence={receipt.MovementSequence} status={receipt.Status}");
                Console.Out.Flush();

                var ended = await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
                if (ended.CommandsAccepted != 1 || ended.ReliableFramesSent != 1 ||
                    resultRouter.Statistics.ActiveSessions != 0)
                    throw new InvalidOperationException($"fixture disconnect or send mismatch commands={ended.CommandsAccepted} receipts={ended.ReliableFramesSent} active_routes={resultRouter.Statistics.ActiveSessions}");
                await world.StopAsync();
                Console.WriteLine($"LOGIN_FIXTURE_PASS tls={ended.Protocol} commands={ended.CommandsAccepted}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("LOGIN_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<MoveReceipt> WaitForMoveAsync(ServerWorldHost world, WorldResultRouter router, long actorId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await world.Results.ReadAsync(cancellationToken);
            router.Route(result); // same production routing boundary as live World outputs
            foreach (var receipt in result.Receipts)
                if (receipt.ActorId == actorId && receipt.MovementSequence == 1)
                    return receipt;
        }
    }

    private static string CreateMap(string root, int mapId)
    {
        string directory = Path.Combine(root, "map");
        Directory.CreateDirectory(directory);
        const int width = 3, height = 3, cell = 16;
        var raw = new byte[8 + width * height];
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(4, 4), height);
        File.WriteAllBytes(Path.Combine(directory, "blocking.dat"), raw);
        string sha = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(directory, "world.json"), JsonSerializer.Serialize(new
        {
            version = 3,
            map_id = mapId,
            collision_cell = cell,
            size = new[] { width * cell, height * cell },
            collision_sha256 = sha
        }));
        return directory;
    }

    private static void WriteCertificatePem(string path, X509Certificate2 certificate)
    {
        string b64 = Convert.ToBase64String(certificate.RawData, Base64FormattingOptions.InsertLineBreaks);
        File.WriteAllText(path, "-----BEGIN CERTIFICATE-----\n" + b64 + "\n-----END CERTIFICATE-----\n");
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        return new X509Certificate2(ephemeral.Export(X509ContentType.Pfx), string.Empty,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }
}
