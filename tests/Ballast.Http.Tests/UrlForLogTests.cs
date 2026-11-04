// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Http.Tests;

/// <summary>
/// One redaction rule for every log, ported from the three copies in GetThere and TransitInfo (<c>ExternalFeedSource.RedactUrl</c>,
/// <c>AdminAudit.RedactUrl</c> and <c>ScrapeEngine.UrlForDisplay</c>). The rule keeps the first copy's <c>?[redacted]</c> marker and
/// the other two's refusal to echo text that is not a URL.
/// </summary>
public class UrlForLogTests
{
    [Fact]
    public void Keeps_scheme_host_port_and_path_and_marks_the_query_redacted()
    {
        Assert.Equal(
            "https://feeds.example.test:8443/a/gtfs.zip?[redacted]",
            UrlForLog.Redact("https://user:secret@feeds.example.test:8443/a/gtfs.zip?apikey=SECRET123"));
    }

    [Fact]
    public void Drops_user_information_and_the_fragment_and_never_shows_the_secret()
    {
        var redacted = UrlForLog.Redact("https://user:pw@feeds.example.com:8443/gtfs/feed.zip?apikey=secret#frag");

        Assert.Equal("https://feeds.example.com:8443/gtfs/feed.zip?[redacted]", redacted);
        Assert.DoesNotContain("pw", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("frag", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_url_without_a_query_is_shown_without_a_marker()
    {
        Assert.Equal("https://feeds.example.test/gtfs.zip", UrlForLog.Redact("https://feeds.example.test/gtfs.zip"));
    }

    [Fact]
    public void A_fragment_alone_is_dropped_with_no_marker()
    {
        Assert.Equal("https://feeds.example.test/a", UrlForLog.Redact("https://feeds.example.test/a#token=abc"));
    }

    [Fact]
    public void A_default_port_is_not_shown_and_another_port_is()
    {
        Assert.Equal("https://feeds.example.com/a", UrlForLog.Redact("https://feeds.example.com:443/a"));
        Assert.Equal("http://feeds.example.com:8080/a", UrlForLog.Redact("http://feeds.example.com:8080/a"));
    }

    [Fact]
    public void An_ipv6_literal_host_keeps_its_brackets()
    {
        Assert.Equal("http://[::1]:8080/x?[redacted]", UrlForLog.Redact("http://[::1]:8080/x?k=v"));
    }

    [Fact]
    public void Text_that_is_not_an_absolute_url_is_never_echoed()
    {
        var redacted = UrlForLog.Redact("feeds/relative?apikey=secret");

        Assert.Equal(UrlForLog.NotAnAbsoluteUrl, redacted);
        Assert.Equal("(not an absolute URL)", redacted);
        Assert.DoesNotContain("secret", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not a url?apiKey=SECRET")]
    [InlineData("//user:secret@feeds.example.test/x?k=SECRET")]
    [InlineData("file:///c:/windows/win.ini?k=SECRET")]
    [InlineData("")]
    [InlineData("   ")]
    public void Anything_without_an_absolute_url_with_a_host_is_the_placeholder(string text)
    {
        var redacted = UrlForLog.Redact(text);

        Assert.Equal(UrlForLog.NotAnAbsoluteUrl, redacted);
        Assert.DoesNotContain("SECRET", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_is_one_url_to_this_method_so_its_second_address_is_inside_the_first_query()
    {
        // An app that stores a list of addresses redacts each one on its own. This pins what happens when it does not.
        Assert.Equal("https://a.example/x?[redacted]", UrlForLog.Redact("https://a.example/x?key=1;https://b.example/y?token=2"));
    }

    [Fact]
    public void The_uri_overload_gives_the_same_answer()
    {
        var uri = new Uri("https://user:pw@feeds.example.test:8443/a?k=v#f");

        Assert.Equal(UrlForLog.Redact(uri.OriginalString), UrlForLog.Redact(uri));
        Assert.Equal("https://feeds.example.test:8443/a?[redacted]", UrlForLog.Redact(uri));
    }

    [Fact]
    public void A_null_url_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => UrlForLog.Redact((string)null!));
        Assert.Throws<ArgumentNullException>(() => UrlForLog.Redact((Uri)null!));
    }
}
