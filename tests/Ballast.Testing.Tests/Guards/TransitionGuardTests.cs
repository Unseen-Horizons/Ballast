// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Testing.Guards;
using Ballast.Testing.Guards.Transitions;

namespace Ballast.Testing.Tests.Guards;

/// <summary>The transition rule: the status-write scanner, the allow-list with its counts, and the source reader.</summary>
public class TransitionGuardTests
{
    private const string Reason = "Reviewed: the conditional claim names the status it must have, so a lost race changes nothing.";
    private const string Path = "Svc/Writer.cs";

    private static TransitionOptions Options(params string[] enums) => new()
    {
        Trees = ["Svc"],
        StateEnums = enums.Length == 0 ? ["TicketStatus", "JourneyStatus"] : enums,
    };

    private static IReadOnlyList<string> Check(string code, TransitionOptions? options = null, params TransitionAllowEntry[] allow) =>
        TransitionGuard.Problems([(Path, code)], options ?? Options(), allow);

    [Fact]
    public void A_tracked_write_of_a_state_enum_is_found_with_its_line()
    {
        var write = Assert.Single(TransitionGuard.Find("var a = 1;\n    ticket.Status = TicketStatus.Used;", Options(), Path));

        Assert.Equal(new StatusWrite(Path, 2, "ticket.Status = TicketStatus.Used;"), write);
    }

    [Fact]
    public void A_set_based_write_of_a_state_column_is_found()
    {
        var write = Assert.Single(TransitionGuard.Find(".SetProperty(j => j.UndoneAtUtc, (DateTime?)now)", Options()));

        Assert.Equal(".SetProperty(j => j.UndoneAtUtc, (DateTime?)now)", write.Text);
    }

    [Theory]
    [InlineData("if (ticket.Status == TicketStatus.Used) return;")]
    [InlineData("// ticket.Status = TicketStatus.Used;")]
    [InlineData("* ticket.Status = TicketStatus.Used;")]
    [InlineData("feedback.Status = FeedbackStatus.Closed;")]
    [InlineData("var s = ticket.Status;")]
    [InlineData("Status = TicketStatus.Active")]
    public void Reads_initialisers_comments_and_other_enums_are_not_writes(string line)
    {
        Assert.Empty(TransitionGuard.Find(line, Options()));
    }

    [Fact]
    public void A_write_with_no_entry_is_reported_with_its_file_and_line()
    {
        var problem = Assert.Single(Check("class A {}\nticket.Status = TicketStatus.Used;"));

        Assert.StartsWith("TRANSITION-ALLOW-LIST: Svc/Writer.cs:2: ticket.Status = TicketStatus.Used; is not excused", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_write_that_occurs_once_is_excused()
    {
        var entry = new TransitionAllowEntry(Path, "ticket.Status = TicketStatus.Used;", 1, Reason);

        Assert.Empty(Check("ticket.Status = TicketStatus.Used;", null, entry));
    }

    [Fact]
    public void A_listed_write_that_occurs_more_often_than_its_count_is_reported()
    {
        var entry = new TransitionAllowEntry(Path, "ticket.Status = TicketStatus.Used;", 1, Reason);

        var problem = Assert.Single(Check("ticket.Status = TicketStatus.Used;\nticket.Status = TicketStatus.Used;", null, entry));

        Assert.Equal("TRANSITION-ALLOW-LIST: Svc/Writer.cs: 2 writes of `ticket.Status = TicketStatus.Used;`, the allow-list covers 1. Each write needs its own review.", problem);
    }

    [Fact]
    public void A_count_higher_than_the_code_holds_is_reported()
    {
        var entry = new TransitionAllowEntry(Path, "ticket.Status = TicketStatus.Used;", 2, Reason);

        var problem = Assert.Single(Check("ticket.Status = TicketStatus.Used;", null, entry));

        Assert.Equal(
            "TRANSITION-ALLOW-LIST: Svc/Writer.cs: `ticket.Status = TicketStatus.Used;` is listed 2 time(s) but the code has 1. Update or remove the entry.",
            problem);
    }

    [Fact]
    public void A_listed_write_that_no_longer_occurs_is_stale()
    {
        var entry = new TransitionAllowEntry(Path, "ticket.Status = TicketStatus.Used;", 1, Reason);

        var problem = Assert.Single(Check("var a = 1;", null, entry));

        Assert.Contains("stale, nothing it excuses is found", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_write_with_a_short_reason_excuses_nothing()
    {
        var entry = new TransitionAllowEntry(Path, "ticket.Status = TicketStatus.Used;", 1, "short");

        var problems = Check("ticket.Status = TicketStatus.Used;", null, entry);

        Assert.Equal(2, problems.Count);
        Assert.Contains("the reason must be at least 20 characters", problems[0], StringComparison.Ordinal);
        Assert.Contains("is not excused by a reviewed entry", problems[1], StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_with_no_file_names_no_key_and_excuses_nothing()
    {
        var entry = new TransitionAllowEntry(string.Empty, "ticket.Status = TicketStatus.Used;", 1, Reason);

        var problems = Check("ticket.Status = TicketStatus.Used;", null, entry);

        Assert.Contains(problems, problem => problem.Contains("no key, so it excuses nothing", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("is not excused by a reviewed entry", StringComparison.Ordinal));
    }

    [Fact]
    public void An_entry_that_covers_no_write_at_all_is_reported()
    {
        var entry = new TransitionAllowEntry(Path, "ticket.Status = TicketStatus.Used;", 0, Reason);

        var problem = Assert.Single(Check("ticket.Status = TicketStatus.Used;", null, entry));

        Assert.Equal(
            "TRANSITION-ALLOW-LIST: Svc/Writer.cs: `ticket.Status = TicketStatus.Used;` must be covered at least once. Remove the entry or correct its count.",
            problem);
    }

    [Fact]
    public void A_listed_write_listed_twice_is_reported_and_its_count_is_checked_once()
    {
        var entry = new TransitionAllowEntry(Path, "ticket.Status = TicketStatus.Used;", 1, Reason);

        var problem = Assert.Single(Check("ticket.Status = TicketStatus.Used;", null, entry, entry));

        Assert.Equal("TRANSITION-ALLOW-LIST Svc/Writer.cs: ticket.Status = TicketStatus.Used;: listed more than once. Keep one entry.", problem);
    }

    [Fact]
    public void The_state_enums_are_an_option()
    {
        Assert.Empty(TransitionGuard.Find("ticket.Status = TicketStatus.Used;", Options("JourneyStatus")));
        Assert.Single(TransitionGuard.Find("journey.Status = JourneyStatus.Cancelled;", Options("JourneyStatus")));
    }

    [Fact]
    public void No_state_enum_is_refused_rather_than_checking_nothing()
    {
        var options = new TransitionOptions { Trees = ["Svc"], StateEnums = [] };

        Assert.Throws<ArgumentException>(() => TransitionGuard.Find("x", options));
    }

    [Fact]
    public void The_planted_write_is_caught_and_a_comment_passes()
    {
        GuardSelfTest.AssertCatches(source => Check(source), "ticket.Status = TicketStatus.Used;", $"{Path}:1:");
        GuardSelfTest.AssertPasses(source => Check(source), "// ticket.Status = TicketStatus.Used;\nvar state = ticket.Status;");
    }

    [Fact]
    public void A_scan_naming_an_enum_that_never_appears_fails_its_own_self_test()
    {
        var blind = Options("NoSuchEnum");

        Assert.ThrowsAny<Exception>(() => GuardSelfTest.AssertCatches(source => Check(source, blind), "ticket.Status = TicketStatus.Used;", $"{Path}:1:"));
    }

    [Fact]
    public void Sources_read_the_cs_files_of_the_trees_and_skip_migrations()
    {
        using var repo = new TempRepository();
        repo.Write("Svc/Writer.cs", "class W {}");
        repo.Write("Svc/Migrations/Old.cs", "class M {}");

        var source = Assert.Single(TransitionGuard.Sources(repo.Root, Options()));

        Assert.Equal("Svc/Writer.cs", source.RelativePath);
    }
}
