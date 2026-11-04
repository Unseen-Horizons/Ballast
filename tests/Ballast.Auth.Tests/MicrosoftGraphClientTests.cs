// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Ballast.Testing.Fakes;

namespace Ballast.Auth.Tests;

/// <summary>
/// The Graph client against a scripted HTTP handler: the token, the reads, the paging, the throttling and each failure. Each failure is
/// reported as its kind and never carries the client secret. No test reaches the network, and no test waits in real time: the Retry-After
/// waits are recorded by a clock that does not sleep.
/// </summary>
public class MicrosoftGraphClientTests
{
    private const string Tenant = "23871d81-3b76-4fc3-8f5a-dcea3b895ba7";
    private const string Client = "fa669e2c-2040-4f50-baa6-6714c7af7150";

    // Placeholders that only the scripted handler sees. The real secret is an app setting and is never in a file.
    private const string FakeSecret = "placeholder-not-a-secret";
    private const string AccessToken = "fake-access-token";

    private const string LoginHost = "https://login.microsoftonline.com/";
    private const string GraphBase = "https://graph.microsoft.com/v1.0/";

    private static readonly Guid ServicePrincipalId = Guid.Parse("0a1b2c3d-0000-4000-8000-000000000001");
    private static readonly Guid AdminRoleId = Guid.Parse("0a1b2c3d-0000-4000-8000-000000000002");
    private static readonly Guid SupportRoleId = Guid.Parse("0a1b2c3d-0000-4000-8000-000000000003");
    private static readonly Guid AnaId = Guid.Parse("0a1b2c3d-0000-4000-8000-000000000004");
    private static readonly Guid MarkoId = Guid.Parse("0a1b2c3d-0000-4000-8000-000000000005");
    private static readonly string SecondPage = $"{GraphBase}servicePrincipals/{ServicePrincipalId:D}/appRoleAssignedTo?$skiptoken=page2";

    /// <summary>A clock whose timers fire at once and record how long each one was asked for.</summary>
    private sealed class RecordingWaits : TimeProvider
    {
        public List<TimeSpan> Waits { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waits.Add(dueTime);
            callback(state);
            return new FiredTimer();
        }

        private sealed class FiredTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static (MicrosoftGraphClient Client, ScriptedHttpHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, RecordingWaits? waits = null)
    {
        var handler = new ScriptedHttpHandler(respond);
        var options = new MicrosoftSignInOptions { TenantId = Tenant, ClientId = Client, ClientSecret = FakeSecret };
        return (new MicrosoftGraphClient(new HttpClient(handler), options, waits ?? new RecordingWaits()), handler);
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Throttled(HttpStatusCode status, TimeSpan? retryAfter)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        if (retryAfter is { } wait)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
        }

        return response;
    }

    private static Dictionary<string, object?> UserJson(string given, string surname, string upn) => new()
    {
        ["givenName"] = given,
        ["surname"] = surname,
        ["userPrincipalName"] = upn,
        ["accountEnabled"] = true,
    };

    /// <summary>
    /// A small directory: two app roles, Ana (Support) on the first page of assignments, Marko (Admin) on the second. The next link of the
    /// first page can be replaced to test the link rules.
    /// </summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Directory(string? nextPageOfAssignments = null)
    {
        var nextPage = nextPageOfAssignments ?? SecondPage;
        return request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (IsToken(url))
            {
                return Json(new Dictionary<string, object?> { ["token_type"] = "Bearer", ["access_token"] = AccessToken });
            }

            if (url.Contains("servicePrincipals(appId=", StringComparison.Ordinal))
            {
                return Json(new Dictionary<string, object?>
                {
                    ["id"] = ServicePrincipalId,
                    ["appRoles"] = new[]
                    {
                        new { id = AdminRoleId, value = "Admin" },
                        new { id = SupportRoleId, value = "Support" },
                    },
                });
            }

            if (url.Contains("skiptoken=page2", StringComparison.Ordinal))
            {
                return Json(new Dictionary<string, object?>
                {
                    ["value"] = new[] { new { principalId = MarkoId, principalType = "User", appRoleId = AdminRoleId } },
                });
            }

            if (url.Contains("appRoleAssignedTo", StringComparison.Ordinal))
            {
                return Json(new Dictionary<string, object?>
                {
                    ["value"] = new[] { new { principalId = AnaId, principalType = "User", appRoleId = SupportRoleId } },
                    ["@odata.nextLink"] = nextPage,
                });
            }

            if (url.Contains($"/users/{AnaId:D}", StringComparison.Ordinal))
            {
                return Json(UserJson("Ana", "Horvat", "ana.horvat@example.test"));
            }

            if (url.Contains($"/users/{MarkoId:D}", StringComparison.Ordinal))
            {
                return Json(UserJson("Marko", "Kovac", "marko.kovac@example.test"));
            }

            if (url.Contains("applications(appId=", StringComparison.Ordinal))
            {
                return Json(new Dictionary<string, object?>
                {
                    ["passwordCredentials"] = new[]
                    {
                        new { endDateTime = "2026-12-01T00:00:00Z" },
                        new { endDateTime = "2027-06-30T00:00:00Z" },
                    },
                });
            }

            return Json(new Dictionary<string, object?>(), HttpStatusCode.NotFound);
        };
    }

    private static bool IsToken(string url) => url.StartsWith(LoginHost, StringComparison.Ordinal);

    private static int CountOf(ScriptedHttpHandler handler, string fragment) =>
        handler.Requests.Count(request => request.Url.AbsoluteUri.Contains(fragment, StringComparison.Ordinal));

    [Fact]
    public async Task ReadDirectoryAsync_ReadsTheRolesAssignmentsUsersAndTheNewestSecretEnd()
    {
        var (client, handler) = Create(Directory());

        var snapshot = await client.ReadDirectoryAsync();

        Assert.Equal(
            new[] { new MicrosoftAppRole(AdminRoleId, "Admin"), new MicrosoftAppRole(SupportRoleId, "Support") },
            snapshot.AppRoles);
        Assert.Contains(snapshot.Assignments, a => a.PrincipalId == AnaId && a.PrincipalType == "User" && a.AppRoleId == SupportRoleId);
        Assert.Equal(new MicrosoftUser(AnaId, "Ana", "Horvat", "ana.horvat@example.test", true), snapshot.Users[AnaId]);
        Assert.Equal(new DateTimeOffset(2027, 6, 30, 0, 0, 0, TimeSpan.Zero), snapshot.ClientSecretEndsUtc);

        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && IsToken(r.Url.AbsoluteUri));
        Assert.All(handler.Requests.Where(r => !IsToken(r.Url.AbsoluteUri)), r => Assert.Equal("Bearer " + AccessToken, r.Authorization));
    }

    [Fact]
    public async Task ReadDirectoryAsync_FollowsEveryPageOfAssignments()
    {
        var (client, _) = Create(Directory());

        var snapshot = await client.ReadDirectoryAsync();

        Assert.Equal(new[] { AnaId, MarkoId }, snapshot.Assignments.Select(a => a.PrincipalId));
    }

    [Theory]
    [InlineData("https://evil.example.test/steal?$skiptoken=page2")]
    [InlineData("http://graph.microsoft.com/v1.0/x?$skiptoken=page2")]
    [InlineData("https://graph.microsoft.com.evil.example.test/x?$skiptoken=page2")]
    public async Task A_next_link_that_is_not_https_on_Graph_is_refused_and_not_followed(string nextLink)
    {
        var (client, handler) = Create(Directory(nextLink));

        var error = await Assert.ThrowsAsync<MicrosoftGraphException>(() => client.ReadDirectoryAsync());

        Assert.Equal(DirectorySyncFailure.Response, error.Failure);
        Assert.DoesNotContain(handler.Requests, r => r.Url.AbsoluteUri.Contains("evil.example.test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refused_user_read_is_a_Graph_failure_with_its_status_and_no_secret_in_the_message()
    {
        var directory = Directory();
        var (client, _) = Create(request => request.RequestUri!.AbsoluteUri.Contains("/users/", StringComparison.Ordinal)
            ? Json(new Dictionary<string, object?> { ["error"] = new { code = "Forbidden", message = "Insufficient privileges" } }, HttpStatusCode.Forbidden)
            : directory(request));

        var error = await Assert.ThrowsAsync<MicrosoftGraphException>(() => client.ReadDirectoryAsync());

        Assert.Equal(DirectorySyncFailure.Graph, error.Failure);
        Assert.Equal(403, error.HttpStatus);
        Assert.DoesNotContain(FakeSecret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessToken, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_token_is_a_token_failure_and_nothing_else_is_asked()
    {
        var directory = Directory();
        var (client, handler) = Create(request => IsToken(request.RequestUri!.AbsoluteUri)
            ? Json(new Dictionary<string, object?> { ["error"] = "invalid_client" }, HttpStatusCode.Unauthorized)
            : directory(request));

        var error = await Assert.ThrowsAsync<MicrosoftGraphException>(() => client.ReadDirectoryAsync());

        Assert.Equal(DirectorySyncFailure.Token, error.Failure);
        Assert.Equal(401, error.HttpStatus);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_token_answer_without_an_access_token_is_a_token_failure()
    {
        var directory = Directory();
        var (client, _) = Create(request => IsToken(request.RequestUri!.AbsoluteUri)
            ? Json(new Dictionary<string, object?> { ["token_type"] = "Bearer" })
            : directory(request));

        var error = await Assert.ThrowsAsync<MicrosoftGraphException>(() => client.ReadDirectoryAsync());

        Assert.Equal(DirectorySyncFailure.Token, error.Failure);
    }

    [Fact]
    public async Task A_token_answer_that_is_not_json_is_a_token_failure()
    {
        var directory = Directory();
        var (client, _) = Create(request => IsToken(request.RequestUri!.AbsoluteUri)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not json</html>", Encoding.UTF8, "text/html") }
            : directory(request));

        var error = await Assert.ThrowsAsync<MicrosoftGraphException>(() => client.ReadDirectoryAsync());

        Assert.Equal(DirectorySyncFailure.Token, error.Failure);
    }

    [Fact]
    public async Task A_timeout_is_a_timeout_failure()
    {
        var handler = new ScriptedHttpHandler(_ => throw new InvalidOperationException("Not reached."))
        {
            ThrowOnSend = new TaskCanceledException("The request timed out."),
        };
        var options = new MicrosoftSignInOptions { TenantId = Tenant, ClientId = Client, ClientSecret = FakeSecret };
        var client = new MicrosoftGraphClient(new HttpClient(handler), options, new RecordingWaits());

        var error = await Assert.ThrowsAsync<MicrosoftGraphException>(() => client.ReadDirectoryAsync());

        Assert.Equal(DirectorySyncFailure.Timeout, error.Failure);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_from_Graph_is_a_response_failure()
    {
        var directory = Directory();
        var (client, _) = Create(request => request.RequestUri!.AbsoluteUri.Contains("servicePrincipals(appId=", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ not json", Encoding.UTF8, "application/json") }
            : directory(request));

        var error = await Assert.ThrowsAsync<MicrosoftGraphException>(() => client.ReadDirectoryAsync());

        Assert.Equal(DirectorySyncFailure.Response, error.Failure);
    }

    [Fact]
    public async Task A_throttled_read_waits_Retry_After_once_and_then_succeeds()
    {
        var waits = new RecordingWaits();
        var directory = Directory();
        var calls = 0;
        var (client, handler) = Create(request =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("servicePrincipals(appId=", StringComparison.Ordinal) && calls++ == 0)
            {
                return Throttled(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(7));
            }

            return directory(request);
        }, waits);

        var snapshot = await client.ReadDirectoryAsync();

        Assert.Equal(new[] { TimeSpan.FromSeconds(7) }, waits.Waits);
        Assert.Equal(2, CountOf(handler, "servicePrincipals(appId="));
        Assert.Equal(2, snapshot.Assignments.Count);
    }

    [Fact]
    public async Task A_second_throttle_is_a_Graph_failure_with_its_status_after_one_wait()
    {
        var waits = new RecordingWaits();
        var directory = Directory();
        var (client, handler) = Create(request => request.RequestUri!.AbsoluteUri.Contains("servicePrincipals(appId=", StringComparison.Ordinal)
            ? Throttled(HttpStatusCode.ServiceUnavailable, TimeSpan.FromSeconds(3))
            : directory(request), waits);

        var error = await Assert.ThrowsAsync<MicrosoftGraphException>(() => client.ReadDirectoryAsync());

        Assert.Equal(DirectorySyncFailure.Graph, error.Failure);
        Assert.Equal(503, error.HttpStatus);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3) }, waits.Waits);
        Assert.Equal(2, CountOf(handler, "servicePrincipals(appId="));
    }

    [Fact]
    public async Task A_Retry_After_longer_than_a_minute_is_capped_at_a_minute()
    {
        var waits = new RecordingWaits();
        var directory = Directory();
        var calls = 0;
        var (client, _) = Create(request =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("servicePrincipals(appId=", StringComparison.Ordinal) && calls++ == 0)
            {
                return Throttled(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(300));
            }

            return directory(request);
        }, waits);

        await client.ReadDirectoryAsync();

        Assert.Equal(new[] { MicrosoftGraphClient.MaxRetryWait }, waits.Waits);
    }

    [Fact]
    public async Task A_throttled_answer_without_Retry_After_waits_the_default()
    {
        var waits = new RecordingWaits();
        var directory = Directory();
        var calls = 0;
        var (client, _) = Create(request =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("servicePrincipals(appId=", StringComparison.Ordinal) && calls++ == 0)
            {
                return Throttled(HttpStatusCode.TooManyRequests, null);
            }

            return directory(request);
        }, waits);

        await client.ReadDirectoryAsync();

        Assert.Equal(new[] { MicrosoftGraphClient.DefaultRetryWait }, waits.Waits);
    }

    [Fact]
    public async Task More_than_twenty_pages_of_assignments_is_a_Response_failure_after_twenty_requests()
    {
        // Every page links to a next page: the read must stop at the cap, not follow the links for ever.
        var (client, handler) = Create(Directory(
            $"{GraphBase}servicePrincipals/{ServicePrincipalId:D}/appRoleAssignedTo?$skiptoken=again"));

        var error = await Assert.ThrowsAsync<MicrosoftGraphException>(() => client.ReadDirectoryAsync());

        Assert.Equal(DirectorySyncFailure.Response, error.Failure);
        Assert.Contains("too many pages", error.Message, StringComparison.Ordinal);
        Assert.Equal(MicrosoftGraphClient.MaxPages, CountOf(handler, "appRoleAssignedTo"));
    }

    [Fact]
    public async Task The_secret_is_sent_only_to_the_token_endpoint_and_never_in_a_Graph_address()
    {
        var (client, handler) = Create(Directory());

        await client.ReadDirectoryAsync();

        Assert.DoesNotContain(handler.Requests, r => r.Url.AbsoluteUri.Contains(FakeSecret, StringComparison.Ordinal) && !IsToken(r.Url.AbsoluteUri));
        Assert.Contains(handler.Requests, r => IsToken(r.Url.AbsoluteUri) && r.Body!.Contains("client_secret=", StringComparison.Ordinal));
    }
}
