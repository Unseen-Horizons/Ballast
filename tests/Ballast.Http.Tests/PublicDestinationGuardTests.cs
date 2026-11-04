// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

namespace Ballast.Http.Tests;

/// <summary>
/// The SSRF guard, ported from GetThere's <c>FeedUrlSsrfTests</c> and <c>PublicDestinationGuard</c>. Feed URLs are operator-supplied
/// and fetched server-side, so without this check the importer is a proxy into its own network, including cloud metadata endpoints.
/// </summary>
public class PublicDestinationGuardTests
{
    [Theory]
    [InlineData("http://127.0.0.1/gtfs.zip")]
    [InlineData("http://localhost/gtfs.zip")]
    [InlineData("http://10.0.0.5/gtfs.zip")]
    [InlineData("http://172.16.3.4/gtfs.zip")]
    [InlineData("http://172.31.255.1/gtfs.zip")]
    [InlineData("http://192.168.1.10/gtfs.zip")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://0.0.0.0/gtfs.zip")]
    [InlineData("http://[::1]/gtfs.zip")]
    // Ranges the original check missed. 100.64/10 is carrier-grade NAT, which is where several
    // cloud providers put their internal service endpoints; 192.0.0.0/24 is IETF protocol
    // assignments; 198.18/15 is the benchmarking range. None of them are routable on the internet.
    [InlineData("http://100.64.0.1/gtfs.zip")]
    [InlineData("http://100.127.255.254/gtfs.zip")]
    [InlineData("http://192.0.0.1/gtfs.zip")]
    [InlineData("http://198.18.0.1/gtfs.zip")]
    [InlineData("http://198.19.255.255/gtfs.zip")]
    public void Rejects_internal_destinations(string url)
    {
        var ex = Assert.Throws<HttpSafetyException>(() => PublicDestinationGuard.EnsurePublicDestination(url));
        Assert.Contains(ex.Reason, new[] { HttpSafetyReason.NonPublicAddress, HttpSafetyReason.HostNotResolved });
    }

    /// <summary>
    /// Guards the edges of the reserved ranges, so a future tightening cannot quietly swallow neighbouring public space. 100.63.x and
    /// 100.128.x sit either side of carrier-grade NAT, and 198.17/198.20 either side of the benchmarking range.
    /// </summary>
    [Theory]
    [InlineData("http://100.63.255.255/gtfs.zip")]
    [InlineData("http://100.128.0.1/gtfs.zip")]
    [InlineData("http://198.17.255.255/gtfs.zip")]
    [InlineData("http://198.20.0.1/gtfs.zip")]
    [InlineData("http://192.0.1.1/gtfs.zip")]
    public void Allows_addresses_just_outside_the_reserved_ranges(string url)
    {
        PublicDestinationGuard.EnsurePublicDestination(url);
    }

    /// <summary>
    /// An IPv4 address carried inside an IPv6 one (NAT64, 6to4, the old compatible form, the mapped form) is judged as the IPv4
    /// address it carries, so a private one behind the prefix is refused and a public one stays allowed.
    /// </summary>
    [Theory]
    [InlineData("64:ff9b::a9fe:a9fe", true)]
    [InlineData("2002:a9fe:a9fe::", true)]
    [InlineData("::7f00:1", true)]
    [InlineData("::ffff:169.254.169.254", true)]
    [InlineData("64:ff9b::808:808", false)]
    [InlineData("2002:808:808::", false)]
    public void Embedded_ipv4_in_an_ipv6_address_is_judged_as_that_ipv4_address(string address, bool internalExpected)
    {
        Assert.Equal(internalExpected, PublicDestinationGuard.IsInternal(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("0.0.0.0", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("::", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fec0::1", true)]
    [InlineData("ff02::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("2606:4700:4700::1111", false)]
    [InlineData("8.8.8.8", false)]
    public void Judges_the_plain_ipv4_and_ipv6_ranges(string address, bool internalExpected)
    {
        Assert.Equal(internalExpected, PublicDestinationGuard.IsInternal(IPAddress.Parse(address)));
    }

    [Fact]
    public void IsInternal_refuses_a_null_address()
    {
        Assert.Throws<ArgumentNullException>(() => PublicDestinationGuard.IsInternal(null!));
    }

    [Theory]
    [InlineData("ftp://example.com/gtfs.zip")]
    [InlineData("file:///c:/windows/win.ini")]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    public void Rejects_non_http_schemes_as_an_invalid_url(string url)
    {
        var ex = Assert.Throws<HttpSafetyException>(() => PublicDestinationGuard.EnsurePublicDestination(url));
        Assert.Equal(HttpSafetyReason.InvalidUrl, ex.Reason);
    }

    [Fact]
    public void Rejects_a_host_that_does_not_resolve_with_its_own_reason()
    {
        var ex = Assert.Throws<HttpSafetyException>(() =>
            PublicDestinationGuard.EnsurePublicDestination("https://this-host-does-not-exist.invalid/gtfs.zip"));

        Assert.Equal(HttpSafetyReason.HostNotResolved, ex.Reason);
        Assert.Equal("https://this-host-does-not-exist.invalid/gtfs.zip", ex.RedactedUrl);
    }

    [Theory]
    [InlineData("http://93.184.216.34/gtfs.zip")]
    [InlineData("https://8.8.8.8/gtfs.zip")]
    public void Allows_public_addresses(string url)
    {
        PublicDestinationGuard.EnsurePublicDestination(url);
    }

    [Fact]
    public void A_refusal_names_the_redacted_url_and_never_the_query_or_user_information()
    {
        var ex = Assert.Throws<HttpSafetyException>(() =>
            PublicDestinationGuard.EnsurePublicDestination("http://user:secret@127.0.0.1:8080/gtfs.zip?apikey=SECRET123"));

        Assert.Equal(HttpSafetyReason.NonPublicAddress, ex.Reason);
        Assert.Equal("http://127.0.0.1:8080/gtfs.zip?[redacted]", ex.RedactedUrl);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET123", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_connect_check_refuses_a_name_that_resolves_to_a_loopback_address_before_any_byte_is_sent()
    {
        // "localhost" resolves to the loopback address on every machine that runs these tests. The handler below has no pre-flight
        // check, so the refusal can only come from the connect-time callback.
        await using var server = new LoopbackServer((_, _) => (200, string.Empty, "must not be reached"));
        using var handler = new SocketsHttpHandler { ConnectCallback = PublicDestinationGuard.ConnectToPublicOnlyAsync };
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://localhost:{server.Port}/x"));

        var refusal = Assert.IsType<HttpSafetyException>(ex.InnerException);
        Assert.Equal(HttpSafetyReason.NonPublicAddress, refusal.Reason);
        Assert.Empty(server.Paths);
    }

    [Fact]
    public async Task The_connect_check_refuses_a_loopback_literal_before_any_byte_is_sent()
    {
        await using var server = new LoopbackServer((_, _) => (200, string.Empty, "must not be reached"));
        using var handler = new SocketsHttpHandler { ConnectCallback = PublicDestinationGuard.ConnectToPublicOnlyAsync };
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://127.0.0.1:{server.Port}/x"));

        Assert.Equal(HttpSafetyReason.NonPublicAddress, Assert.IsType<HttpSafetyException>(ex.InnerException).Reason);
        Assert.Empty(server.Paths);
    }
}
