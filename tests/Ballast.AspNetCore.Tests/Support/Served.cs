// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ballast.AspNetCore.Tests.Support;

/// <summary>An in-memory application with a client, for the tests that need the whole pipeline.</summary>
internal sealed class Served : IAsyncDisposable
{
    private static readonly Regex _traceIdMember = new("\"traceId\":\"[^\"]*\"", RegexOptions.CultureInvariant);

    private Served(WebApplication app, HttpClient client, CapturedLogs logs)
    {
        App = app;
        Client = client;
        Logs = logs;
    }

    /// <summary>The running application.</summary>
    public WebApplication App { get; }

    /// <summary>A client for the application.</summary>
    public HttpClient Client { get; }

    /// <summary>The log entries the application wrote.</summary>
    public CapturedLogs Logs { get; }

    /// <summary>
    /// Builds and starts an application in memory. <paramref name="configure"/> adds services and configuration, and
    /// <paramref name="pipeline"/> builds the request pipeline after <c>Build()</c>.
    /// </summary>
    /// <param name="configure">Adds services and configuration to the builder.</param>
    /// <param name="pipeline">Builds the request pipeline.</param>
    /// <returns>The running application.</returns>
    public static async Task<Served> StartAsync(Action<WebApplicationBuilder> configure, Action<WebApplication> pipeline)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();

        var logs = new CapturedLogs();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddProvider(logs);

        configure(builder);
        var app = builder.Build();
        pipeline(app);
        await app.StartAsync();
        return new Served(app, app.GetTestClient(), logs);
    }

    /// <summary>
    /// The body with every trace reference replaced by <c>T</c>. A trace reference is different on every request, so the
    /// tests compare the rest of an answer and read the reference from the header.
    /// </summary>
    public static string WithoutTraceId(string json) => _traceIdMember.Replace(json, "\"traceId\":\"T\"");

    /// <summary>Stops the application and releases the client.</summary>
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
