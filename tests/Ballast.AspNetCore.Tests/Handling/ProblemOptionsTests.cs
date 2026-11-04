// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.AspNetCore;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.AspNetCore.Tests.Handling;

/// <summary>The checks that refuse an invalid mapping or titles setting when the options are added, and the English titles.</summary>
public class ProblemOptionsTests
{
    [Fact]
    public void A_mapping_below_400_is_refused_when_it_is_built()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExceptionMapping(_ => true, 200, "CODE"));
    }

    [Fact]
    public void A_mapping_above_599_is_refused_when_it_is_built()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExceptionMapping(_ => true, 600, "CODE"));
    }

    [Fact]
    public void A_mapping_with_a_blank_code_is_refused_when_it_is_built()
    {
        Assert.Throws<ArgumentException>(() => new ExceptionMapping(_ => true, 503, "   "));
    }

    [Fact]
    public void A_mapping_without_a_predicate_is_refused_when_it_is_built()
    {
        Assert.Throws<ArgumentNullException>(() => new ExceptionMapping(null!, 503, "CODE"));
    }

    [Fact]
    public void A_blank_title_is_refused_when_the_options_are_added_and_the_message_names_the_mapping()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddProblemResponses(options =>
            options.ExceptionMappings.Add(new ExceptionMapping(_ => true, 503, "SERVICE_BUSY") { Title = " " })));

        Assert.Contains("SERVICE_BUSY", error.Message, StringComparison.Ordinal);
        Assert.Contains("Title", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_retry_after_below_one_second_is_refused_when_the_options_are_added()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddProblemResponses(options =>
            options.ExceptionMappings.Add(new ExceptionMapping(_ => true, 503, "SERVICE_BUSY") { Title = "Busy.", RetryAfterSeconds = 0 })));

        Assert.Contains("RetryAfterSeconds", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_entry_in_the_mapping_list_is_refused_when_the_options_are_added()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddProblemResponses(options =>
            options.ExceptionMappings.Add(null!)));
    }

    [Fact]
    public void Using_the_problem_responses_without_adding_them_is_refused()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        Assert.Throws<InvalidOperationException>(() => app.UseProblemResponses());
    }

    [Fact]
    public void A_registered_titles_service_is_kept_when_the_problem_responses_are_added()
    {
        var titles = new FixedTitles();
        var services = new ServiceCollection();
        services.AddSingleton<IProblemTitles>(titles);

        services.AddProblemResponses();
        using var provider = services.BuildServiceProvider();

        Assert.Same(titles, provider.GetRequiredService<IProblemTitles>());
    }

    [Fact]
    public void The_english_titles_are_the_reason_phrases_and_the_validation_title()
    {
        var titles = new EnglishProblemTitles();

        Assert.Equal("Not Found", titles.Title(404, "NOT_FOUND"));
        Assert.Equal("Too Many Requests", titles.Title(429, "RATE_LIMITED"));
        Assert.Equal("Internal Server Error", titles.Title(500, "INTERNAL_ERROR"));
        Assert.Equal("One or more validation errors occurred.", titles.Title(400, "VALIDATION_FAILED"));
        Assert.Equal("Bad Request", titles.Title(400, "SOMETHING_ELSE"));
    }

    [Fact]
    public void A_status_with_no_reason_phrase_is_titled_error_and_never_blank()
    {
        var title = new EnglishProblemTitles().Title(599, "CODE");

        Assert.False(string.IsNullOrWhiteSpace(title));
    }

    private sealed class FixedTitles : IProblemTitles
    {
        public string Title(int status, string code) => "Fixed.";
    }
}
