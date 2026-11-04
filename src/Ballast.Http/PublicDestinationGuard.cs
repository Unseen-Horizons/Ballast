// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Sockets;

namespace Ballast.Http;

/// <summary>
/// Rejects URLs that resolve to loopback, link-local, private or otherwise internal addresses. Two checks, because one is not
/// enough: <see cref="EnsurePublicDestination"/> before a request (a clear answer and no connection), and
/// <see cref="ConnectToPublicOnlyAsync"/> at connect time, which is what actually enforces the rule.
/// </summary>
/// <remarks>
/// <see cref="EnsurePublicDestination"/> alone could not enforce it: it resolves the name, and then the HTTP handler resolves it
/// again when it connects. A record with a short TTL that answers public on the first lookup and 169.254.169.254 on the second
/// walks past the check (DNS rebinding). Deciding at connect time removes the window, because the address vetted there is the
/// address the socket is opened to. The connect check runs for every connection the handler makes, redirect hops included.
/// </remarks>
public static class PublicDestinationGuard
{
    /// <summary>
    /// Refuses a URL that is not an absolute http or https URL, or whose host does not resolve to public addresses only. A
    /// literal IP address is judged as it is; a name is resolved, and every address it gives must be public.
    /// </summary>
    /// <param name="url">The absolute URL to check. Required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="url"/> is null.</exception>
    /// <exception cref="HttpSafetyException">
    /// <see cref="HttpSafetyReason.InvalidUrl"/> when the text is not an absolute http or https URL;
    /// <see cref="HttpSafetyReason.HostNotResolved"/> when the name does not resolve;
    /// <see cref="HttpSafetyReason.NonPublicAddress"/> when any address is not public.
    /// </exception>
    public static void EnsurePublicDestination(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new HttpSafetyException(HttpSafetyReason.InvalidUrl, "A URL must be an absolute http or https URL.");
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = Dns.GetHostAddresses(uri.Host);
            }
            catch (SocketException)
            {
                throw new HttpSafetyException(HttpSafetyReason.HostNotResolved,
                    $"Host '{uri.Host}' could not be resolved.", uri);
            }
        }

        if (addresses.Length == 0 || addresses.Any(IsInternal))
        {
            throw new HttpSafetyException(HttpSafetyReason.NonPublicAddress,
                $"Host '{uri.Host}' resolves to a non-public address.", uri);
        }
    }

    /// <summary>
    /// The connect callback for a <c>SocketsHttpHandler</c>: it connects only to addresses <see cref="IsInternal"/> accepts. Every
    /// candidate address must be public, not just one of them, so a host that publishes one public and one private address is
    /// refused, rather than reachable on the private one by a later connection.
    /// </summary>
    /// <param name="context">The connection the handler is opening. Required.</param>
    /// <param name="ct">Cancels the connect.</param>
    /// <returns>The connected stream. The caller owns it.</returns>
    /// <exception cref="HttpSafetyException">
    /// <see cref="HttpSafetyReason.NonPublicAddress"/> when any candidate address is not public. The handler wraps it in an
    /// <see cref="HttpRequestException"/> whose <see cref="Exception.InnerException"/> is this exception.
    /// </exception>
    /// <remarks>A name that does not resolve here raises the resolver's own exception, which is not wrapped.</remarks>
    public static async ValueTask<Stream> ConnectToPublicOnlyAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var host = context.DnsEndPoint.Host;

        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        if (addresses.Length == 0 || addresses.Any(IsInternal))
        {
            throw new HttpSafetyException(HttpSafetyReason.NonPublicAddress,
                $"Host '{host}' resolves to a non-public address.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Whether an address is internal: loopback, link-local, private, carrier-grade NAT, the benchmarking range, multicast, the
    /// reserved and broadcast range, or the unspecified address. An IPv4 address carried inside an IPv6 one (the mapped form,
    /// NAT64 <c>64:ff9b::/96</c>, 6to4 <c>2002::/16</c> and the retired compatible form <c>::a.b.c.d</c>) is judged as the IPv4
    /// address it carries, so a public address behind one of those prefixes stays allowed.
    /// </summary>
    /// <param name="address">The address to judge. Required.</param>
    /// <returns>True when the address is not public.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
    public static bool IsInternal(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                return IsInternal(address.MapToIPv4());
            }

            // An IPv4 address carried inside an IPv6 one reaches that IPv4 host, so the embedded address is judged like
            // any other: NAT64 (64:ff9b::/96), 6to4 (2002::/16) and the retired IPv4-compatible form (::a.b.c.d). A
            // public address behind one of these prefixes stays allowed. Loopback (::1) and the unspecified address
            // are caught before and after this, so the compatible form never has to decide them.
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B && bytes[4..12].All(b => b == 0))
            {
                return IsInternal(new IPAddress(bytes[12..16]));
            }
            if (bytes[0] == 0x20 && bytes[1] == 0x02)
            {
                return IsInternal(new IPAddress(bytes[2..6]));
            }
            if (bytes[..12].All(b => b == 0))
            {
                return IsInternal(new IPAddress(bytes[12..16]));
            }

            // Unique local addresses (fc00::/7) and the unspecified address.
            return (bytes[0] & 0xFE) == 0xFC || address.Equals(IPAddress.IPv6Any);
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            0 => true,
            10 => true,
            100 when octets[1] >= 64 && octets[1] <= 127 => true,
            127 => true,
            169 when octets[1] == 254 => true,
            172 when octets[1] >= 16 && octets[1] <= 31 => true,
            192 when octets[1] == 0 && octets[2] == 0 => true,
            192 when octets[1] == 168 => true,
            198 when octets[1] is 18 or 19 => true,
            >= 224 => true,
            _ => false
        };
    }
}
