// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;

namespace Ballast.Auth.Tests;

/// <summary>The sign-in flows over a string (GUID text) key, as GetThereAPI uses.</summary>
public sealed class StringKeyAuthFlowTests : AuthFlowSuite<StringUser, string, StringRefresh>
{
    /// <inheritdoc />
    protected override IdentityRole<string> NewRole(string name) => new IdentityRole(name);

    /// <inheritdoc />
    protected override string MissingUserId => "00000000-0000-0000-0000-000000000000";
}

/// <summary>The same sign-in flows over an int key, as Chistify's Identity tables use.</summary>
public sealed class IntKeyAuthFlowTests : AuthFlowSuite<IntUser, int, IntRefresh>
{
    /// <inheritdoc />
    protected override IdentityRole<int> NewRole(string name) => new() { Name = name };

    /// <inheritdoc />
    protected override string MissingUserId => "999999";
}
