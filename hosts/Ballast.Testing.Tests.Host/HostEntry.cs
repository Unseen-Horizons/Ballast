// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Builder;

namespace Ballast.Testing.Tests.Host;

/// <summary>The entry point of the API that the factory tests boot. It has one route, so the host has a real pipeline.</summary>
public sealed class HostEntry
{
    /// <summary>Starts the API.</summary>
    /// <param name="args">The command-line arguments.</param>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.MapGet("/ping", () => "pong");
        app.Run();
    }
}
