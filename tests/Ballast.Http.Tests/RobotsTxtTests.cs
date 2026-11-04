// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http.Tests;

/// <summary>robots.txt as RFC 9309 reads it for ExampleBot: the group, the longest match, the wildcards, and the answers for a file that cannot be read.</summary>
public class RobotsTxtTests
{
    /// <summary>The crawler these tests read robots.txt for.</summary>
    private const string Token = "ExampleBot";

    private static RobotsTxt Parse(string text) => RobotsTxt.Parse(text, Token);

    private static RobotsTxt FromStatus(int status, string body) => RobotsTxt.FromStatus(status, body, Token);

    [Fact]
    public void A_group_naming_this_crawler_is_used_over_the_star_group()
    {
        var robots = Parse("""
            User-agent: *
            Disallow: /

            User-agent: ExampleBot
            Disallow: /private
            """);

        Assert.True(robots.IsAllowed("/timetable"));
        Assert.False(robots.IsAllowed("/private/x"));
    }

    [Fact]
    public void Without_a_group_for_this_crawler_the_star_group_is_used()
    {
        var robots = Parse("User-agent: *\nDisallow: /admin\n");

        Assert.False(robots.IsAllowed("/admin"));
        Assert.True(robots.IsAllowed("/home"));
    }

    [Fact]
    public void A_file_with_no_group_for_this_crawler_or_star_allows_everything()
    {
        var robots = Parse("User-agent: otherbot\nDisallow: /\n");

        Assert.True(robots.IsAllowed("/anything"));
        Assert.Null(robots.Reason);
    }

    [Fact]
    public void An_empty_file_allows_everything()
    {
        Assert.True(Parse(string.Empty).IsAllowed("/anything"));
    }

    [Fact]
    public void The_product_token_matches_case_insensitively_and_ignores_a_version()
    {
        var robots = Parse("User-agent: exAMPLEbot/2.1 (compatible)\nDisallow: /x\n");

        Assert.False(robots.IsAllowed("/x"));
    }

    [Fact]
    public void The_longest_rule_wins_and_an_allow_wins_a_tie()
    {
        var robots = Parse("""
            User-agent: *
            Disallow: /docs
            Allow: /docs/public
            Disallow: /docs/public/secret
            Allow: /tie
            Disallow: /tie
            """);

        Assert.False(robots.IsAllowed("/docs/private"));
        Assert.True(robots.IsAllowed("/docs/public/page"));
        Assert.False(robots.IsAllowed("/docs/public/secret/x"));
        Assert.True(robots.IsAllowed("/tie"));
    }

    [Fact]
    public void Star_matches_any_run_and_a_trailing_dollar_anchors_the_end()
    {
        var robots = Parse("""
            User-agent: *
            Disallow: /*.pdf$
            Disallow: /search*results
            """);

        Assert.False(robots.IsAllowed("/files/report.pdf"));
        Assert.True(robots.IsAllowed("/files/report.pdf?download=1"));
        Assert.False(robots.IsAllowed("/search/by/results"));
        Assert.True(robots.IsAllowed("/search/by"));
    }

    [Fact]
    public void An_empty_value_is_no_rule()
    {
        Assert.True(Parse("User-agent: *\nDisallow:\n").IsAllowed("/anything"));
    }

    [Fact]
    public void Crawl_delay_is_read_from_the_chosen_group_and_a_bad_value_is_ignored()
    {
        var chosen = Parse("User-agent: *\nCrawl-delay: 2\n\nUser-agent: ExampleBot\nCrawl-delay: 7.5\n");
        var bad = Parse("User-agent: *\nCrawl-delay: soon\n");
        var negative = Parse("User-agent: *\nCrawl-delay: -1\n");

        Assert.Equal(TimeSpan.FromSeconds(7.5), chosen.CrawlDelay);
        Assert.Null(bad.CrawlDelay);
        Assert.Null(negative.CrawlDelay);
    }

    [Fact]
    public void Consecutive_user_agent_lines_share_a_group_and_a_user_agent_after_rules_starts_a_new_one()
    {
        var robots = Parse("""
            User-agent: otherbot
            User-agent: ExampleBot
            Disallow: /shared

            User-agent: *
            Disallow: /
            """);

        Assert.False(robots.IsAllowed("/shared/x"));
        Assert.True(robots.IsAllowed("/other"));
    }

    [Fact]
    public void Comments_and_a_byte_order_mark_are_ignored()
    {
        var robots = Parse("﻿# the file\nUser-agent: * # everyone\nDisallow: /private # keep out\n");

        Assert.False(robots.IsAllowed("/private/x"));
        Assert.True(robots.IsAllowed("/public"));
    }

    [Fact]
    public void A_2xx_answer_is_parsed()
    {
        var robots = FromStatus(200, "User-agent: *\nDisallow: /x\n");

        Assert.False(robots.IsAllowed("/x"));
        Assert.Null(robots.Reason);
    }

    [Fact]
    public void A_4xx_answer_allows_everything_and_says_so()
    {
        var robots = FromStatus(404, "ignored");

        Assert.True(robots.IsAllowed("/anything"));
        Assert.Contains("404", robots.Reason);
        Assert.Contains("allowed", robots.Reason);
    }

    [Fact]
    public void A_5xx_answer_disallows_everything_and_says_so()
    {
        var robots = FromStatus(503, string.Empty);

        Assert.False(robots.IsAllowed("/"));
        Assert.Contains("503", robots.Reason);
    }

    [Fact]
    public void A_null_path_is_an_argument_error()
    {
        Assert.Throws<ArgumentNullException>(() => RobotsTxt.AllowAll.IsAllowed(null!));
    }

    [Fact]
    public void DisallowAll_keeps_its_reason()
    {
        var robots = RobotsTxt.DisallowAll("could not be read");

        Assert.False(robots.IsAllowed("/"));
        Assert.Equal("could not be read", robots.Reason);
    }
}
