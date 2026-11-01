// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Ballast.Email.Tests;

/// <summary>A host environment with the name the test gives, so the Development default can be tested either way.</summary>
public sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
    /// <inheritdoc />
    public string EnvironmentName { get; set; } = environmentName;

    /// <inheritdoc />
    public string ApplicationName { get; set; } = "Ballast.Email.Tests";

    /// <inheritdoc />
    public string ContentRootPath { get; set; } = string.Empty;

    /// <inheritdoc />
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
