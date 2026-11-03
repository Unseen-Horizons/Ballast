// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Android.Gms.Extensions;

using Ballast.Maui.Diagnostics;
using Ballast.Maui.Reviews;

using Google.Android.Play.Core.Review;
using Google.Android.Play.Core.Review.Model;

namespace Ballast.Maui.Reviews;

/// <summary>
/// The rating prompt through Google Play's in-app review. Play decides whether the rider sees it at all: a request can return with nothing
/// shown, and that is not a failure. Never throws.
/// </summary>
public sealed class PlayReviewPrompt : IStoreReviewPrompt
{
    private readonly IClientLog _log;

    /// <summary>Creates the prompt.</summary>
    /// <param name="log">Where a failed flow is recorded.</param>
    public PlayReviewPrompt(IClientLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public async Task<bool> RequestAsync()
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
                ?? throw new InvalidOperationException("There is no current activity to show the rating over.");
            var manager = ReviewManagerFactory.Create(activity);
            var info = await Await<ReviewInfo>(manager.RequestReviewFlow()).ConfigureAwait(false);
            await Await<Java.Lang.Object>(manager.LaunchReviewFlow(activity, info)).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning("ReviewPrompt", $"Play's review flow failed ({ex.GetType().Name}: {ex.Message}).");
            return false;
        }
    }

    private static Task<T> Await<T>(global::Android.Gms.Tasks.Task? task) where T : class, global::Android.Runtime.IJavaObject =>
        (task ?? throw new InvalidOperationException("Play returned no task.")).AsAsync<T>();
}
