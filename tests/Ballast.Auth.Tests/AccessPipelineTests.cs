// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Auth.Tests;

/// <summary>
/// A small web host in memory over a test database, with the library's JWT bearer pipeline and one authorised route.
/// </summary>
internal sealed class PipelineHost : IAsyncDisposable
{
    /// <summary>The address the host's user signs in with.</summary>
    public const string Email = "pipeline@example.test";

    /// <summary>The password the host's user signs in with.</summary>
    public const string Password = "Correct-horse-42!";

    private readonly SqlTestDatabase _database;
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private PipelineHost(SqlTestDatabase database, WebApplication app)
    {
        _database = database;
        _app = app;
        _client = app.GetTestClient();
    }

    /// <summary>Starts the host, or skips the calling test when SQL Server cannot be reached.</summary>
    /// <returns>The host.</returns>
    public static async Task<PipelineHost> StartAsync(IAuthAccountStore? store = null)
    {
        var database = await SqlTestDatabase.CreateAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Jwt:Key"] = TestConfig.Key;
        builder.Configuration["Jwt:Issuer"] = TestConfig.Issuer;
        builder.Configuration["Jwt:Audience"] = TestConfig.Audience;
        builder.Services.AddDbContext<AuthTestDb<StringUser, string, StringRefresh>>(o => o.UseSqlServer(database.ConnectionString));
        builder.Services.AddScoped<IAuthAudit>(sp => new AuditSink<AuthTestDb<StringUser, string, StringRefresh>>(sp.GetRequiredService<AuthTestDb<StringUser, string, StringRefresh>>()));
        if (store is not null)
        {
            builder.Services.AddSingleton<IAuthAccountStore>(store);
        }

        builder.Services.AddBallastAuth<StringUser, string, IdentityRole<string>, AuthTestDb<StringUser, string, StringRefresh>, StringRefresh>(builder.Configuration);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/me", () => Results.Ok("me")).RequireAuthorization();
        await app.StartAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthTestDb<StringUser, string, StringRefresh>>().Database.EnsureCreatedAsync();
        }

        return new PipelineHost(database, app);
    }

    /// <summary>A scope with its own database context.</summary>
    /// <returns>The scope, which must be disposed.</returns>
    public AsyncServiceScope Scope() => _app.Services.CreateAsyncScope();

    /// <summary>Creates the user and signs in, and returns the access token the sign-in issued.</summary>
    /// <returns>The access token.</returns>
    public async Task<string> SignInAsync()
    {
        await using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var user = new StringUser { UserName = Email, Email = Email, FullName = "Pipeline Test" };
        if (!(await users.CreateAsync(user, Password)).Succeeded)
        {
            throw new InvalidOperationException("Test user not created.");
        }

        var checkedUser = await scope.ServiceProvider.GetRequiredService<PasswordSignInService<StringUser, string, AuthTestDb<StringUser, string, StringRefresh>>>()
            .CheckPasswordAsync(Email, Password);
        var session = await scope.ServiceProvider.GetRequiredService<AuthSessionService<StringUser, string, StringRefresh, AuthTestDb<StringUser, string, StringRefresh>>>()
            .IssueAsync(checkedUser, rememberMe: false, "Login");
        return session.AccessToken;
    }

    /// <summary>A call to the authorised route with the token.</summary>
    /// <param name="token">The access token.</param>
    /// <returns>The response.</returns>
    public async Task<HttpResponseMessage> CallAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    /// <summary>A call to the authorised route with no token.</summary>
    /// <returns>The response.</returns>
    public async Task<HttpResponseMessage> AnonymousCallAsync() => await _client.GetAsync("/me");

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }
}

/// <summary>
/// The JWT bearer pipeline with the real account check: a token that validates is still refused once its account changes.
/// </summary>
public sealed class AccessPipelineTests
{
    [Fact]
    public async Task A_valid_token_passes_and_a_request_without_one_is_unauthorized()
    {
        await using var host = await PipelineHost.StartAsync();
        var token = await host.SignInAsync();

        using var allowed = await host.CallAsync(token);
        using var anonymous = await host.AnonymousCallAsync();

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task A_changed_security_stamp_refuses_the_token_at_the_next_request()
    {
        await using var host = await PipelineHost.StartAsync();
        var token = await host.SignInAsync();
        using (var before = await host.CallAsync(token))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        await using (var scope = host.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
            var user = await users.FindByEmailAsync(PipelineHost.Email) ?? throw new InvalidOperationException("No test user.");
            Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
        }

        using var after = await host.CallAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task A_locked_account_loses_its_access_tokens_at_once()
    {
        await using var host = await PipelineHost.StartAsync();
        var token = await host.SignInAsync();

        await using (var scope = host.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
            var user = await users.FindByEmailAsync(PipelineHost.Email) ?? throw new InvalidOperationException("No test user.");
            await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        }

        using var after = await host.CallAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task A_deleted_account_loses_its_access_tokens()
    {
        await using var host = await PipelineHost.StartAsync();
        var token = await host.SignInAsync();

        await using (var scope = host.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthTestDb<StringUser, string, StringRefresh>>();
            var user = await db.Users.SingleAsync(u => u.Email == PipelineHost.Email);
            db.RefreshTokens.RemoveRange(db.RefreshTokens);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }

        using var after = await host.CallAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }
}
