using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SGTest.Server.Transport;

const string KeyHex = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
const string IvHex = "000102030405060708090a0b0c0d0e0f";
const string PlainHex = "1c00000001000100010000000000000001000000e8030000d0070000";
const string RecordHex = "20000000c8742a0e72a5d69b17332b42d767b080934464023a67818d9c4a56b6478a503e";
var settings = new AesCbcSettings(Convert.FromHexString(KeyHex), Convert.FromHexString(IvHex));
var plain = Convert.FromHexString(PlainHex);
byte[] record;
using (var memory = new MemoryStream())
{
    await using (var writer = new AesCbcFrameStream(memory, settings))
    {
        await writer.WriteAsync(plain);
        record = memory.ToArray();
    }
}
if (!record.SequenceEqual(Convert.FromHexString(RecordHex)))
    throw new Exception("Cross-runtime golden ciphertext mismatch");
using (var reader = new AesCbcFrameStream(new MemoryStream(record), settings))
{
    var oneByte = new byte[1];
    await reader.ReadExactlyAsync(oneByte);
    var tail = new byte[plain.Length - 1];
    await reader.ReadExactlyAsync(tail);
    if (!oneByte.Concat(tail).SequenceEqual(plain))
        throw new Exception("Partial frame ReadAsync failed");
    if (await reader.ReadAsync(new byte[1]) != 0)
        throw new Exception("EOF was not preserved");
}
using (var invalid = new AesCbcFrameStream(new MemoryStream(new byte[] { 15, 0, 0, 0 }), settings))
{
    try
    {
        await invalid.ReadAsync(new byte[28]);
        throw new Exception("Malformed encrypted record accepted");
    }
    catch (IOException) { }
}
Console.WriteLine("AES_CBC_DOTNET_GOLDEN_PASS frame=28 cipher=32 record=36");

string godotExe = Environment.GetEnvironmentVariable("SGTEST_GODOT_BIN")
    ?? @"C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe";
if (!File.Exists(godotExe))
{
    Console.WriteLine("AES_CBC_GODOT_TCP_NOT_VERIFIED (Godot binary absent)");
    return;
}
using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var server = Task.Run(async () =>
{
    using var client = await listener.AcceptTcpClientAsync(stop.Token);
    using var transport = new AesCbcFrameStream(client.GetStream(), settings);
    byte[] received = new byte[plain.Length];
    await transport.ReadExactlyAsync(received, stop.Token);
    if (!received.SequenceEqual(plain)) throw new Exception("Godot TCP frame mismatch");
    await transport.WriteAsync(received, stop.Token);
    await transport.FlushAsync(stop.Token);
}, stop.Token);

using var process = new Process();
process.StartInfo = new ProcessStartInfo(godotExe)
{
    WorkingDirectory = Directory.GetCurrentDirectory(),
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};
foreach (string argument in new[] { "--headless", "--path", "client", "--script",
    "res://tests/test_aes_cbc_tcp.gd", "--", port.ToString() })
    process.StartInfo.ArgumentList.Add(argument);
process.StartInfo.Environment["SGTEST_AES_KEY_HEX"] = KeyHex;
process.StartInfo.Environment["SGTEST_AES_IV_HEX"] = IvHex;
if (!process.Start()) throw new Exception("Unable to start Godot");
Task<string> stdout = process.StandardOutput.ReadToEndAsync();
Task<string> stderr = process.StandardError.ReadToEndAsync();
await Task.WhenAll(server, process.WaitForExitAsync(stop.Token));
string combined = (await stdout) + (await stderr);
Console.Write(combined);
if (process.ExitCode != 0 || !combined.Contains("AES_CBC_GODOT_TCP_PASS", StringComparison.Ordinal))
    throw new Exception("Godot-to-C# encrypted TCP E2E failed");
Console.WriteLine("AES_CBC_TRANSPORT_PROBE_PASS");
