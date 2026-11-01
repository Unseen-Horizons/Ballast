// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Jobs;

/// <summary>
/// The words of the 409 answer that <see cref="RequireJobHolderAttribute"/> gives a copy that does not hold the lock. The
/// code, the status and the <c>Retry-After</c> wait are fixed by the library; the title and the detail are the app's to set,
/// in English by default. Set them in the <see cref="JobLeadershipSetup.RequireJobHolder"/> of
/// <see cref="JobLeadershipRegistration.AddJobLeadership"/>.
/// </summary>
public sealed class RequireJobHolderOptions
{
    /// <summary>The default title: "This server copy is not running background jobs yet.".</summary>
    public const string DefaultTitle = "This server copy is not running background jobs yet.";

    /// <summary>The default detail: a deploy is in progress, and the wait is a minute.</summary>
    public const string DefaultDetail = "A deploy is in progress and the previous copy has not finished stopping. Try again in a minute.";

    /// <summary>The problem's title. Required, and not blank.</summary>
    public string Title { get; set; } = DefaultTitle;

    /// <summary>The problem's detail. Required, and not blank.</summary>
    public string Detail { get; set; } = DefaultDetail;
}
