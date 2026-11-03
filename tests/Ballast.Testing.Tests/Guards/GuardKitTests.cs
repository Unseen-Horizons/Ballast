// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Testing.Guards;

namespace Ballast.Testing.Tests.Guards;

/// <summary>The allow-list rules, the repo root finder, the clock scanner and the planted-violation helper.</summary>
public class GuardKitTests
{
    private const string Reason = "Reviewed: the value is a fixed first-party address, checked on 2026-11-03.";

    [Fact]
    public void An_entry_with_a_reason_excuses_its_finding_and_nothing_is_reported()
    {
        var problems = AllowList.Problems("LIST", [new AllowEntry("a.cs:Rule", Reason)], ["a.cs:Rule"]);

        Assert.Empty(problems);
    }

    [Fact]
    public void A_finding_with_no_entry_is_reported_as_not_excused()
    {
        var problems = AllowList.Problems("LIST", [], ["a.cs:Rule"]);

        var problem = Assert.Single(problems);
        Assert.Equal("LIST: a.cs:Rule is not excused by a reviewed entry with a reason of at least 20 characters.", problem);
    }

    [Fact]
    public void An_entry_whose_reason_is_too_short_excuses_nothing_so_its_finding_fails_too()
    {
        var problems = AllowList.Problems("LIST", [new AllowEntry("a.cs:Rule", "too short")], ["a.cs:Rule"]);

        Assert.Equal(2, problems.Count);
        Assert.Equal("LIST a.cs:Rule: the reason must be at least 20 characters.", problems[0]);
        Assert.Contains("LIST: a.cs:Rule is not excused", problems[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_reason_is_not_a_reason()
    {
        var problems = AllowList.Problems("LIST", [new AllowEntry("a.cs:Rule", new string(' ', 40))], ["a.cs:Rule"]);

        Assert.Contains(problems, p => p.Contains("the reason must be at least", StringComparison.Ordinal));
    }

    [Fact]
    public void An_entry_that_matches_no_finding_is_stale_and_fails()
    {
        var problems = AllowList.Problems("LIST", [new AllowEntry("gone.cs:Rule", Reason)], []);

        var problem = Assert.Single(problems);
        Assert.Equal("LIST gone.cs:Rule: stale, nothing it excuses is found. Remove the entry.", problem);
    }

    [Fact]
    public void An_entry_listed_twice_is_reported_once_per_extra_listing()
    {
        var problems = AllowList.Problems("LIST", [new AllowEntry("a.cs:Rule", Reason), new AllowEntry("a.cs:Rule", Reason)], ["a.cs:Rule"]);

        Assert.Equal("LIST a.cs:Rule: listed more than once. Keep one entry.", Assert.Single(problems));
    }

    [Fact]
    public void An_entry_without_a_key_is_reported()
    {
        var problems = AllowList.Problems("LIST", [new AllowEntry(" ", Reason)], []);

        Assert.Equal("LIST: an entry has no key, so it excuses nothing. Remove it or name the finding.", Assert.Single(problems));
    }

    [Fact]
    public void The_minimum_reason_length_is_a_parameter()
    {
        var problems = AllowList.Problems("LIST", [new AllowEntry("a", "short reason")], ["a"], minimumReasonLength: 5);

        Assert.Empty(problems);
    }

    [Fact]
    public void A_minimum_below_one_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AllowList.Problems("LIST", [], [], minimumReasonLength: 0));
    }

    [Fact]
    public void The_repo_root_is_the_folder_that_holds_the_marker()
    {
        var root = Path.Combine(Path.GetTempPath(), "ballast-root-" + Guid.NewGuid().ToString("N"));
        var deep = Path.Combine(root, "src", "inside");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(root, "Marker.slnx"), "<Solution />");
        try
        {
            Assert.Equal(root, RepoRoot.Find("Marker.slnx", deep));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_marker_that_no_folder_holds_is_reported()
    {
        var start = Path.Combine(Path.GetTempPath(), "ballast-nomarker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(start);
        try
        {
            var error = Assert.Throws<DirectoryNotFoundException>(() => RepoRoot.Find("Never-Present-" + Guid.NewGuid().ToString("N") + ".slnx", start));
            Assert.Contains("Could not locate the repository root", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(start, recursive: true);
        }
    }

    [Fact]
    public void The_clock_guard_reports_a_local_clock_read_and_names_its_line()
    {
        var findings = ClockGuard.Findings("Svc/Clock.cs", "class C\n{\n    var now = DateTime.Now;\n}");

        Assert.Equal(["Svc/Clock.cs:3: var now = DateTime.Now;"], findings);
    }

    [Fact]
    public void The_clock_guard_ignores_the_same_name_in_a_comment()
    {
        var findings = ClockGuard.Findings("Svc/Clock.cs", "// DateTime.Now is banned here\n/* DateTimeOffset.Today */\nvar u = DateTime.UtcNow;");

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("var a = DateTimeOffset.Today;")]
    [InlineData("var b = x.ToLocalTime();")]
    [InlineData("var c = TimeZoneInfo.Local;")]
    public void The_clock_guard_covers_every_local_zone_read(string line)
    {
        GuardSelfTest.AssertCatches(source => ClockGuard.Findings("p.cs", source), line, "p.cs:1");
    }

    [Fact]
    public void The_planted_violation_helper_proves_the_clock_guard_can_see_a_violation()
    {
        GuardSelfTest.AssertCatches(source => ClockGuard.Findings("planted.cs", source), "var t = DateTime.Today;", "DateTime.Today");
    }

    [Fact]
    public void The_planted_violation_helper_passes_a_clean_source()
    {
        GuardSelfTest.AssertPasses(source => ClockGuard.Findings("clean.cs", source), "var t = DateTime.UtcNow;");
    }

    [Fact]
    public void The_planted_violation_helper_fails_when_a_scan_is_blind()
    {
        var error = Assert.ThrowsAny<Exception>(() => GuardSelfTest.AssertCatches(_ => [], "var t = DateTime.Now;", "DateTime.Now"));

        Assert.Contains("did not catch the planted violation", error.Message, StringComparison.Ordinal);
    }
}
