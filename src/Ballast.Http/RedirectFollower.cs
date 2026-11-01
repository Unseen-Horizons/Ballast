// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Runtime.ExceptionServices;

namespace Ballast.Http;

/// <summary>
/// Sends a request and follows its redirects by hand, deciding at each hop whether the credential and the body go with it, and
/// before the hop is sent. The client it uses must not follow redirects itself: register it with
/// <see cref="SafeHttpClientExtensions.AddSafeHttpClient"/>, which turns that off. An automatic follow would send a credential
/// to a hop this class has not yet judged.
/// </summary>
/// <remarks>
/// The rules, applied to every hop (see <see cref="RedirectRules"/>):
/// <list type="bullet">
/// <item><description>Each address is checked by <see cref="PublicDestinationGuard.EnsurePublicDestination"/> before the request.</description></item>
/// <item><description>A URL with user information is refused, for the first address and for each hop.</description></item>
/// <item><description>An https address never redirects to http.</description></item>
/// <item><description>A credential is sent only over https, and only to the host and port of the first address. A hop that leaves them is refused.</description></item>
/// <item><description>A 307 or 308 re-sends the body only to the host of the first address.</description></item>
/// <item><description>A 301, 302 or 303 becomes a GET without a body.</description></item>
/// <item><description>A redirect with no Location is refused, and so is the redirect after <see cref="RedirectFollowerOptions.MaxRedirects"/>.</description></item>
/// </list>
/// </remarks>
public sealed class RedirectFollower
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _clientName;
    private readonly RedirectFollowerOptions _options;
    private readonly RedirectPolicy _policy;

    /// <summary>Creates a follower over the named client.</summary>
    /// <param name="httpFactory">Creates the client. Required.</param>
    /// <param name="clientName">The name the client is registered under with <see cref="SafeHttpClientExtensions.AddSafeHttpClient"/>. Required.</param>
    /// <param name="options">The rules. Null uses the defaults.</param>
    /// <exception cref="ArgumentException"><paramref name="clientName"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="RedirectFollowerOptions.MaxRedirects"/> is below 0.</exception>
    public RedirectFollower(IHttpClientFactory httpFactory, string clientName, RedirectFollowerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        _options = options ?? new RedirectFollowerOptions();
        ArgumentOutOfRangeException.ThrowIfNegative(_options.MaxRedirects);
        _httpFactory = httpFactory;
        _clientName = clientName;
        _policy = new RedirectPolicy(_options.RequireHttps, AllowSchemeDowngrade: false);
    }

    /// <summary>
    /// Sends the request and follows its redirects. The result is the first response that is not a followed redirect: a 2xx, or
    /// any other status the caller decides about. The caller disposes it. The redirect responses are disposed here.
    /// </summary>
    /// <param name="request">The request. Required.</param>
    /// <param name="ct">Cancels the request. A timeout of the client is not a cancellation, and it is thrown as the client throws it.</param>
    /// <returns>The final response.</returns>
    /// <exception cref="HttpSafetyException">A rule refused the first address or a hop. The reason says which.</exception>
    /// <exception cref="HttpRequestException">The host could not be reached, and the failure is not a refusal.</exception>
    public async Task<HttpResponseMessage> SendAsync(RedirectRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Url);
        var origin = request.Url;
        var credentialed = request.Authorization is not null;
        CheckOrigin(origin, credentialed);

        var http = _httpFactory.CreateClient(_clientName);
        var current = origin;
        var method = request.Method;
        var hasBody = request.Body is not null && !IsBodyless(method);

        for (var hop = 0; ; hop++)
        {
            if (!_options.AllowNonPublicAddresses)
            {
                PublicDestinationGuard.EnsurePublicDestination(current.AbsoluteUri);
            }

            using var message = new HttpRequestMessage(method, current);
            if (hasBody)
            {
                message.Content = request.Body!();
            }
            if (credentialed)
            {
                message.Headers.Authorization = request.Authorization;
            }

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException ex) when (ex.InnerException is HttpSafetyException refused)
            {
                // The connect-time check refused the address. The handler wraps it, so the reason is taken from inside.
                ExceptionDispatchInfo.Capture(refused).Throw();
                throw;
            }

            var status = response.StatusCode;
            if (!IsRedirect(status))
            {
                return response;
            }

            Uri location;
            try
            {
                location = RedirectRules.RequireLocation(response, current);
            }
            finally
            {
                response.Dispose();
            }

            if (hop >= _options.MaxRedirects)
            {
                throw new HttpSafetyException(HttpSafetyReason.TooManyRedirects,
                    $"{origin.Host} redirected more than {_options.MaxRedirects} times.", origin);
            }

            current = RedirectRules.Next(origin, current, location, status, hasBody, credentialed, _policy);

            if (status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther)
            {
                method = HttpMethod.Get;
                hasBody = false;
            }
        }
    }

    /// <summary>Whether a status is a redirect this class follows: 301, 302, 303, 307 or 308. A 300 or 305 is an answer, not a redirect.</summary>
    /// <param name="status">The status to test.</param>
    /// <returns>True for the five redirect statuses.</returns>
    public static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    /// <summary>Whether a redirect keeps the method and the body (307, 308). The others become a GET.</summary>
    internal static bool KeepsMethod(HttpStatusCode status) =>
        status is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool IsBodyless(HttpMethod method) => method == HttpMethod.Get || method == HttpMethod.Head;

    private void CheckOrigin(Uri origin, bool credentialed)
    {
        if (!origin.IsAbsoluteUri || origin.Scheme is not ("http" or "https"))
        {
            throw new HttpSafetyException(HttpSafetyReason.InvalidUrl, "A request is made to an absolute http or https URL.");
        }

        if (origin.UserInfo.Length > 0)
        {
            throw new HttpSafetyException(HttpSafetyReason.UserInfoInUrl,
                "A URL that embeds credentials (user:password@) is refused.", origin);
        }

        if (credentialed && origin.Scheme != Uri.UriSchemeHttps)
        {
            throw new HttpSafetyException(HttpSafetyReason.CredentialNotHttps,
                $"{origin.Host} carries a credential, so its address must be https. The credential is not sent in clear.", origin);
        }

        if (_options.RequireHttps && origin.Scheme != Uri.UriSchemeHttps)
        {
            throw new HttpSafetyException(HttpSafetyReason.SchemeNotAllowed,
                $"{origin.Host} is not https, and this request is made over https only.", origin);
        }
    }
}
