// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;

using Ballast.Email;
using Ballast.Errors;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ballast.Auth.Tests;

/// <summary>A context that maps only the e-mail link table, as an app's context maps it.</summary>
public sealed class LinkTestDb : DbContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="options">The options, with the connection.</param>
    public LinkTestDb(DbContextOptions<LinkTestDb> options)
        : base(options)
    {
    }

    /// <summary>The links.</summary>
    public DbSet<AuthEmailLink> AuthEmailLinks { get; set; } = default!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyEmailLinkModel();
    }
}

/// <summary>An account store the tests control: every subject is known and may sign in, unless the test deactivates or forgets it.</summary>
internal sealed class FakeAccounts : IAuthAccountStore
{
    private readonly HashSet<string> _deactivated = new();
    private readonly HashSet<string> _forgotten = new();

    /// <summary>Makes the subject refuse to sign in.</summary>
    /// <param name="subject">The subject.</param>
    public void Deactivate(string subject) => _deactivated.Add(subject);

    /// <summary>Lets the subject sign in again.</summary>
    /// <param name="subject">The subject.</param>
    public void Activate(string subject) => _deactivated.Remove(subject);

    /// <summary>Makes the store not know the subject.</summary>
    /// <param name="subject">The subject.</param>
    public void Forget(string subject) => _forgotten.Add(subject);

    /// <inheritdoc />
    public Task<AuthAccountState?> FindAsync(string subject, CancellationToken ct = default)
    {
        if (_forgotten.Contains(subject))
        {
            return Task.FromResult<AuthAccountState?>(null);
        }

        return Task.FromResult<AuthAccountState?>(new AuthAccountState("stamp", !_deactivated.Contains(subject)));
    }
}

/// <summary>One mail the recording transport was asked to send.</summary>
/// <param name="To">The recipient.</param>
/// <param name="Subject">The subject.</param>
/// <param name="Text">The plain-text body.</param>
/// <param name="Html">The HTML body, or null.</param>
internal sealed record SentMail(string To, string Subject, string Text, string? Html);

/// <summary>A mail transport that records what it was asked to send, and can be made to fail.</summary>
internal sealed class RecordingTransport : IMailTransport
{
    /// <summary>The mails sent, in order.</summary>
    public List<SentMail> Sent { get; } = [];

    /// <summary>When true, the next sends throw, as a provider outage would.</summary>
    public bool Fail { get; set; }

    /// <inheritdoc />
    public Task<EmailSendResult> SendAsync(string to, string subject, string text, string? html = null, CancellationToken ct = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("The provider is down.");
        }

        Sent.Add(new SentMail(to, subject, text, html));
        return Task.FromResult(EmailSendResult.Sent);
    }
}

/// <summary>
/// A host for the e-mail link tests: a fresh database, a manual clock, a recording mail transport, and the link and sign-in settings. Each
/// test makes its own, and disposing it drops the database.
/// </summary>
internal sealed class LinkHost : IAsyncDisposable
{
    private LinkHost(SqlTestDatabase database, AuthEmailLinkOptions options, MailSettings settings, RecordingTransport transport)
    {
        Database = database;
        Options = options;
        Transport = transport;
        Accounts = new FakeAccounts();
        Time = new ManualTimeProvider();
        Email = new EmailService(settings, NullLogger<EmailService>.Instance, transport);
    }

    /// <summary>The database for this test.</summary>
    public SqlTestDatabase Database { get; }

    /// <summary>The link settings.</summary>
    public AuthEmailLinkOptions Options { get; }

    /// <summary>The sign-in options, which give the refusals.</summary>
    public AuthOptions Auth { get; } = new();

    /// <summary>The clock. Move it to test expiry and the rate window.</summary>
    public ManualTimeProvider Time { get; }

    /// <summary>The transport, which records the mails.</summary>
    public RecordingTransport Transport { get; }

    /// <summary>The account store the redeem checks after the claim.</summary>
    public FakeAccounts Accounts { get; }

    /// <summary>The mail sender the links use.</summary>
    public EmailService Email { get; }

    /// <summary>Creates a host. Skips the test when SQL Server cannot be reached.</summary>
    /// <param name="configure">Sets the link settings.</param>
    /// <param name="settings">The mail settings. Defaults to a configured sender, so a send reaches the transport.</param>
    /// <returns>The host, with an empty link table.</returns>
    public static async Task<LinkHost> CreateAsync(Action<AuthEmailLinkOptions>? configure = null, MailSettings? settings = null)
    {
        var database = await SqlTestDatabase.CreateAsync();
        var options = new AuthEmailLinkOptions();
        configure?.Invoke(options);
        options.Validate();

        await using (var db = new LinkTestDb(OptionsFor(database)))
        {
            await db.Database.EnsureCreatedAsync();
        }

        return new LinkHost(database, options, settings ?? new MailSettings("test-api-key", "sender@example.test"), new RecordingTransport());
    }

    /// <summary>A new context on this database, as one request has.</summary>
    /// <returns>The context, which the caller disposes.</returns>
    public LinkTestDb NewContext() => new(OptionsFor(Database));

    /// <summary>The link service over a context.</summary>
    /// <param name="db">The context.</param>
    /// <returns>The service.</returns>
    public EmailLinkService<LinkTestDb> Service(LinkTestDb db) => new(Auth, Options, db, Email, Accounts, Time);

    /// <summary>Issues a link for a subject, capturing its secret from the mail the way a real composer would build it.</summary>
    /// <param name="db">The context.</param>
    /// <param name="subject">The subject key.</param>
    /// <param name="purpose">The purpose.</param>
    /// <returns>The secret and the outcome.</returns>
    public async Task<(string Secret, EmailLinkIssued Issued)> IssueAsync(LinkTestDb db, string subject, string purpose = "magic_link")
    {
        string? secret = null;
        var issued = await Service(db).IssueAsync(subject, purpose, "person@example.test", raw =>
        {
            secret = raw;
            return new EmailContent("Sign in", $"Open https://app.example.test/signin?link={raw}");
        });

        return (secret!, issued);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Database.DisposeAsync();

    private static DbContextOptions<LinkTestDb> OptionsFor(SqlTestDatabase database) =>
        new DbContextOptionsBuilder<LinkTestDb>().UseSqlServer(database.ConnectionString).Options;
}

/// <summary>The e-mail sign-in link on SQL Server: single use, expiry, purpose, storage, rate limit, concurrency and the send outcomes.</summary>
public class EmailLinkTests
{
    [Fact]
    public async Task A_link_signs_in_once_and_then_is_refused()
    {
        await using var host = await LinkHost.CreateAsync();
        await using var db = host.NewContext();
        var (secret, _) = await host.IssueAsync(db, "contact-42");

        var subject = await host.Service(db).RedeemAsync(secret, "magic_link");
        Assert.Equal("contact-42", subject);

        var again = await Assert.ThrowsAsync<AppException>(() => host.Service(db).RedeemAsync(secret, "magic_link"));
        Assert.Equal("EMAIL_LINK_INVALID", again.Code);
        Assert.Equal(401, again.StatusCode);
    }

    [Fact]
    public async Task The_link_is_mailed_with_its_secret_and_the_send_result_and_expiry_are_returned()
    {
        await using var host = await LinkHost.CreateAsync();
        await using var db = host.NewContext();
        var (secret, issued) = await host.IssueAsync(db, "contact-42");

        Assert.Equal(EmailSendResult.Sent, issued.Result);
        Assert.Equal(host.Time.Now.UtcDateTime + host.Options.Lifetime, issued.ExpiresAtUtc);

        var mail = Assert.Single(host.Transport.Sent);
        Assert.Equal("person@example.test", mail.To);
        Assert.Contains(secret, mail.Text);
    }

    [Fact]
    public async Task A_link_works_until_its_lifetime_passes_and_then_is_refused()
    {
        await using var host = await LinkHost.CreateAsync();
        await using var db = host.NewContext();
        var (early, _) = await host.IssueAsync(db, "contact-1");
        var (late, _) = await host.IssueAsync(db, "contact-2");

        host.Time.Now = host.Time.Now.AddMinutes(14);
        Assert.Equal("contact-1", await host.Service(db).RedeemAsync(early, "magic_link"));

        host.Time.Now = host.Time.Now.AddMinutes(1);
        var expired = await Assert.ThrowsAsync<AppException>(() => host.Service(db).RedeemAsync(late, "magic_link"));
        Assert.Equal("EMAIL_LINK_INVALID", expired.Code);
    }

    [Fact]
    public async Task A_link_for_another_purpose_is_refused_and_is_not_used_up()
    {
        await using var host = await LinkHost.CreateAsync();
        await using var db = host.NewContext();
        var (secret, _) = await host.IssueAsync(db, "contact-42", "magic_link");

        var wrong = await Assert.ThrowsAsync<AppException>(() => host.Service(db).RedeemAsync(secret, "password_reset"));
        Assert.Equal("EMAIL_LINK_INVALID", wrong.Code);

        Assert.Equal("contact-42", await host.Service(db).RedeemAsync(secret, "magic_link"));
    }

    [Fact]
    public async Task An_unknown_or_blank_secret_gets_the_same_answer_as_a_used_one()
    {
        await using var host = await LinkHost.CreateAsync();
        await using var db = host.NewContext();

        var unknown = await Assert.ThrowsAsync<AppException>(() => host.Service(db).RedeemAsync("never-issued", "magic_link"));
        var blank = await Assert.ThrowsAsync<AppException>(() => host.Service(db).RedeemAsync("  ", "magic_link"));
        var missing = await Assert.ThrowsAsync<AppException>(() => host.Service(db).RedeemAsync(null, "magic_link"));

        Assert.Equal("EMAIL_LINK_INVALID", unknown.Code);
        Assert.Equal(unknown.Code, blank.Code);
        Assert.Equal(unknown.Code, missing.Code);
        Assert.Equal(unknown.StatusCode, missing.StatusCode);
    }

    [Fact]
    public async Task The_secret_is_never_stored_only_its_sha256()
    {
        await using var host = await LinkHost.CreateAsync();
        await using var db = host.NewContext();
        var (secret, _) = await host.IssueAsync(db, "contact-42");

        var row = await db.AuthEmailLinks.AsNoTracking().SingleAsync();
        var expectedHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

        Assert.Equal(expectedHash, row.TokenHash);
        Assert.NotEqual(secret, row.TokenHash);
        Assert.DoesNotContain(secret, row.SubjectKey);
        Assert.DoesNotContain(secret, row.Purpose);
        Assert.Equal(43, secret.Length);
        Assert.DoesNotContain('+', secret);
        Assert.DoesNotContain('/', secret);
        Assert.DoesNotContain('=', secret);
        Assert.Null(row.ClaimedAt);
    }

    [Fact]
    public async Task The_rate_limit_refuses_links_beyond_the_window_count_and_resets_when_the_window_passes()
    {
        await using var host = await LinkHost.CreateAsync(o =>
        {
            o.MaxIssuedPerWindow = 2;
            o.RateWindow = TimeSpan.FromHours(1);
        });
        await using var db = host.NewContext();

        await host.IssueAsync(db, "contact-1");
        await host.IssueAsync(db, "contact-1");

        var refused = await Assert.ThrowsAsync<AppException>(() => host.IssueAsync(db, "contact-1"));
        Assert.Equal("RATE_LIMITED", refused.Code);
        Assert.Equal(429, refused.StatusCode);
        Assert.Equal(2, host.Transport.Sent.Count);

        // Another subject is counted on its own.
        await host.IssueAsync(db, "contact-2");
        Assert.Equal(3, host.Transport.Sent.Count);

        // Once the window has passed the links issued at its start, the subject may ask again.
        host.Time.Now = host.Time.Now.AddHours(1);
        await host.IssueAsync(db, "contact-1");
        Assert.Equal(4, host.Transport.Sent.Count);
    }

    [Fact]
    public async Task Concurrent_redeems_of_one_link_let_exactly_one_through()
    {
        await using var host = await LinkHost.CreateAsync();
        string secret;
        await using (var db = host.NewContext())
        {
            secret = (await host.IssueAsync(db, "contact-7")).Secret;
        }

        var attempts = Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = host.NewContext();
            try
            {
                await host.Service(db).RedeemAsync(secret, "magic_link");
                return true;
            }
            catch (AppException ex) when (ex.Code == "EMAIL_LINK_INVALID")
            {
                return false;
            }
        });

        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public async Task A_send_that_is_not_configured_keeps_the_link_and_reports_it()
    {
        await using var host = await LinkHost.CreateAsync(settings: new MailSettings(null, null, logInsteadOfSending: false));
        await using var db = host.NewContext();

        var (_, issued) = await host.IssueAsync(db, "contact-42");

        Assert.Equal(EmailSendResult.NotConfigured, issued.Result);
        Assert.Empty(host.Transport.Sent);
        var row = await db.AuthEmailLinks.AsNoTracking().SingleAsync();
        Assert.Null(row.ClaimedAt);
    }

    [Fact]
    public async Task A_failed_send_is_reported_and_the_link_is_kept()
    {
        await using var host = await LinkHost.CreateAsync();
        await using var db = host.NewContext();
        host.Transport.Fail = true;

        var (_, issued) = await host.IssueAsync(db, "contact-42");

        Assert.Equal(EmailSendResult.Failed, issued.Result);
        Assert.Single(await db.AuthEmailLinks.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Purge_deletes_only_links_that_have_expired_and_left_the_window()
    {
        await using var host = await LinkHost.CreateAsync();
        await using var db = host.NewContext();
        await host.IssueAsync(db, "contact-1");

        host.Time.Now = host.Time.Now.AddMinutes(61);
        await host.IssueAsync(db, "contact-2");

        var purged = await host.Service(db).PurgeAsync();

        Assert.Equal(1, purged);
        var remaining = await db.AuthEmailLinks.AsNoTracking().SingleAsync();
        Assert.Equal("contact-2", remaining.SubjectKey);
    }

    [Fact]
    public async Task Blank_or_oversized_inputs_are_refused_as_arguments()
    {
        await using var host = await LinkHost.CreateAsync();
        await using var db = host.NewContext();
        var service = host.Service(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.IssueAsync(" ", "magic_link", "person@example.test", raw => new EmailContent("s", raw)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.IssueAsync(new string('x', 129), "magic_link", "person@example.test", raw => new EmailContent("s", raw)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.IssueAsync("contact-1", new string('p', 65), "person@example.test", raw => new EmailContent("s", raw)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RedeemAsync("secret", " "));
    }

    [Fact]
    public async Task A_deactivated_subject_is_refused_and_its_link_stays_used()
    {
        await using var host = await LinkHost.CreateAsync();
        await using var db = host.NewContext();
        var (secret, _) = await host.IssueAsync(db, "contact:42");

        host.Accounts.Deactivate("contact:42");
        var refused = await Assert.ThrowsAsync<AppException>(() => host.Service(db).RedeemAsync(secret, "magic_link"));
        Assert.Equal("EMAIL_LINK_INVALID", refused.Code);

        // Reactivated, the same link still does not work: the refused redeem used it up.
        host.Accounts.Activate("contact:42");
        var again = await Assert.ThrowsAsync<AppException>(() => host.Service(db).RedeemAsync(secret, "magic_link"));
        Assert.Equal("EMAIL_LINK_INVALID", again.Code);
    }

    [Fact]
    public async Task A_subject_the_account_store_does_not_know_is_refused()
    {
        await using var host = await LinkHost.CreateAsync();
        await using var db = host.NewContext();
        var (secret, _) = await host.IssueAsync(db, "contact:43");

        host.Accounts.Forget("contact:43");
        var error = await Assert.ThrowsAsync<AppException>(() => host.Service(db).RedeemAsync(secret, "magic_link"));
        Assert.Equal("EMAIL_LINK_INVALID", error.Code);
    }

    [Fact]
    public void The_link_table_has_a_unique_hash_a_rate_limit_index_and_the_default_name()
    {
        using var db = new LinkTestDb(new DbContextOptionsBuilder<LinkTestDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = db.Model.FindEntityType(typeof(AuthEmailLink))!;

        Assert.Equal("AuthEmailLinks", entity.GetTableName());
        Assert.Equal(128, entity.FindProperty(nameof(AuthEmailLink.TokenHash))!.GetMaxLength());
        Assert.Equal(AuthEmailLinkModel.SubjectKeyMaxLength, entity.FindProperty(nameof(AuthEmailLink.SubjectKey))!.GetMaxLength());
        Assert.Equal(AuthEmailLinkModel.PurposeMaxLength, entity.FindProperty(nameof(AuthEmailLink.Purpose))!.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == nameof(AuthEmailLink.TokenHash));
        Assert.Contains(entity.GetIndexes(), i => !i.IsUnique && i.Properties.Count == 3);
    }
}

/// <summary>The link settings are checked when they are registered, so a bad value stops the start.</summary>
public class EmailLinkOptionsTests
{
    [Fact]
    public void The_defaults_are_15_minutes_and_five_links_an_hour()
    {
        var options = new AuthEmailLinkOptions();

        Assert.Equal(TimeSpan.FromMinutes(15), options.Lifetime);
        Assert.Equal(5, options.MaxIssuedPerWindow);
        Assert.Equal(TimeSpan.FromHours(1), options.RateWindow);
        options.Validate();
    }

    [Fact]
    public void A_lifetime_of_zero_is_refused()
    {
        var options = new AuthEmailLinkOptions { Lifetime = TimeSpan.Zero };

        var error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("Lifetime", error.Message);
    }

    [Fact]
    public void A_lifetime_over_a_day_is_refused_and_a_day_is_allowed()
    {
        Assert.Throws<InvalidOperationException>(() => new AuthEmailLinkOptions { Lifetime = TimeSpan.FromHours(25) }.Validate());
        new AuthEmailLinkOptions { Lifetime = TimeSpan.FromHours(24) }.Validate();
    }

    [Fact]
    public void A_limit_below_one_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new AuthEmailLinkOptions { MaxIssuedPerWindow = 0 }.Validate());
        Assert.Contains("MaxIssuedPerWindow", error.Message);
    }

    [Fact]
    public void A_window_of_zero_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new AuthEmailLinkOptions { RateWindow = TimeSpan.Zero }.Validate());
        Assert.Contains("RateWindow", error.Message);
    }

    [Fact]
    public void Registration_refuses_an_out_of_range_setting_before_the_app_starts()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddBallastEmailLinks<LinkTestDb>(o => o.Lifetime = TimeSpan.Zero));
    }
}
