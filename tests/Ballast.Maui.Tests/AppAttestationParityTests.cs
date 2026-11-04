// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Text;
using System.Text.Json;

using Ballast.AppAttestation;
using Ballast.Maui.AppAttestation;

namespace Ballast.Maui.Tests;

/// <summary>
/// The wire the app and the server share. The server's answers are serialised with ASP.NET's web defaults and read by the app's
/// session code; the app's requests are read back by the server's own types. The client's JSON context is internal, so the parity is
/// checked through the service's real request path, not by reaching into it.
/// </summary>
public class AppAttestationParityTests
{
    private static readonly JsonSerializerOptions ServerWire = new(JsonSerializerDefaults.Web);

    /// <summary>The server's side: it answers each route with its own answer type, serialised the way ASP.NET does, and records what the app sent.</summary>
    private sealed class ServerAnswers : HttpMessageHandler
    {
        /// <summary>The bodies the app posted to create a session, in order.</summary>
        public List<string> SessionBodies { get; } = [];

        /// <summary>The nonce the server issues.</summary>
        public string Nonce { get; init; } = "server-nonce";

        /// <summary>The session token the server issues.</summary>
        public string AccessToken { get; init; } = "server-token";

        /// <summary>The session's lifetime in seconds.</summary>
        public int ExpiresInSeconds { get; init; } = 3600;

        /// <summary>The web ticket the server issues.</summary>
        public string Ticket { get; init; } = "server-ticket";

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/app/session/nonce")
                return Answer(new AppNonceResponse { Nonce = Nonce });

            if (path == "/app/session")
            {
                SessionBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Answer(new AppSessionResponse { AccessToken = AccessToken, ExpiresInSeconds = ExpiresInSeconds });
            }

            if (path == "/app/session/web-ticket")
                return Answer(new AppWebTicketResponse { Ticket = Ticket });

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Answer<T>(T value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, ServerWire), Encoding.UTF8, "application/json") };
    }

    private static (AppSessionService Service, ServerAnswers Server, FakeAttestation Attestation, StepClock Clock) Make(ServerAnswers? server = null)
    {
        server ??= new ServerAnswers();
        var attestation = new FakeAttestation();
        var clock = new StepClock();
        var http = new HttpClient(server) { BaseAddress = new Uri("https://map.test/") };
        return (new AppSessionService(attestation, http, () => "install-0000000001", clock), server, attestation, clock);
    }

    [Fact]
    public async Task The_app_reads_the_servers_nonce_answer()
    {
        var (service, _, attestation, _) = Make();

        await service.GetTokenAsync();

        Assert.Equal("server-nonce", attestation.Nonces.Single());
    }

    [Fact]
    public async Task The_app_reads_the_servers_session_answer()
    {
        var (service, _, _, _) = Make();

        Assert.Equal("server-token", await service.GetTokenAsync());
    }

    [Fact]
    public async Task The_app_reads_the_servers_session_lifetime()
    {
        var (service, server, _, clock) = Make();
        await service.GetTokenAsync();

        // An hour lasts; the renewal falls five minutes before it ends, so 3000 seconds in nothing happens and 3400 seconds in it does.
        clock.Advance(TimeSpan.FromSeconds(3000));
        await service.GetTokenAsync();
        Assert.Single(server.SessionBodies);

        clock.Advance(TimeSpan.FromSeconds(400));
        await service.GetTokenAsync();
        Assert.Equal(2, server.SessionBodies.Count);
    }

    [Fact]
    public async Task The_app_reads_the_servers_web_ticket_answer()
    {
        var (service, _, _, _) = Make();

        var result = await service.GetWebTicketAsync();

        Assert.Equal("server-ticket", result.Ticket);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task The_server_reads_what_the_app_sends_to_create_a_session()
    {
        var (service, server, attestation, _) = Make();
        attestation.Token = "play-token";

        await service.GetTokenAsync();

        var sent = JsonSerializer.Deserialize<CreateAppSessionRequest>(server.SessionBodies.Single(), ServerWire);
        Assert.NotNull(sent);
        Assert.Equal("android", sent.Platform);
        Assert.Equal("install-0000000001", sent.InstallId);
        Assert.Equal("server-nonce", sent.Nonce);
        Assert.Equal("play-token", sent.IntegrityToken);
    }

    [Fact]
    public async Task A_missing_integrity_token_is_sent_as_null_and_read_as_null()
    {
        var (service, server, attestation, _) = Make();
        attestation.Token = null;

        await service.GetTokenAsync();

        var sent = JsonSerializer.Deserialize<CreateAppSessionRequest>(server.SessionBodies.Single(), ServerWire);
        Assert.NotNull(sent);
        Assert.Null(sent.IntegrityToken);
    }

    [Fact]
    public async Task The_app_and_the_server_use_the_same_names_for_the_session_request()
    {
        var (service, server, _, _) = Make();
        await service.GetTokenAsync();

        var serverNames = JsonSerializer.Serialize(new CreateAppSessionRequest("android", "install", "nonce", "token"), ServerWire);
        var sentNames = server.SessionBodies.Single();

        Assert.Equal(PropertyNames(serverNames), PropertyNames(sentNames));
    }

    [Fact]
    public void The_app_types_are_read_back_by_the_server_types()
    {
        var nonce = JsonSerializer.Deserialize<AppNonceResponse>(JsonSerializer.Serialize(new AppNonceDto { Nonce = "n-1" }, ServerWire), ServerWire);
        var session = JsonSerializer.Deserialize<AppSessionResponse>(JsonSerializer.Serialize(new AppSessionDto { AccessToken = "t-1", ExpiresInSeconds = 900 }, ServerWire), ServerWire);
        var ticket = JsonSerializer.Deserialize<AppWebTicketResponse>(JsonSerializer.Serialize(new AppWebTicketDto { Ticket = "k-1" }, ServerWire), ServerWire);
        var request = JsonSerializer.Deserialize<CreateAppSessionRequest>(
            JsonSerializer.Serialize(new AppSessionRequestDto("ios", "i-1", "n-2", null), ServerWire),
            ServerWire);

        Assert.Equal("n-1", nonce!.Nonce);
        Assert.Equal("t-1", session!.AccessToken);
        Assert.Equal(900, session.ExpiresInSeconds);
        Assert.Equal("k-1", ticket!.Ticket);
        Assert.Equal("ios", request!.Platform);
        Assert.Equal("i-1", request.InstallId);
        Assert.Equal("n-2", request.Nonce);
        Assert.Null(request.IntegrityToken);
    }

    private static string[] PropertyNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
    }
}
