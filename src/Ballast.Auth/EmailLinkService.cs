// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;

using Ballast.Email;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Ballast.Auth;

/// <summary>
/// Issues and redeems single-use sign-in links. A link is a random secret that is mailed once; only its SHA-256 is stored, so a copy of
/// the table cannot sign anyone in. A redeem claims the link in one conditional update, so the first request that presents it wins and
/// every later one is refused. A link is never a session: the caller exchanges a redeemed link for one, through
/// <see cref="AuthSessionService{TUser,TKey,TRefresh,TContext}.IssueAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The app resolves a subject to an address. After the claim, a redeem asks the account store whether the subject may sign in.
/// A deactivated or unknown subject is refused and its link stays used. The app issues the session for the subject a redeem returns.
/// </para>
/// <para>
/// The rate limit counts the links issued in the window, then inserts the new one: two requests that arrive together can both pass the
/// count, so a burst may get a few links over the limit. It limits a steady flood; it is not an exact quota.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The app's database context, which maps <see cref="AuthEmailLink"/> with <see cref="AuthEmailLinkModel.ApplyEmailLinkModel"/>.</typeparam>
public sealed class EmailLinkService<TContext>
    where TContext : DbContext
{
    private readonly AuthOptions _auth;
    private readonly AuthEmailLinkOptions _options;
    private readonly TContext _db;
    private readonly EmailService _email;
    private readonly IAuthAccountStore _accounts;
    private readonly TimeProvider _time;

    /// <summary>Creates the service.</summary>
    /// <param name="auth">The sign-in options, whose <see cref="AuthOptions.Failures"/> give the refusals.</param>
    /// <param name="options">The link settings.</param>
    /// <param name="db">The app's database context.</param>
    /// <param name="email">The mail sender, from Ballast.Email.</param>
    /// <param name="accounts">Says whether a redeemed subject may still sign in. The app registers its store; AddBallastAuth registers the Identity one.</param>
    /// <param name="time">The clock.</param>
    public EmailLinkService(AuthOptions auth, AuthEmailLinkOptions options, TContext db, EmailService email, IAuthAccountStore accounts, TimeProvider time)
    {
        _auth = auth;
        _options = options;
        _db = db;
        _email = email;
        _accounts = accounts;
        _time = time;
    }

    /// <summary>
    /// Issues a link for a subject and a purpose, and mails it. The link's secret is passed to <paramref name="compose"/>, which builds the
    /// e-mail around it (the app owns its address and its page). The row is saved before the mail is sent, and it is kept whatever the send
    /// reports.
    /// </summary>
    /// <param name="subjectKey">What the link signs in, as the app names it. At most 128 characters.</param>
    /// <param name="purpose">What the link is for, such as <c>magic_link</c>. At most 64 characters. A link redeems only for its purpose.</param>
    /// <param name="address">The address to mail the link to.</param>
    /// <param name="compose">Builds the e-mail from the link's secret. Its link must carry the secret, and nothing else is sent.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>What the mail sender reported, and when the link expires.</returns>
    /// <exception cref="Ballast.Errors.AppException">
    /// The subject and the purpose were already sent <see cref="AuthEmailLinkOptions.MaxIssuedPerWindow"/> links in the window
    /// (<see cref="AuthFailures.EmailLinkRateLimited"/>). No link is saved and no mail is sent.
    /// </exception>
    /// <exception cref="ArgumentException">A subject, a purpose or an address is blank, or a subject or purpose is too long.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="compose"/> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task<EmailLinkIssued> IssueAsync(string subjectKey, string purpose, string address, Func<string, EmailContent> compose, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentNullException.ThrowIfNull(compose);
        if (subjectKey.Length > AuthEmailLinkModel.SubjectKeyMaxLength)
        {
            throw new ArgumentException($"The subject key is longer than {AuthEmailLinkModel.SubjectKeyMaxLength} characters.", nameof(subjectKey));
        }

        if (purpose.Length > AuthEmailLinkModel.PurposeMaxLength)
        {
            throw new ArgumentException($"The purpose is longer than {AuthEmailLinkModel.PurposeMaxLength} characters.", nameof(purpose));
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var windowStart = now - _options.RateWindow;
        var links = _db.Set<AuthEmailLink>();

        var issuedInWindow = await links.CountAsync(
            link => link.SubjectKey == subjectKey && link.Purpose == purpose && link.CreatedAt > windowStart, ct);
        if (issuedInWindow >= _options.MaxIssuedPerWindow)
        {
            throw _auth.Failures.EmailLinkRateLimited.ToException();
        }

        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now + _options.Lifetime;
        links.Add(new AuthEmailLink
        {
            SubjectKey = subjectKey,
            Purpose = purpose,
            TokenHash = HashSecret(secret),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await _db.SaveChangesAsync(ct);

        var content = compose(secret);
        var result = await _email.SendAsync(address, content.Subject, content.Text, content.Html, ct);
        return new EmailLinkIssued(result, expiresAt);
    }

    /// <summary>
    /// Redeems a link: the link is claimed, and the subject it signs in is returned. A link can be redeemed once. The claim and the purpose
    /// check are one conditional update, so a link presented for another purpose is refused and is not used up.
    /// </summary>
    /// <param name="secret">The secret from the link the user opened.</param>
    /// <param name="purpose">The purpose the caller expects. The link must have been issued for it.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The subject key the link was issued for. The caller turns it into a session.</returns>
    /// <exception cref="Ballast.Errors.AppException">
    /// The secret is blank or unknown, already used, expired, or issued for another purpose (<see cref="AuthFailures.EmailLinkInvalid"/>).
    /// These are one answer, so a link cannot be probed. A subject the account store does not know, or that cannot sign in, gets the same answer.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> is null, empty or blank.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task<string> RedeemAsync(string? secret, string purpose, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw _auth.Failures.EmailLinkInvalid.ToException();
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var hash = HashSecret(secret);
        var links = _db.Set<AuthEmailLink>();

        // One conditional update is the lock: only the request that flips ClaimedAt sees a row count of one. The purpose is part of the
        // condition, so a wrong purpose changes nothing and the link still works for its own purpose.
        var claimed = await links
            .Where(link => link.TokenHash == hash && link.Purpose == purpose && link.ClaimedAt == null && link.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(link => link.ClaimedAt, now), ct);
        if (claimed == 0)
        {
            throw _auth.Failures.EmailLinkInvalid.ToException();
        }

        // The row is ours now: the hash is unique and only one request could claim it. Its subject is read after the claim.
        var subject = await links
            .Where(link => link.TokenHash == hash)
            .Select(link => link.SubjectKey)
            .SingleAsync(ct);

        // The claim is already made, so the link stays used whatever the account says. A refused account gets the same answer as a used link.
        var account = await _accounts.FindAsync(subject, ct);
        if (account is null || !account.CanSignIn)
        {
            throw _auth.Failures.EmailLinkInvalid.ToException();
        }

        return subject;
    }

    /// <summary>
    /// Deletes the links that can no longer matter: expired, and issued before the rate window. Neither a redeem nor the rate limit answers
    /// differently without them. The app runs it from a background job, in the job holder only.
    /// </summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The number of links deleted.</returns>
    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var issuedBefore = now - _options.RateWindow;

        // Link rows are short-lived secrets, not operational records, so a hard delete is the right way to drop them.
        return await _db.Set<AuthEmailLink>()
            .Where(link => link.ExpiresAt < now && link.CreatedAt < issuedBefore)
            .ExecuteDeleteAsync(ct);
    }

    private static string HashSecret(string secret) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
