// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Testing.Fakes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ballast.Testing.Hosting;

/// <summary>
/// A web application built and started in memory, with a client for it, for tests that need the whole request pipeline and no
/// port. Its logs go to a <see cref="CapturedLogs"/>, so a test can assert on what the app wrote.
/// </summary>
public sealed class InMemoryApp : IAsyncDisposable
{
    private InMemoryApp(WebApplication app, HttpClient client, CapturedLogs logs)
    {
        App = app;
        Client = client;
        Logs = logs;
    }

    /// <summary>The running application.</summary>
    public WebApplication App { get; }

    /// <summary>A client for the application. It is disposed with this object.</summary>
    public HttpClient Client { get; }

    /// <summary>The log entries the application wrote, at Debug and above.</summary>
    public CapturedLogs Logs { get; }

    /// <summary>
    /// Builds and starts an application on the test server. <paramref name="configure"/> adds services and configuration to the
    /// builder, and <paramref name="pipeline"/> builds the request pipeline after <c>Build()</c>.
    /// </summary>
    /// <param name="configure">Adds services and configuration to the builder. Required.</param>
    /// <param name="pipeline">Builds the request pipeline. Required.</param>
    /// <param name="environmentName">The environment the app runs in: "Production" (the default, as a deployed app runs) or another name.</param>
    /// <returns>The running application.</returns>
    public static async Task<InMemoryApp> StartAsync(
        Action<WebApplicationBuilder> configure,
        Action<WebApplication> pipeline,
        string environmentName = "Production")
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(pipeline);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.WebHost.UseTestServer();

        var logs = new CapturedLogs();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddProvider(logs);

        configure(builder);
        var app = builder.Build();
        pipeline(app);
        await app.StartAsync();
        return new InMemoryApp(app, app.GetTestClient(), logs);
    }

    /// <summary>Stops the application and disposes the client.</summary>
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
