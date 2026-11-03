// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.Feedback.Tests;

/// <summary>
/// The rules for feedback the server takes in: the caps, the reply address, the category list, the receipt time, the status, the list
/// and the page, and a save that fails. Each is tested against the in-memory store.
/// </summary>
public class FeedbackManagerTests
{
    private static readonly DateTimeOffset Received = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static (FeedbackManager Manager, FakeFeedbackStore Store) Manager(FeedbackOptions? options = null)
    {
        var store = new FakeFeedbackStore();
        var manager = new FeedbackManager(store, NullLogger<FeedbackManager>.Instance, options, new FixedClock(Received));
        return (manager, store);
    }

    private static FeedbackRequest Valid(string message = "The map is empty.") => new() { Message = message, AppVersion = "0.5.0", Platform = "Android" };

    // ── The survey case, ported ────────────────────────────────────────────────

    [Fact]
    public async Task A_message_in_a_category_is_stored_under_it_without_an_email_and_the_list_filter_finds_it()
    {
        var (manager, _) = Manager();

        var saved = await manager.SubmitAsync(new FeedbackRequest
        {
            Message = "Good (4/5): quick and clear.",
            Subject = "home-survey",
            Category = "Survey",
            AppVersion = "0.5.0",
            Platform = "Android",
        });

        Assert.Equal("Survey", saved.Category);
        Assert.Null(saved.Email);
        Assert.Equal("home-survey", saved.Subject);

        var listed = await manager.ListAsync(1, 25, "Survey", status: null);
        var row = Assert.Single(listed.Data);
        Assert.Equal(saved.Id, row.Id);
    }

    // ── Storing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_message_is_trimmed_and_blank_optional_text_is_stored_as_absent()
    {
        var (manager, store) = Manager();

        var saved = await manager.SubmitAsync(new FeedbackRequest
        {
            Message = "  The stop name is wrong.  ",
            Subject = "   ",
            Email = "rider@example.test",
            Category = "  Bug  ",
            ContextName = "  Stop A  ",
        });

        Assert.Equal("The stop name is wrong.", saved.Message);
        Assert.Null(saved.Subject);
        Assert.Equal("rider@example.test", saved.Email);
        Assert.Equal("Bug", saved.Category);
        Assert.Equal("Stop A", saved.ContextName);
        Assert.Equal("The stop name is wrong.", store.Rows.Single().Message);
    }

    [Fact]
    public async Task The_app_version_and_platform_are_stored_as_the_app_sent_them()
    {
        var (manager, _) = Manager();

        var saved = await manager.SubmitAsync(new FeedbackRequest { Message = "Hi", AppVersion = " 0.5.0 ", Platform = "Android" });

        Assert.Equal(" 0.5.0 ", saved.AppVersion);
        Assert.Equal("Android", saved.Platform);
    }

    [Fact]
    public async Task A_new_message_is_New_and_its_receipt_time_is_the_clock_s_utc_instant()
    {
        var (manager, _) = Manager();

        var saved = await manager.SubmitAsync(Valid());

        Assert.Equal("New", saved.Status);
        Assert.Equal(Received.UtcDateTime, saved.ReceivedAt);
        Assert.Equal(DateTimeKind.Utc, saved.ReceivedAt.Kind);
    }

    [Fact]
    public async Task The_context_is_kept_as_attribution()
    {
        var (manager, _) = Manager();

        var saved = await manager.SubmitAsync(new FeedbackRequest { Message = "Missing departure", ContextId = 4411, ContextName = "Stop A" });

        Assert.Equal(4411, saved.ContextId);
    }

    [Fact]
    public async Task Each_saved_message_gets_its_own_id()
    {
        var (manager, _) = Manager();

        var first = await manager.SubmitAsync(Valid("one"));
        var second = await manager.SubmitAsync(Valid("two"));

        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id);
    }

    [Fact]
    public async Task A_null_request_is_refused() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Manager().Manager.SubmitAsync(null!));

    // ── The message ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public async Task A_blank_message_is_refused_and_nothing_is_saved(string message)
    {
        var (manager, store) = Manager();

        var ex = await Assert.ThrowsAsync<AppException>(() => manager.SubmitAsync(new FeedbackRequest { Message = message }));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("FEEDBACK_INVALID", ex.Code);
        Assert.Equal("message is required.", ex.Detail);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task A_message_of_exactly_the_cap_is_stored()
    {
        var (manager, store) = Manager();

        await manager.SubmitAsync(new FeedbackRequest { Message = new string('m', FeedbackLimits.MessageMaxLength) });

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task A_message_one_character_over_the_cap_is_refused_and_not_cut_down()
    {
        var (manager, store) = Manager();

        var ex = await Assert.ThrowsAsync<AppException>(() => manager.SubmitAsync(new FeedbackRequest { Message = new string('m', FeedbackLimits.MessageMaxLength + 1) }));

        Assert.Equal("FEEDBACK_INVALID", ex.Code);
        Assert.Equal("message must be 4000 characters or fewer.", ex.Detail);
        Assert.Equal(0, store.Count);
    }

    // ── The other caps ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("subject", 200)]
    [InlineData("appVersion", 64)]
    [InlineData("platform", 64)]
    [InlineData("category", 64)]
    [InlineData("contextName", 200)]
    public async Task A_field_over_its_cap_is_refused_and_named(string field, int max)
    {
        var (manager, store) = Manager();
        var over = new string('x', max + 1);
        var request = field switch
        {
            "subject" => new FeedbackRequest { Message = "Hi", Subject = over },
            "appVersion" => new FeedbackRequest { Message = "Hi", AppVersion = over },
            "platform" => new FeedbackRequest { Message = "Hi", Platform = over },
            "category" => new FeedbackRequest { Message = "Hi", Category = over },
            _ => new FeedbackRequest { Message = "Hi", ContextName = over },
        };

        var ex = await Assert.ThrowsAsync<AppException>(() => manager.SubmitAsync(request));

        Assert.Equal("FEEDBACK_INVALID", ex.Code);
        Assert.Equal($"{field} must be {max} characters or fewer.", ex.Detail);
        Assert.Equal(0, store.Count);
    }

    [Theory]
    [InlineData("subject", 200)]
    [InlineData("appVersion", 64)]
    [InlineData("platform", 64)]
    [InlineData("category", 64)]
    [InlineData("contextName", 200)]
    public async Task A_field_of_exactly_its_cap_is_stored(string field, int max)
    {
        var (manager, store) = Manager();
        var at = new string('x', max);
        var request = field switch
        {
            "subject" => new FeedbackRequest { Message = "Hi", Subject = at },
            "appVersion" => new FeedbackRequest { Message = "Hi", AppVersion = at },
            "platform" => new FeedbackRequest { Message = "Hi", Platform = at },
            "category" => new FeedbackRequest { Message = "Hi", Category = at },
            _ => new FeedbackRequest { Message = "Hi", ContextName = at },
        };

        await manager.SubmitAsync(request);

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task A_reply_address_over_its_cap_is_refused()
    {
        var (manager, store) = Manager();
        var email = new string('a', 250) + "@x.test";

        var ex = await Assert.ThrowsAsync<AppException>(() => manager.SubmitAsync(new FeedbackRequest { Message = "Hi", Email = email }));

        Assert.Equal("email must be 256 characters or fewer.", ex.Detail);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task A_reply_address_of_exactly_the_cap_is_accepted()
    {
        var (manager, store) = Manager();
        var email = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 63);

        await manager.SubmitAsync(new FeedbackRequest { Message = "Hi", Email = email });

        Assert.Equal(FeedbackLimits.EmailMaxLength, email.Length);
        Assert.Equal(1, store.Count);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("two@@example.test")]
    public async Task A_reply_address_that_is_not_an_address_is_refused(string email)
    {
        var (manager, store) = Manager();

        var ex = await Assert.ThrowsAsync<AppException>(() => manager.SubmitAsync(new FeedbackRequest { Message = "Hi", Email = email }));

        Assert.Equal("FEEDBACK_INVALID", ex.Code);
        Assert.Equal("email must be an e-mail address.", ex.Detail);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task No_reply_address_is_fine()
    {
        var (manager, store) = Manager();

        var saved = await manager.SubmitAsync(new FeedbackRequest { Message = "Hi", Email = null });

        Assert.Null(saved.Email);
        Assert.Equal(1, store.Count);
    }

    // ── Categories ───────────────────────────────────────────────────────────

    [Fact]
    public async Task With_no_category_list_any_category_up_to_the_cap_is_accepted()
    {
        var (manager, _) = Manager();

        var saved = await manager.SubmitAsync(new FeedbackRequest { Message = "Hi", Category = "Anything At All" });

        Assert.Equal("Anything At All", saved.Category);
    }

    [Fact]
    public async Task With_a_category_list_a_category_not_in_it_is_refused_and_its_code_is_the_option()
    {
        var (manager, store) = Manager(new FeedbackOptions { Categories = ["Bug", "Survey"], UnknownCategoryCode = "XX_CATEGORY" });

        var ex = await Assert.ThrowsAsync<AppException>(() => manager.SubmitAsync(new FeedbackRequest { Message = "Hi", Category = "Other" }));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("XX_CATEGORY", ex.Code);
        Assert.Equal("category must be one of: Bug, Survey.", ex.Detail);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task With_a_category_list_the_categories_match_exactly()
    {
        var (manager, _) = Manager(new FeedbackOptions { Categories = ["Bug"] });

        await Assert.ThrowsAsync<AppException>(() => manager.SubmitAsync(new FeedbackRequest { Message = "Hi", Category = "bug" }));
        var saved = await manager.SubmitAsync(new FeedbackRequest { Message = "Hi", Category = " Bug " });

        Assert.Equal("Bug", saved.Category);
    }

    [Fact]
    public async Task With_a_category_list_a_message_with_no_category_is_still_accepted()
    {
        var (manager, _) = Manager(new FeedbackOptions { Categories = ["Bug"] });

        var saved = await manager.SubmitAsync(new FeedbackRequest { Message = "Hi", Category = "   " });

        Assert.Null(saved.Category);
    }

    // ── Listing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_is_newest_first_and_a_blank_category_is_no_filter()
    {
        var (manager, _) = Manager();
        await manager.SubmitAsync(new FeedbackRequest { Message = "one", Category = "Bug" });
        await manager.SubmitAsync(new FeedbackRequest { Message = "two", Category = "Other" });

        var page = await manager.ListAsync(1, 50, "   ", status: null);

        string[] expectedOrder = ["two", "one"];
        Assert.Equal(expectedOrder, page.Data.Select(r => r.Message).ToArray());
        Assert.Equal(2, page.Total);
    }

    [Fact]
    public async Task The_list_filters_by_status()
    {
        var (manager, _) = Manager();
        var handled = await manager.SubmitAsync(Valid("handled one"));
        await manager.SubmitAsync(Valid("still new"));
        await manager.SetStatusAsync(handled.Id, FeedbackStatus.Handled);

        var page = await manager.ListAsync(1, 50, category: null, FeedbackStatus.Handled);

        var row = Assert.Single(page.Data);
        Assert.Equal("handled one", row.Message);
        Assert.Equal("Handled", row.Status);
    }

    [Fact]
    public async Task A_page_past_the_first_returns_the_rest_with_the_whole_total()
    {
        var (manager, _) = Manager();
        for (var i = 1; i <= 3; i++)
        {
            await manager.SubmitAsync(Valid($"message {i}"));
        }

        var second = await manager.ListAsync(2, 2, category: null, status: null);

        var row = Assert.Single(second.Data);
        Assert.Equal("message 1", row.Message);
        Assert.Equal(3, second.Total);
        Assert.Equal(2, second.TotalPages);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    public async Task A_page_or_page_size_below_one_is_refused(int page, int perPage)
    {
        var (manager, _) = Manager();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => manager.ListAsync(page, perPage, null, null));
    }

    // ── Status ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_status_is_set_and_an_unknown_message_reports_false()
    {
        var (manager, store) = Manager();
        var saved = await manager.SubmitAsync(Valid());

        Assert.True(await manager.SetStatusAsync(saved.Id, FeedbackStatus.InReview));
        Assert.False(await manager.SetStatusAsync(999, FeedbackStatus.Dismissed));
        Assert.Equal("InReview", store.Rows.Single().Status);
    }

    [Fact]
    public async Task A_status_that_is_not_one_of_the_four_is_refused()
    {
        var (manager, store) = Manager();
        var saved = await manager.SubmitAsync(Valid());

        var ex = await Assert.ThrowsAsync<AppException>(() => manager.SetStatusAsync(saved.Id, (FeedbackStatus)9));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("FEEDBACK_INVALID", ex.Code);
        Assert.Equal("New", store.Rows.Single().Status);
    }

    [Fact]
    public void The_four_statuses_keep_their_names_and_order()
    {
        string[] statusNames = ["New", "InReview", "Handled", "Dismissed"];
        Assert.Equal(statusNames, Enum.GetNames<FeedbackStatus>());
        Assert.Equal(0, (int)FeedbackStatus.New);
        Assert.Equal(3, (int)FeedbackStatus.Dismissed);
    }

    // ── A save that fails ────────────────────────────────────────────────────

    [Fact]
    public async Task A_save_that_fails_is_reported_and_never_shown_as_saved()
    {
        var (manager, store) = Manager();
        store.FailSaves = new InvalidOperationException("The disk is full.");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SubmitAsync(Valid()));

        Assert.Equal("The disk is full.", ex.Message);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task A_saved_message_is_logged_with_its_id_and_category()
    {
        var logger = new CapturingLogger<FeedbackManager>();
        var manager = new FeedbackManager(new FakeFeedbackStore(), logger);

        await manager.SubmitAsync(new FeedbackRequest { Message = "Hi", Category = "Bug" });

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Stored feedback 1 (category Bug)", entry.Message);
    }

    [Fact]
    public void The_options_must_be_valid()
    {
        Assert.Throws<ArgumentException>(() => new FeedbackManager(new FakeFeedbackStore(), NullLogger<FeedbackManager>.Instance, new FeedbackOptions { Route = " " }));
    }
}
