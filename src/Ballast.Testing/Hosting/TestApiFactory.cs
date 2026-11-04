// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Ballast.Testing.Hosting;

/// <summary>
/// Boots a real API in-process, for tests that exercise routing, authorization, model validation and the error shape together.
/// A test that calls a manager directly leaves all of that unverified.
/// <para>
/// A derived factory fills <see cref="Settings"/> in its constructor, and overrides <see cref="PrepareAsync"/> and
/// <see cref="TeardownAsync"/> to create and drop its database. The base class does the rest: the environment name, the settings,
/// the removal of background hosted services (they would run against the test database on their own schedule), the lifecycle of
/// the host, and a client that logs in over HTTP, so a test carries a token the login endpoint really issues.
/// </para>
/// </summary>
/// <typeparam name="TEntryPoint">The API's entry point type (a public type in the API's assembly, such as its <c>Program</c>).</typeparam>
public abstract class TestApiFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>, IAsyncLifetime
    where TEntryPoint : class
{
    private readonly Dictionary<string, string?> _settings = new(StringComparer.Ordinal);

    /// <summary>
    /// The configuration to set in the test host, by key (such as <c>ConnectionStrings:DefaultConnection</c>). Fill it in the
    /// derived constructor; the host reads it when it is first built.
    /// </summary>
    protected IDictionary<string, string?> Settings => _settings;

    /// <summary>The environment the host runs in. Default <c>Development</c>.</summary>
    protected virtual string EnvironmentName => Environments.Development;

    /// <summary>Whether the API's hosted services are removed from the test host. Default true.</summary>
    protected virtual bool RemoveHostedServices => true;

    /// <summary>
    /// Creates the schema or seeds data, once, before the first test. The default does nothing. It runs with
    /// <see cref="CancellationToken.None"/>, because an assembly or collection fixture has no test to cancel with.
    /// </summary>
    /// <param name="ct">Cancels the preparation.</param>
    protected virtual Task PrepareAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Drops what <see cref="PrepareAsync"/> made, after the last test. The default does nothing. It runs after the host is
    /// stopped and disposed, so a hosted service that still uses the database has finished with it. It is called even when
    /// preparation failed part way.
    /// </summary>
    /// <param name="ct">Cancels the teardown.</param>
    protected virtual Task TeardownAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Runs <see cref="PrepareAsync"/>. Called once by xUnit before the first test of the collection.</summary>
    public ValueTask InitializeAsync() => new(PrepareAsync(CancellationToken.None));

    /// <summary>
    /// Stops and disposes the host first, then runs <see cref="TeardownAsync"/>. The host's hosted services may still use
    /// the database while they stop, so the database is dropped only once the host is gone. Teardown runs even when the host
    /// fails to dispose.
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            try
            {
                await TeardownAsync(CancellationToken.None);
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }
    }

    /// <summary>Applies the environment and the settings, and removes the hosted services when <see cref="RemoveHostedServices"/> is set.</summary>
    /// <param name="builder">The host builder.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        if (!RemoveHostedServices)
        {
            return;
        }

        builder.ConfigureServices(services =>
        {
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                services.Remove(hosted);
            }
        });
    }

    /// <summary>A client with no credentials.</summary>
    public HttpClient CreateAnonymousClient() => CreateClient();

    /// <summary>
    /// A client that carries a bearer token from a real login: it posts <paramref name="credentials"/> as JSON to
    /// <paramref name="loginPath"/> and sets the token from the answer's <paramref name="tokenProperty"/>.
    /// </summary>
    /// <param name="loginPath">The login route, such as <c>/auth/login</c>.</param>
    /// <param name="credentials">The JSON body to post.</param>
    /// <param name="tokenProperty">The property of the answer that holds the token. Default <c>accessToken</c>.</param>
    /// <exception cref="InvalidOperationException">The login was refused, or the answer has no token.</exception>
    public async Task<HttpClient> CreateBearerClientAsync(string loginPath, object credentials, string tokenProperty = "accessToken")
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(loginPath, credentials);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            client.Dispose();
            throw new InvalidOperationException($"Login at {loginPath} failed: {(int)response.StatusCode} {body}");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty(tokenProperty, out var token) || token.GetString() is not { Length: > 0 } value)
        {
            client.Dispose();
            throw new InvalidOperationException($"Login at {loginPath} answered without a '{tokenProperty}' string.");
        }

        client.DefaultRequestHeaders.Authorization = new("Bearer", value);
        return client;
    }
}
