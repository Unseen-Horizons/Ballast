// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

using Ballast.AspNetCore;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ballast.AppAttestation.Tests.Support;

/// <summary>
/// The real app-session pieces (the thin controller, the gate, the scheme, the limits and the registration an app uses) on a test
/// server with a fake decoder, no database and no Google.
/// </summary>
public sealed class AppAttestationTestHost : IAsyncDisposable
{
    public const string SessionKey = "app-session-signing-key-long-enough-to-be-valid-0123456789";
    public const string UserKey = "integration-signing-key-long-enough-to-be-valid-0123456789";
    public const string UserIssuer = "example-api";
    public const string UserAudience = "example-app";
    public const string Package = "com.example.app";
    public const string Issuer = "example-session-issuer";
    public const string Audience = "example-app-session";
    public const string Header = "X-App-Session";
    public const string Cookie = "example_app";
    public const string Prefix = "/view/";

    private readonly WebApplication _app;

    public FakeClock Clock { get; }
    public FakeDecoder Decoder { get; }
    public CapturingLoggerProvider Logs { get; }
    public HttpClient Client { get; }
    public IServiceProvider Services => _app.Services;

    private AppAttestationTestHost(WebApplication app, FakeClock clock, FakeDecoder decoder, CapturingLoggerProvider logs, HttpClient client)
    {
        _app = app;
        Clock = clock;
        Decoder = decoder;
        Logs = logs;
        Client = client;
    }

    /// <summary>
    /// Starts the host. The defaults are a Development run with the bypass on and enforcement off. Each argument changes one thing a test
    /// needs to change.
    /// </summary>
    /// <param name="enforce">The value of <c>AppAttestation:Enforce</c>.</param>
    /// <param name="environment">The environment name.</param>
    /// <param name="allowUnattested">The value of <c>AppAttestation:AllowUnattested</c>.</param>
    /// <param name="developerCertificates">Developer certificates, as numbered configuration keys.</param>
    /// <param name="tinyLimit">The limit on the gated data route, per caller, per minute.</param>
    /// <param name="remoteIp">A peer address for the requests, when a test needs one.</param>
    /// <param name="storeCapacity">The capacity of the in-memory stores.</param>
    /// <param name="beforeAppAttestation">Registers services before the library, so a store the test supplies is the one kept.</param>
    /// <param name="adjustSetup">Changes the setup after the defaults, for a test of a different app value.</param>
    /// <param name="serviceAccountJson">The Google service account, when a test sets one.</param>
    public static async Task<AppAttestationTestHost> StartAsync(
        bool enforce = false,
        string environment = "Development",
        bool allowUnattested = true,
        string[]? developerCertificates = null,
        int tinyLimit = 1000,
        string? remoteIp = null,
        int storeCapacity = 20_000,
        Action<IServiceCollection>? beforeAppAttestation = null,
        Action<AppAttestationSetup>? adjustSetup = null,
        string? serviceAccountJson = null)
    {
        var clock = new FakeClock();
        var decoder = new FakeDecoder();
        var logs = new CapturingLoggerProvider();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddProvider(logs);

        var settings = new Dictionary<string, string?>
        {
            ["AppSession:Key"] = SessionKey,
            ["AppSession:StoreCapacity"] = storeCapacity.ToString(CultureInfo.InvariantCulture),
            ["AppAttestation:Enforce"] = enforce ? "true" : "false",
            ["AppAttestation:AllowUnattested"] = allowUnattested ? "true" : "false",
        };
        for (var i = 0; i < (developerCertificates?.Length ?? 0); i++)
            settings[$"AppAttestation:DeveloperCertificates:{i}"] = developerCertificates![i];
        if (serviceAccountJson is not null)
            settings["PlayIntegrity:ServiceAccountJson"] = serviceAccountJson;
        builder.Configuration.AddInMemoryCollection(settings);

        // Registered before the library so its TryAdd keeps these.
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<IPlayIntegrityDecoder>(decoder);
        beforeAppAttestation?.Invoke(builder.Services);

        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(pm =>
            {
                pm.ApplicationParts.Clear();
                pm.ApplicationParts.Add(new OnlyTypesPart(typeof(TestAppSessionController), typeof(TestGateController)));
            });
        builder.Services.AddProblemResponses();

        builder.Services.AddAuthentication(options => options.DefaultScheme = TestBearerHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>(TestBearerHandler.SchemeName, _ => { })
            .AddAppAttestationScheme();
        builder.Services.AddAuthorization();

        builder.Services.AddAppAttestation(builder.Configuration, builder.Environment, setup =>
        {
            setup.PackageName = Package;
            setup.Issuer = Issuer;
            setup.Audience = Audience;
            setup.HeaderName = Header;
            setup.CookieName = Cookie;
            setup.RedirectPrefix = Prefix;
            setup.SessionRequiredDetail = "This endpoint answers only the example app. Open it from the app, or sign in.";
            setup.SessionRequiredCode = AppAttestationCodes.SessionRequired;
            setup.AttestationFailedCode = AppAttestationCodes.AttestationFailed;
            setup.AttestationUnavailableCode = AppAttestationCodes.AttestationUnavailable;
            adjustSetup?.Invoke(setup);
        });

        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(AppSessionContext.RateLimitKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100_000,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            limiter.AddAppAttestationLimit("AppSession", 10);
            limiter.AddAppAttestationLimit("Tiny", tinyLimit);
            limiter.RejectionStatusCode = 429;
        });

        var app = builder.Build();

        // A test server has no peer address; a test that needs to see what a log line does with a real one sets it here.
        if (remoteIp is not null)
        {
            var address = System.Net.IPAddress.Parse(remoteIp);
            app.Use((context, next) =>
            {
                context.Connection.RemoteIpAddress = address;
                return next(context);
            });
        }

        app.UseProblemResponses();
        app.UseAuthentication();
        app.UseAppAttestationGate();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        return new AppAttestationTestHost(app, clock, decoder, logs, app.GetTestClient());
    }

    /// <summary>Runs the real bootstrap: a nonce, then a session answered by the fake decoder (Play recognised).</summary>
    /// <param name="platform">The platform the install reports.</param>
    /// <param name="installId">The install's id.</param>
    /// <param name="verdict">Builds the verdict from the nonce. Play recognised by default.</param>
    /// <returns>The session token.</returns>
    public async Task<string> CreateSessionAsync(string platform = "android", string installId = "install-0000000001", Func<string, IntegrityVerdict>? verdict = null)
    {
        var nonceResponse = await Client.GetAsync("/app/session/nonce");
        nonceResponse.EnsureSuccessStatusCode();
        var nonce = JsonDocument.Parse(await nonceResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("nonce").GetString()!;

        Decoder.ForToken = _ => (verdict ?? Verdicts.PlayRecognised)(nonce);

        var response = await Client.PostAsJsonAsync("/app/session", new { platform, installId, nonce, integrityToken = "opaque-token" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"session failed: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("accessToken").GetString()!;
    }

    /// <summary>A user access token: the user key and audience, a <c>sub</c>, and no session claims.</summary>
    /// <param name="userId">The user's id.</param>
    /// <returns>The token.</returns>
    public static string UserJwt(string userId = "user-1") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", userId), new Claim("role", "Admin")]),
            Issuer = UserIssuer,
            Audience = UserAudience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(UserKey)), SecurityAlgorithms.HmacSha256),
        });

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
