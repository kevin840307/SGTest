using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace SGTest.Server.Gateway;

public sealed record TlsGatewayListenerOptions(
    int MaxConnections = 128,
    int MaxConnectionsPerAddress = 8,
    int Backlog = 128,
    TlsWireConnectionOptions? Connection = null);

public readonly record struct TlsGatewayListenerStatistics(
    int ActiveConnections, int ActiveAddresses, long AcceptedConnections,
    long RejectedCapacity, long RejectedAddress, long CompletedConnections,
    long CommandsAccepted, long ReliableFramesSent);

/// <summary>
/// TLS-only admission and accepted-socket ownership, outside the World writer.
/// Capacity includes handshaking, authenticating and closing sessions. The caller
/// owns the certificate, identity, World and result/AOI pumps until disposal ends.
/// </summary>
public sealed class TlsGatewayListener : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly IdentityTlsAuthorization _authorization;
    private readonly X509Certificate2 _certificate;
    private readonly WorldResultRouter _router;
    private readonly TlsGatewayListenerOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<IPAddress, int> _addresses = [];
    private readonly HashSet<Task> _sessions = [];
    private CancellationTokenSource? _lifetime;
    private Task? _run;
    private Task? _dispose;
    private Exception? _sessionFault;
    private IPEndPoint? _endpoint;
    private bool _started, _disposed;
    private int _active;
    private long _accepted, _capacityRejected, _addressRejected, _completed, _commands, _frames;

    public TlsGatewayListener(IPAddress address, int port,
        IdentityTlsAuthorization authorization, X509Certificate2 certificate,
        WorldResultRouter router, TlsGatewayListenerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _options = options ?? new();
        if (port is < 0 or > 65535 || _options.MaxConnections is < 1 or > 4096 ||
            _options.MaxConnectionsPerAddress < 1 ||
            _options.MaxConnectionsPerAddress > _options.MaxConnections ||
            _options.Backlog is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(options));
        TlsWireConnection.Validate(_options.Connection ?? new());
        TlsWireConnection.ValidateCertificate(certificate);
        _listener = new TcpListener(address, port);
    }

    public IPEndPoint LocalEndpoint
    {
        get { lock (_gate) return _endpoint ?? throw new InvalidOperationException("Listener has not started"); }
    }

    public TlsGatewayListenerStatistics Statistics
    {
        get
        {
            lock (_gate) return new(_active, _addresses.Count, _accepted,
                _capacityRejected, _addressRejected, _completed, _commands, _frames);
        }
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) throw new InvalidOperationException("Listener is single-use");
            _started = true;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener.Start(_options.Backlog);
            _endpoint = (IPEndPoint)_listener.LocalEndpoint;
            _run = AcceptLoopAsync(_lifetime.Token);
            return _run;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                var address = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
                if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
                bool admitted;
                lock (_gate)
                {
                    _addresses.TryGetValue(address, out int count);
                    admitted = false;
                    if (_active >= _options.MaxConnections) _capacityRejected++;
                    else if (count >= _options.MaxConnectionsPerAddress) _addressRejected++;
                    else
                    {
                        _active++;
                        _accepted++;
                        _addresses[address] = count + 1;
                        admitted = true;
                    }
                }
                if (!admitted)
                {
                    client.Dispose();
                    continue;
                }
                var session = RunClientAsync(client, address, token);
                lock (_gate) _sessions.Add(session);
                _ = session.ContinueWith(Finished, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            _listener.Stop();
            _lifetime!.Cancel();
            Task[] pending;
            lock (_gate) pending = _sessions.ToArray();
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        lock (_gate)
            if (_sessionFault is not null)
                throw new InvalidOperationException("TLS session pipeline failed", _sessionFault);
    }

    private async Task RunClientAsync(TcpClient client, IPAddress address, CancellationToken token)
    {
        try
        {
            var result = await _authorization.RunAuthenticatedSocketAsync(client,
                _certificate, _router, _options.Connection, token).ConfigureAwait(false);
            lock (_gate)
            {
                _commands += result.CommandsAccepted;
                _frames += result.ReliableFramesSent;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            lock (_gate) _sessionFault ??= error;
            _lifetime!.Cancel();
            throw;
        }
        finally
        {
            client.Dispose();
            lock (_gate)
            {
                _active--;
                _completed++;
                if (_addresses[address] == 1) _addresses.Remove(address);
                else _addresses[address]--;
            }
        }
    }

    private void Finished(Task task)
    {
        lock (_gate)
        {
            _sessions.Remove(task);
            if (task.IsFaulted) _ = task.Exception;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_dispose is not null) return new(_dispose);
            _disposed = true;
            _dispose = DisposeCoreAsync(_run);
            return new(_dispose);
        }
    }

    private async Task DisposeCoreAsync(Task? run)
    {
        _lifetime?.Cancel();
        try
        {
            if (run is not null) await run.ConfigureAwait(false);
        }
        finally
        {
            _listener.Stop();
            _lifetime?.Dispose();
        }
    }
}
