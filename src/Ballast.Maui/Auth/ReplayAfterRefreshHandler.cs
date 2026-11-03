// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Net;

namespace Ballast.Maui.Auth;

/// <summary>
/// The one replay flow the app's two handlers share. A call goes out with the current credential. When the server answers 401
/// and the subclass says that 401 is one to answer (<see cref="IsRefusalAsync"/>), the credential is renewed once and the call is
/// sent again with the same method, address, headers and body. A replayed call is marked, so it is never replayed twice.
/// </summary>
/// <remarks>
/// <para>
/// A body that cannot be read twice (a one-shot stream the first send consumed) cannot be replayed. Sending the wrong bytes would
/// look like a successful upload, so the 401 is returned instead.
/// </para>
/// <para>
/// Whether the renewal runs before that body check is the subclass's choice (<see cref="RenewsBeforeCheckingBody"/>). Both orders
/// keep the call safe. The difference is whether a refused call spends a renewal it cannot use.
/// </para>
/// </remarks>
public abstract class ReplayAfterRefreshHandler : DelegatingHandler
{
    private readonly HttpRequestOptionsKey<bool> _replayedKey;
    private readonly string _traceTag;

    /// <summary>Creates the base.</summary>
    /// <param name="replayedOptionName">The request option that marks a replayed call. Unique to each handler class.</param>
    /// <param name="traceTag">The tag that starts each trace line, for example "[AppSession]".</param>
    protected ReplayAfterRefreshHandler(string replayedOptionName, string traceTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replayedOptionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(traceTag);
        _replayedKey = new HttpRequestOptionsKey<bool>(replayedOptionName);
        _traceTag = traceTag;
    }

    /// <summary>
    /// True renews the credential before the body check, so a call whose body cannot be replayed still renews. False (the default)
    /// checks the body first, and spends no renewal on a call that cannot be replayed.
    /// </summary>
    protected virtual bool RenewsBeforeCheckingBody => false;

    /// <summary>Puts the current credential on the call. Returns it, or null when there is none to send.</summary>
    protected abstract Task<string?> AttachAsync(HttpRequestMessage request, CancellationToken ct);

    /// <summary>
    /// Whether this 401 is one to answer by renewing. <paramref name="sentCredential"/> is what the call carried (null for none).
    /// </summary>
    protected abstract Task<bool> IsRefusalAsync(HttpResponseMessage response, string? sentCredential, CancellationToken ct);

    /// <summary>Renews the credential that was refused. Returns the new one, or null when it could not be renewed.</summary>
    protected abstract Task<string?> RenewAsync(string? rejectedCredential, CancellationToken ct);

    /// <summary>Puts <paramref name="credential"/> on a request, replacing whatever it carried.</summary>
    protected abstract void SetCredential(HttpRequestMessage request, string credential);

    /// <summary>
    /// Answers a call whose renewal failed. The default returns the 401 as it came. A subclass may throw, or answer with a
    /// response of its own, and then it owns the disposal of <paramref name="response"/>.
    /// </summary>
    protected virtual Task<HttpResponseMessage> RenewFailedAsync(HttpRequestMessage request, HttpResponseMessage response, CancellationToken ct) =>
        Task.FromResult(response);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sent = await AttachAsync(request, cancellationToken).ConfigureAwait(false);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        if (request.Options.TryGetValue(_replayedKey, out var replayed) && replayed)
            return response;

        if (!await IsRefusalAsync(response, sent, cancellationToken).ConfigureAwait(false))
            return response;

        var body = RenewsBeforeCheckingBody ? null : await ReadReplayBodyAsync(request, cancellationToken).ConfigureAwait(false);
        if (body is { Replayable: false })
            return response;

        var fresh = await RenewAsync(sent, cancellationToken).ConfigureAwait(false);
        if (fresh is null)
            return await RenewFailedAsync(request, response, cancellationToken).ConfigureAwait(false);

        body ??= await ReadReplayBodyAsync(request, cancellationToken).ConfigureAwait(false);
        if (body is { Replayable: false })
            return response;

        response.Dispose();
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        if (body.Bytes is not null)
        {
            clone.Content = new ByteArrayContent(body.Bytes);
            foreach (var header in request.Content!.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        SetCredential(clone, fresh);
        clone.Options.Set(_replayedKey, true);
        return await base.SendAsync(clone, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The body buffered for a replay, and whether it can be replayed at all.</summary>
    private sealed record ReplayBody(bool Replayable, byte[]? Bytes);

    private async Task<ReplayBody> ReadReplayBodyAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is null)
            return new ReplayBody(true, null);

        try
        {
            return new ReplayBody(true, await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or NotSupportedException)
        {
            Trace.WriteLine($"{_traceTag} The request body cannot be replayed, so a refused call is not retried: {ex.Message}");
            return new ReplayBody(false, null);
        }
    }
}
