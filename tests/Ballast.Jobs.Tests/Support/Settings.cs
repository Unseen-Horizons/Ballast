// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Configuration;

namespace Ballast.Jobs.Tests;

/// <summary>Configuration made from key and value pairs, for the tests that bind settings.</summary>
internal static class Settings
{
    /// <summary>The configuration with exactly these values.</summary>
    /// <param name="values">The key and value pairs, with keys such as <c>JobLeadership:Enabled</c>.</param>
    public static IConfiguration From(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
}
