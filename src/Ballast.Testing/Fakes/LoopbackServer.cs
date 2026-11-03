// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ballast.Testing.Fakes;

/// <summary>
/// A tiny HTTP/1.1 server on the loopback address, for the tests that need a real socket: a connect-time check, or a client's own
/// redirect behaviour. It answers each request from a function and records each path asked for. Every answer closes the connection.
/// Nothing leaves the machine.
/// </summary>
public sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<string, string, (int Status, string Headers, string Body)> _respond;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accepting;
    private readonly object _gate = new();
    private readonly List<string> _paths = [];

    /// <summary>
    /// Starts listening on a free loopback port. Each request is answered with <paramref name="respond"/>(method, path), which gives
    /// the status, the extra header lines (each ending in CRLF, or empty) and the body.
    /// </summary>
    /// <param name="respond">Builds the answer for a method and a path. Required.</param>
    public LoopbackServer(Func<string, string, (int Status, string Headers, string Body)> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        _respond = respond;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = Task.Run(() => AcceptAsync(_stop.Token));
    }

    /// <summary>The port the server listens on.</summary>
    public int Port { get; }

    /// <summary>The base address of the server, <c>http://127.0.0.1:{port}</c>.</summary>
    public Uri BaseAddress => new($"http://127.0.0.1:{Port}");

    /// <summary>The request paths the server answered, in order. A copy.</summary>
    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_gate)
            {
                return _paths.ToArray();
            }
        }
    }

    /// <summary>Stops listening and waits for the accept loop to end.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _accepting;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopping the listener is how the accept loop ends, so these are the expected ends of it.
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client), CancellationToken.None);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync() ?? string.Empty;
            while ((await reader.ReadLineAsync()) is { Length: > 0 })
            {
                // Headers are read and ignored: the answer depends only on the method and the path.
            }

            var parts = requestLine.Split(' ');
            var method = parts.Length > 0 ? parts[0] : string.Empty;
            var path = parts.Length > 1 ? parts[1] : string.Empty;
            lock (_gate)
            {
                _paths.Add(path);
            }

            var (status, headers, body) = _respond(method, path);
            var bytes = Encoding.UTF8.GetBytes(body);
            var head = $"HTTP/1.1 {status} Test\r\n{headers}Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
        }
    }
}
