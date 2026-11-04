// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Testing.Guards;
using Ballast.Testing.Guards.AdminJs;

namespace Ballast.Testing.Tests.Guards;

/// <summary>The admin-page escaping rule: the template scanner, the allow-list around its hits, and the script reader.</summary>
public class AdminJsEscapeGuardTests
{
    private const string Reason = "Reviewed: the value is a fixed label from this page's own table, never text from data.";
    private const string PagePath = "admin/page.js";

    private static AdminJsOptions Options() => new() { Folders = ["admin"] };

    private static IReadOnlyList<string> Check(string code, AdminJsOptions? options = null, params AllowEntry[] allow) =>
        AdminJsEscapeGuard.Problems([(PagePath, code)], options ?? Options(), allow);

    [Theory]
    [InlineData("el.innerHTML = `<td>${x.name}</td>`;")]
    [InlineData("el.innerHTML = `<td>${row?.title}</td>`;")]
    [InlineData("el.innerHTML = `<td>${a[b].message}</td>`;")]
    [InlineData("el.innerHTML = `<td>${s.operatorName || ''}</td>`;")]
    [InlineData("el.innerHTML = `<a href=\"${f.url}\">x</a>`;")]
    [InlineData("el.innerHTML = rows.map(r => `<li>${r.label}</li>`).join('');")]
    [InlineData("el.innerHTML = `<ul>${rows.map(r => `<li>${r.description}</li>`).join('')}</ul>`;")]
    [InlineData("el.innerHTML = `\n<td>\n${x.lastError}\n</td>`;")]
    public void The_scanner_flags_an_unescaped_text_path(string code)
    {
        Assert.NotEmpty(AdminJsEscapeGuard.Scan(code));
    }

    [Theory]
    [InlineData("el.innerHTML = `<td>${Shell.esc(x.name)}</td>`;")]
    [InlineData("el.innerHTML = `<td>${esc(x.title)}</td>`;")]
    [InlineData("el.innerHTML = `<a href=\"?q=${encodeURIComponent(x.name)}\">x</a>`;")]
    [InlineData("el.textContent = `${x.name} (${x.title})`;")]
    [InlineData("el.innerHTML = `<td>${x.count}</td>`;")]
    [InlineData("el.innerHTML = `<td>${name}</td>`;")]
    [InlineData("// el.innerHTML = `<td>${x.name}</td>`;")]
    [InlineData("var re = /[\"'`]/g; el.innerHTML = `<td>${Shell.esc(x.name)}</td>`;")]
    public void The_scanner_accepts_an_escaped_or_a_non_text_value(string code)
    {
        Assert.Empty(AdminJsEscapeGuard.Scan(code));
    }

    [Fact]
    public void The_scanner_reports_the_line_and_the_normalised_expression()
    {
        var hit = Assert.Single(AdminJsEscapeGuard.Scan("var a = 1;\nvar b = `<b>${x.name  ||  ''}</b>`;"));

        Assert.Equal(2, hit.Line);
        Assert.Equal("x.name || ''", hit.Expression);
    }

    [Fact]
    public void A_text_value_with_no_entry_is_reported_with_its_file_and_line()
    {
        var problem = Assert.Single(Check("var a = 1;\nel.innerHTML = `<td>${x.name}</td>`;"));

        Assert.StartsWith("ADMIN-JS-ALLOW-LIST: admin/page.js:2: ${x.name} is text from data in an HTML template, not escaped.", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_on_the_same_file_and_expression_excuses_the_value()
    {
        Assert.Empty(Check("el.innerHTML = `<td>${x.name}</td>`;", null, new AllowEntry($"{PagePath}|x.name", Reason)));
    }

    [Fact]
    public void An_entry_that_matches_no_value_is_stale()
    {
        var problem = Assert.Single(Check("el.textContent = 'safe';", null, new AllowEntry($"{PagePath}|x.name", Reason)));

        Assert.Equal($"ADMIN-JS-ALLOW-LIST {PagePath}|x.name: stale, nothing it excuses is found. Remove the entry.", problem);
    }

    [Fact]
    public void An_entry_with_a_short_reason_excuses_nothing()
    {
        var problems = Check("el.innerHTML = `<td>${x.name}</td>`;", null, new AllowEntry($"{PagePath}|x.name", "short"));

        Assert.Equal(2, problems.Count);
        Assert.Equal($"ADMIN-JS-ALLOW-LIST {PagePath}|x.name: the reason must be at least 20 characters.", problems[0]);
        Assert.Contains($"ADMIN-JS-ALLOW-LIST: {PagePath}|x.name is not excused", problems[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_listed_twice_is_reported_and_the_first_entry_still_excuses()
    {
        var entry = new AllowEntry($"{PagePath}|x.name", Reason);

        var problem = Assert.Single(Check("el.innerHTML = `<td>${x.name}</td>`;", null, entry, entry));

        Assert.Equal($"ADMIN-JS-ALLOW-LIST {PagePath}|x.name: listed more than once. Keep one entry.", problem);
    }

    [Fact]
    public void The_text_names_are_an_option()
    {
        var options = new AdminJsOptions { Folders = ["admin"], TextNames = ["code"] };

        Assert.Empty(Check("el.innerHTML = `<td>${x.name}</td>`;", options));
        Assert.Single(Check("el.innerHTML = `<td>${x.code}</td>`;", options));
    }

    [Fact]
    public void A_text_name_list_that_is_empty_is_refused_rather_than_matching_everything()
    {
        Assert.Throws<ArgumentException>(() => AdminJsEscapeGuard.Scan("x", []));
    }

    [Fact]
    public void The_planted_value_is_caught_and_an_escaped_value_passes()
    {
        GuardSelfTest.AssertCatches(source => Check(source), "el.innerHTML = `<td>${x.title}</td>`;", $"{PagePath}:1:");
        GuardSelfTest.AssertPasses(source => Check(source), "el.innerHTML = `<td>${Shell.esc(x.title)}</td>`;");
    }

    [Fact]
    public void A_scan_that_sees_no_value_fails_its_own_self_test()
    {
        var blind = new AdminJsOptions { Folders = ["admin"], TextNames = ["never-present"] };

        Assert.ThrowsAny<Exception>(() => GuardSelfTest.AssertCatches(source => Check(source, blind), "el.innerHTML = `<td>${x.title}</td>`;", $"{PagePath}:1:"));
    }

    [Fact]
    public void Sources_read_the_scripts_of_the_folders_and_refuse_too_few()
    {
        using var repo = new TempRepository();
        repo.Write("admin/page.js", "el.innerHTML = `<td>${Shell.esc(x.name)}</td>`;");
        repo.Write("admin/sub/more.js", "var a = 1;");

        var sources = AdminJsEscapeGuard.Sources(repo.Root, new AdminJsOptions { Folders = ["admin"], MinimumFileCount = 2 });

        Assert.Equal(2, sources.Count);
        var tooFew = Assert.Throws<InvalidOperationException>(() => AdminJsEscapeGuard.Sources(repo.Root, new AdminJsOptions { Folders = ["admin"], MinimumFileCount = 3 }));
        Assert.Contains("The scan found 2 script(s)", tooFew.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sources_refuse_a_missing_folder_and_an_empty_folder_list()
    {
        using var repo = new TempRepository();

        Assert.Throws<DirectoryNotFoundException>(() => AdminJsEscapeGuard.Sources(repo.Root, Options()));
        Assert.Throws<ArgumentException>(() => AdminJsEscapeGuard.Sources(repo.Root, new AdminJsOptions { Folders = [] }));
    }
}
