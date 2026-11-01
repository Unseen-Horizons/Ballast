// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ballast.Http.Tests;

/// <summary>
/// A tiny HTTP/1.1 server on the loopback address, for the tests that need a real socket: the connect-time check and the handler's
/// own redirect behaviour. It answers each request from a function and records each path asked for. Nothing leaves the machine.
/// </summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<string, string, (int Status, string Headers, string Body)> _respond;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accepting;
    private readonly object _gate = new();
    private readonly List<string> _paths = [];

    /// <summary>Starts listening on a free loopback port and answers each request with <paramref name="respond"/>(method, path).</summary>
    public LoopbackServer(Func<string, string, (int Status, string Headers, string Body)> respond)
    {
        _respond = respond;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = Task.Run(() => AcceptAsync(_stop.Token));
    }

    /// <summary>The port the server listens on.</summary>
    public int Port { get; }

    /// <summary>The request paths the server answered, in order.</summary>
    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_gate) return _paths.ToArray();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try { await _accepting; }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
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
            string? line;
            while ((line = await reader.ReadLineAsync()) is { Length: > 0 }) { }

            var parts = requestLine.Split(' ');
            var method = parts.Length > 0 ? parts[0] : string.Empty;
            var path = parts.Length > 1 ? parts[1] : string.Empty;
            lock (_gate) _paths.Add(path);

            var (status, headers, body) = _respond(method, path);
            var bytes = Encoding.UTF8.GetBytes(body);
            var head = $"HTTP/1.1 {status} Test\r\n{headers}Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
            var wire = Encoding.ASCII.GetBytes(head);
            await stream.WriteAsync(wire);
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
        }
    }
}
