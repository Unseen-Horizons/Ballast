// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates.Tests;

/// <summary>
/// The platform's own store can say whether a newer build exists, and that answer decides the banner ahead of the server's latest
/// build. The decision and the way the store is asked are pure, so they are tested here with a fake <see cref="IAppUpdater"/>.
/// </summary>
public class StoreUpdateRulesTests
{
    private const int Installed = 8;

    private static AppVersionResponse Info(int latest, int minimum = 0) =>
        new() { Platform = "Android", LatestBuild = latest, MinimumBuild = minimum };

    /// <summary>Answers what the test says, the way a platform updater would.</summary>
    private sealed class FakeUpdater : IAppUpdater
    {
        private readonly Func<Task<StoreUpdate?>> _answer;

        public FakeUpdater(StoreUpdate? answer) { _answer = () => Task.FromResult(answer); }

        public FakeUpdater(Func<Task<StoreUpdate?>> answer) { _answer = answer; }

        public bool CanStart(string? storeUrl) => true;

        public Task<AppUpdateStartResult> StartAsync(string? storeUrl) => Task.FromResult(AppUpdateStartResult.Started);

        public Task<StoreUpdate?> IsUpdateAvailableAsync() => _answer();
    }

    /// <summary>An updater that does not override <see cref="IAppUpdater.IsUpdateAvailableAsync"/> (Windows, iOS).</summary>
    private sealed class NoStoreUpdater : IAppUpdater
    {
        public bool CanStart(string? storeUrl) => false;

        public Task<AppUpdateStartResult> StartAsync(string? storeUrl) => Task.FromResult(AppUpdateStartResult.NoStoreLink);
    }

    /// <summary>What the app's check does with the two answers: ask the store, then decide.</summary>
    private static async Task<AppUpdateDecision> DecideAsync(AppVersionResponse? info, IAppUpdater? updater, int? dismissed = null)
    {
        var store = await AppVersionRules.AskStoreAsync(updater, TimeSpan.FromSeconds(5));
        return AppVersionRules.Decide(Installed, info, store, dismissed);
    }

    // ── Store answers ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]   // the server has not been given a latest build, and answers no numbers
    [InlineData(8)]   // the server says the installed build is the latest
    [InlineData(5)]   // the server is behind the store
    [InlineData(14)]  // the server agrees
    public async Task A_store_saying_an_update_is_available_shows_the_banner_whatever_the_servers_latest_build_is(int serverLatest)
    {
        AppVersionResponse? info = serverLatest > 0 ? Info(serverLatest) : null;

        var decision = await DecideAsync(info, new FakeUpdater(new StoreUpdate(true, 14)));

        Assert.Equal(AppUpdateDecision.UpdateAvailable, decision);
    }

    [Fact]
    public async Task A_store_saying_no_update_hides_the_banner_although_the_server_names_a_newer_build()
    {
        var decision = await DecideAsync(Info(latest: 20), new FakeUpdater(new StoreUpdate(false, 0)));

        Assert.Equal(AppUpdateDecision.UpToDate, decision);
    }

    [Fact]
    public async Task A_store_that_cannot_tell_leaves_the_servers_rule_to_decide()
    {
        var updater = new FakeUpdater((StoreUpdate?)null);

        Assert.Equal(AppUpdateDecision.UpdateAvailable, await DecideAsync(Info(latest: 12), updater));
        Assert.Equal(AppUpdateDecision.UpToDate, await DecideAsync(Info(latest: Installed), updater));
        Assert.Equal(AppUpdateDecision.UpToDate, await DecideAsync(info: null, updater));
    }

    [Fact]
    public async Task A_platform_with_no_store_flow_uses_the_servers_rule()
    {
        Assert.Equal(AppUpdateDecision.UpdateAvailable, await DecideAsync(Info(latest: 12), new NoStoreUpdater()));
        Assert.Equal(AppUpdateDecision.UpToDate, await DecideAsync(Info(latest: Installed), new NoStoreUpdater()));
    }

    [Fact]
    public async Task Without_an_updater_the_servers_rule_decides() =>
        Assert.Equal(AppUpdateDecision.UpdateAvailable, await DecideAsync(Info(latest: 12), updater: null));

    [Fact]
    public async Task A_store_build_that_is_not_newer_than_the_installed_one_is_no_update()
    {
        // A stored answer from before the rider updated: the store said 14, and 14 is what is installed now.
        Assert.Equal(AppUpdateDecision.UpToDate, AppVersionRules.Decide(14, null, new StoreUpdate(true, 14), null));
        Assert.Equal(AppUpdateDecision.UpToDate, await DecideAsync(null, new FakeUpdater(new StoreUpdate(true, Installed))));
    }

    // ── Server 404 (no numbers) and the minimum ──────────────────────────────

    [Fact]
    public async Task A_server_with_no_numbers_blocks_nobody_and_the_store_banner_still_decides()
    {
        // The server has no info at all, which is "no minimum".
        Assert.Equal(AppUpdateDecision.UpdateAvailable, await DecideAsync(info: null, new FakeUpdater(new StoreUpdate(true, 14))));
        Assert.Equal(AppUpdateDecision.UpToDate, await DecideAsync(info: null, new FakeUpdater(new StoreUpdate(false, 0))));
    }

    [Fact]
    public async Task The_servers_minimum_still_blocks_whatever_the_store_says()
    {
        var info = Info(latest: 12, minimum: 9);

        Assert.Equal(AppUpdateDecision.UpdateRequired, await DecideAsync(info, new FakeUpdater(new StoreUpdate(false, 0))));
        Assert.Equal(AppUpdateDecision.UpdateRequired, await DecideAsync(info, new FakeUpdater(new StoreUpdate(true, 14)), dismissed: 14));
    }

    // ── Dismissal per store version code ─────────────────────────────────────

    [Fact]
    public async Task Dismissing_hides_the_banner_until_a_higher_store_version_code()
    {
        var store14 = new FakeUpdater(new StoreUpdate(true, 14));
        var store15 = new FakeUpdater(new StoreUpdate(true, 15));

        Assert.Equal(AppUpdateDecision.UpdateAvailableDismissed, await DecideAsync(null, store14, dismissed: 14));
        Assert.Equal(AppUpdateDecision.UpdateAvailable, await DecideAsync(null, store15, dismissed: 14));
    }

    [Fact]
    public void The_dismissal_is_stored_against_the_number_the_banner_announced()
    {
        // The store decided: its code, not the server's latest build.
        Assert.Equal(14, AppVersionRules.AnnouncedBuild(Installed, Info(latest: 20), new StoreUpdate(true, 14)));
        // The store could not tell: the server's.
        Assert.Equal(20, AppVersionRules.AnnouncedBuild(Installed, Info(latest: 20), store: null));
        // Nothing to offer.
        Assert.Equal(0, AppVersionRules.AnnouncedBuild(Installed, Info(latest: 20), new StoreUpdate(false, 0)));
        Assert.Equal(0, AppVersionRules.AnnouncedBuild(Installed, info: null, store: null));
    }

    // ── Asking the store never blocks or throws ─────────────────────────────

    [Fact]
    public async Task An_updater_that_throws_is_treated_as_unable_to_tell()
    {
        var updater = new FakeUpdater(() => throw new InvalidOperationException("The store is down."));

        Assert.Null(await AppVersionRules.AskStoreAsync(updater, TimeSpan.FromSeconds(5)));
        Assert.Equal(AppUpdateDecision.UpdateAvailable, await DecideAsync(Info(latest: 12), updater));
    }

    [Fact]
    public async Task An_updater_that_does_not_answer_in_time_is_treated_as_unable_to_tell()
    {
        var never = new TaskCompletionSource<StoreUpdate?>();
        var updater = new FakeUpdater(() => never.Task);

        Assert.Null(await AppVersionRules.AskStoreAsync(updater, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task A_timeout_is_logged_with_its_reason()
    {
        var never = new TaskCompletionSource<StoreUpdate?>();
        var logger = new CapturingLogger<StoreUpdateRulesTests>();

        await AppVersionRules.AskStoreAsync(new FakeUpdater(() => never.Task), TimeSpan.FromMilliseconds(50), logger);

        Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("did not answer"));
    }

    [Fact]
    public async Task An_available_answer_with_no_build_number_is_unusable()
    {
        // Without a number a dismissal could not be remembered, so it is not trusted.
        var updater = new FakeUpdater(new StoreUpdate(true, 0));

        Assert.Null(await AppVersionRules.AskStoreAsync(updater, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task The_default_interface_method_cannot_tell()
    {
        Assert.Null(await AppVersionRules.AskStoreAsync(new NoStoreUpdater(), TimeSpan.FromSeconds(5)));
        Assert.Null(await AppVersionRules.AskStoreAsync(null, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_cancelled_wait_is_not_swallowed()
    {
        var never = new TaskCompletionSource<StoreUpdate?>();
        using var cancel = new CancellationTokenSource();
        var wait = AppVersionRules.AskStoreAsync(new FakeUpdater(() => never.Task), TimeSpan.FromSeconds(30), ct: cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}
