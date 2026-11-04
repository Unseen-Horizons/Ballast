// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

namespace Ballast.Http;

/// <summary>
/// The one set of rules every redirect follows, applied to one hop: <see cref="RedirectFollower"/> uses it for each hop it sends,
/// and <c>PoliteFetcher</c> uses it for each hop of a page and of robots.txt. A rule is written here once, so no
/// two loops can disagree about it.
/// </summary>
internal static class RedirectRules
{
    /// <summary>
    /// The Location of a redirect response. A response with no Location header is a missing one; a response whose Location is
    /// not a URL is an invalid one. Either is a refusal, because there is no next address to follow.
    /// </summary>
    /// <param name="response">The redirect response. Its headers are read, not its body.</param>
    /// <param name="current">The address that answered, for the message.</param>
    /// <returns>The Location, relative or absolute.</returns>
    /// <exception cref="HttpSafetyException">
    /// <see cref="HttpSafetyReason.MissingLocation"/> when there is no Location header, or
    /// <see cref="HttpSafetyReason.InvalidLocation"/> when the header is present but is not a URL.
    /// </exception>
    internal static Uri RequireLocation(HttpResponseMessage response, Uri current)
    {
        var location = response.Headers.Location;
        if (location is not null)
        {
            return location;
        }

        if (response.Headers.Contains("Location"))
        {
            throw new HttpSafetyException(HttpSafetyReason.InvalidLocation,
                $"{current.Host} answered {(int)response.StatusCode} with a Location that is not a URL.", current);
        }

        throw new HttpSafetyException(HttpSafetyReason.MissingLocation,
            $"{current.Host} answered {(int)response.StatusCode} without a Location header.", current);
    }

    /// <summary>
    /// The next address of a redirect, or the reason it may not be followed.
    /// </summary>
    /// <param name="origin">The address the caller asked for first. Credential and body rules compare against it.</param>
    /// <param name="current">The address that answered with the redirect.</param>
    /// <param name="location">The Location header, relative or absolute.</param>
    /// <param name="status">The redirect status.</param>
    /// <param name="bodyFollows">Whether the request that was redirected carries a body.</param>
    /// <param name="credentialed">Whether the request carries a credential (an Authorization header).</param>
    /// <param name="policy">The scheme rules for this kind of request.</param>
    /// <returns>The next address.</returns>
    /// <exception cref="HttpSafetyException">The hop is refused; the reason says which rule.</exception>
    internal static Uri Next(
        Uri origin,
        Uri current,
        Uri location,
        HttpStatusCode status,
        bool bodyFollows,
        bool credentialed,
        RedirectPolicy policy)
    {
        if (!Uri.TryCreate(current, location, out var next))
        {
            throw new HttpSafetyException(HttpSafetyReason.InvalidLocation,
                $"{current.Host} redirected to something that is not a URL.", current);
        }

        if (next.Scheme is not ("http" or "https"))
        {
            throw new HttpSafetyException(HttpSafetyReason.SchemeNotAllowed,
                $"{current.Host} redirected to {next.Scheme}. Only http and https are followed.", current);
        }

        if (next.UserInfo.Length > 0)
        {
            throw new HttpSafetyException(HttpSafetyReason.UserInfoInUrl,
                $"{current.Host} redirected to a URL that embeds credentials, which is refused.", current);
        }

        if (!policy.AllowSchemeDowngrade && current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp)
        {
            throw new HttpSafetyException(HttpSafetyReason.SchemeDowngrade,
                $"{current.Host} redirected from https to http, which is refused.", current);
        }

        if (policy.RequireHttps && next.Scheme != Uri.UriSchemeHttps)
        {
            throw new HttpSafetyException(HttpSafetyReason.SchemeNotAllowed,
                $"{current.Host} redirected to {next.Scheme}. This request is made over https only.", current);
        }

        if (credentialed)
        {
            if (next.Scheme != Uri.UriSchemeHttps)
            {
                throw new HttpSafetyException(HttpSafetyReason.CredentialNotHttps,
                    $"{current.Host} redirected a request with a credential to {next.Scheme}; the credential is not sent in clear.", current);
            }

            if (!string.Equals(next.Host, origin.Host, StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpSafetyException(HttpSafetyReason.CredentialOffHost,
                    $"{current.Host} redirected to {next.Host}. A request with a credential is not sent off its host.", current);
            }

            if (next.Port != origin.Port)
            {
                throw new HttpSafetyException(HttpSafetyReason.CredentialOtherPort,
                    $"{current.Host} redirected to port {next.Port}. A request with a credential is not sent to another port.", current);
            }
        }

        // A 307 or 308 keeps the method and the body, so the body would go to the next address. The body goes only where the
        // caller sent it, so a redirect to another host is refused. A 301, 302 or 303 drops the body (see RedirectFollower).
        if (bodyFollows && RedirectFollower.KeepsMethod(status) && !string.Equals(next.Host, origin.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpSafetyException(HttpSafetyReason.BodyOffHost,
                $"{current.Host} redirected a request with a body to {next.Host}. The body is not re-sent to another host.", current);
        }

        return next;
    }
}

/// <summary>The scheme rules for one redirect chain. See <see cref="RedirectRules.Next"/>.</summary>
/// <param name="RequireHttps">Whether every address in the chain must be https. When false, http is allowed.</param>
/// <param name="AllowSchemeDowngrade">Whether an https address may redirect to http. Only robots.txt allows this.</param>
internal sealed record RedirectPolicy(bool RequireHttps, bool AllowSchemeDowngrade);
