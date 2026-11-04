// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Ballast.Auth;

/// <summary>
/// Reads the directory over Microsoft Graph with the client-credentials grant. It follows at most <see cref="MaxPages"/> pages, follows a next
/// link only when it is an https link on Graph (the token goes with it), and retries a throttled answer (429 or 503) once after its Retry-After,
/// at most <see cref="MaxRetryWait"/>. The client is registered as a typed HTTP client, so it has the registered timeout.
/// </summary>
public sealed class MicrosoftGraphClient : IMicrosoftGraphClient
{
    /// <summary>The most pages one list may have. A list with more is a read failure, not a partial one.</summary>
    public const int MaxPages = 20;

    /// <summary>The longest wait a Retry-After may ask for.</summary>
    public static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);

    /// <summary>The wait when a throttled answer gives no Retry-After.</summary>
    public static readonly TimeSpan DefaultRetryWait = TimeSpan.FromSeconds(10);

    private const string GraphBase = "https://graph.microsoft.com/v1.0/";
    private const string GraphHost = "graph.microsoft.com";
    private const string GraphScope = "https://graph.microsoft.com/.default";

    private readonly HttpClient _http;
    private readonly MicrosoftSignInOptions _options;
    private readonly TimeProvider _time;

    /// <summary>Creates the client.</summary>
    /// <param name="http">The HTTP client, from the typed registration.</param>
    /// <param name="options">The settings: the tenant, the client id and the client secret.</param>
    /// <param name="time">The clock the Retry-After wait runs on. Null, the default, uses the system clock.</param>
    public MicrosoftGraphClient(HttpClient http, MicrosoftSignInOptions options, TimeProvider? time = null)
    {
        _http = http;
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<MicrosoftDirectorySnapshot> ReadDirectoryAsync(CancellationToken ct = default)
    {
        var clientId = _options.ClientId ?? string.Empty;
        var token = await GetTokenAsync(ct);
        try
        {
            // The service principal of this application in the tenant: its app roles, and its id for the assignments.
            using var servicePrincipal = await GetJsonAsync($"{GraphBase}servicePrincipals(appId='{clientId}')?$select=id,appRoles", token, ct);
            var root = servicePrincipal.RootElement;
            var servicePrincipalId = GuidOf(root, "id");

            List<MicrosoftAppRole> appRoles = [];
            foreach (var role in root.GetProperty("appRoles").EnumerateArray())
            {
                var value = StringOrNull(role, "value");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    appRoles.Add(new MicrosoftAppRole(GuidOf(role, "id"), value.Trim()));
                }
            }

            var assignments = await ReadListAsync(
                $"{GraphBase}servicePrincipals/{servicePrincipalId:D}/appRoleAssignedTo",
                token,
                item => new MicrosoftAssignment(GuidOf(item, "principalId"), StringOrNull(item, "principalType") ?? string.Empty, GuidOf(item, "appRoleId")),
                ct);

            // Each assigned user once, with the fields the sync needs.
            var users = new Dictionary<Guid, MicrosoftUser>();
            foreach (var principalId in assignments.Where(a => a.PrincipalType == "User").Select(a => a.PrincipalId).Distinct())
            {
                using var user = await GetJsonAsync($"{GraphBase}users/{principalId:D}?$select=id,givenName,surname,userPrincipalName,accountEnabled", token, ct);
                var u = user.RootElement;
                users[principalId] = new MicrosoftUser(
                    principalId,
                    StringOrNull(u, "givenName"),
                    StringOrNull(u, "surname"),
                    StringOrNull(u, "userPrincipalName"),
                    u.GetProperty("accountEnabled").GetBoolean());
            }

            // The end of the newest client secret, so the owner is warned before it ends.
            using var application = await GetJsonAsync($"{GraphBase}applications(appId='{clientId}')?$select=passwordCredentials", token, ct);
            DateTimeOffset? secretEnds = null;
            foreach (var credential in application.RootElement.GetProperty("passwordCredentials").EnumerateArray())
            {
                if (StringOrNull(credential, "endDateTime") is { } text
                    && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ends)
                    && (secretEnds is null || ends > secretEnds))
                {
                    secretEnds = ends;
                }
            }

            return new MicrosoftDirectorySnapshot(appRoles, assignments, users, secretEnds);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or JsonException or UriFormatException)
        {
            throw new MicrosoftGraphException(DirectorySyncFailure.Response, null, "Microsoft answered with a shape this application does not read.", ex);
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        var body = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{_options.TenantId}/oauth2/v2.0/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _options.ClientId ?? string.Empty,
                    ["client_secret"] = _options.ClientSecret ?? string.Empty,
                    ["scope"] = GraphScope,
                    ["grant_type"] = "client_credentials",
                }),
            },
            DirectorySyncFailure.Token,
            ct);

        try
        {
            using var document = JsonDocument.Parse(body);
            if (StringOrNull(document.RootElement, "access_token") is { Length: > 0 } token)
            {
                return token;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new MicrosoftGraphException(DirectorySyncFailure.Token, null, "The token answer is not a JSON object.", ex);
        }

        throw new MicrosoftGraphException(DirectorySyncFailure.Token, null, "The token answer has no access token.");
    }

    // Every page of a list. The next link is followed only when it is an https link on Graph, because the token goes with it.
    private async Task<List<T>> ReadListAsync<T>(string firstUrl, string token, Func<JsonElement, T> read, CancellationToken ct)
    {
        List<T> items = [];
        string? url = firstUrl;
        for (var page = 1; url is not null; page++)
        {
            if (page > MaxPages)
            {
                throw new MicrosoftGraphException(DirectorySyncFailure.Response, null, $"Microsoft has more than {MaxPages} pages of assignments (too many pages).");
            }

            using var document = await GetJsonAsync(url, token, ct);
            var root = document.RootElement;
            foreach (var item in root.GetProperty("value").EnumerateArray())
            {
                items.Add(read(item));
            }

            url = StringOrNull(root, "@odata.nextLink");
            if (url is not null && !IsGraphLink(url))
            {
                throw new MicrosoftGraphException(DirectorySyncFailure.Response, null, "Microsoft's next page is not on Microsoft Graph, so it is not followed.");
            }
        }

        return items;
    }

    private static bool IsGraphLink(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var next)
        && next.Scheme == Uri.UriSchemeHttps
        && string.Equals(next.Host, GraphHost, StringComparison.OrdinalIgnoreCase);

    private async Task<JsonDocument> GetJsonAsync(string url, string token, CancellationToken ct)
    {
        var body = await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return request;
            },
            DirectorySyncFailure.Graph,
            ct);

        return JsonDocument.Parse(body);
    }

    // A throttled answer (429 or 503) is retried once after its Retry-After. A request is built again for the retry, because a request message
    // cannot be sent twice.
    private async Task<string> SendAsync(Func<HttpRequestMessage> build, DirectorySyncFailure failure, CancellationToken ct)
    {
        var reply = await SendOnceAsync(build, failure, ct);
        if (reply.Status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            await Task.Delay(reply.RetryWait, _time, ct);
            reply = await SendOnceAsync(build, failure, ct);
        }

        if (!IsSuccess(reply.Status))
        {
            throw new MicrosoftGraphException(failure, (int)reply.Status, $"Microsoft answered {(int)reply.Status}.");
        }

        return reply.Body;
    }

    private async Task<Reply> SendOnceAsync(Func<HttpRequestMessage> build, DirectorySyncFailure failure, CancellationToken ct)
    {
        try
        {
            using var request = build();
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return new Reply(response.StatusCode, body, RetryWait(response));
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The client's own timeout, not the caller's cancellation.
            throw new MicrosoftGraphException(DirectorySyncFailure.Timeout, null, "The request to Microsoft timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MicrosoftGraphException(failure, null, "The request to Microsoft failed.", ex);
        }
    }

    private static TimeSpan RetryWait(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        var wait = retry is null
            ? DefaultRetryWait
            : retry.Delta ?? (retry.Date is { } date ? date - DateTimeOffset.UtcNow : DefaultRetryWait);
        if (wait < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return wait > MaxRetryWait ? MaxRetryWait : wait;
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static string? StringOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Guid GuidOf(JsonElement element, string name) =>
        Guid.TryParse(StringOrNull(element, name), out var id) ? id : throw new FormatException($"'{name}' is missing or not a GUID.");

    private sealed record Reply(HttpStatusCode Status, string Body, TimeSpan RetryWait);
}
