// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Ballast.Errors;

/// <summary>
/// Reads the problem fields out of an HTTP answer. It accepts the RFC 9457 problem JSON that <see cref="Problem"/> describes,
/// and the shapes that earlier APIs wrote, so a client keeps working against a server that has not been updated.
/// </summary>
public static class ProblemReader
{
    /// <summary>
    /// Reads the problem fields from an answer's body.
    /// </summary>
    /// <param name="response">The answer to read. Its body is read once.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>
    /// The fields the body carries, or null when it carries none. Null is returned when the body is empty, is not JSON, is
    /// neither a JSON object nor a JSON string, or has none of the members below as a string. Null means the answer has no
    /// problem fields. It does not mean that the request succeeded.
    /// </returns>
    /// <remarks>
    /// <para>Only a JSON string value is read. The members are read as follows:</para>
    /// <list type="bullet">
    /// <item><c>code</c>, <c>detail</c> and <c>traceId</c> are read as they are.</item>
    /// <item>
    /// <c>title</c> is read as it is. When it is absent or is not a string, <c>message</c> is read as the title, and when
    /// that is absent too, <c>error</c> is.
    /// </item>
    /// <item>A body that is a bare JSON string is read as the title, with no code.</item>
    /// </list>
    /// <para>
    /// A code is never taken from the title, and a title is never taken from a code. The reader translates nothing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled while the body was read.</exception>
    public static async Task<ProblemInfo?> TryReadAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return Read(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static ProblemInfo? Read(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.String)
        {
            var bare = root.GetString();
            return string.IsNullOrWhiteSpace(bare) ? null : new ProblemInfo(null, bare);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var code = StringMember(root, "code");
        var title = StringMember(root, "title") ?? StringMember(root, "message") ?? StringMember(root, "error");
        var detail = StringMember(root, "detail");
        var traceId = StringMember(root, "traceId");

        return code is null && title is null && detail is null && traceId is null
            ? null
            : new ProblemInfo(code, title, detail, traceId);
    }

    private static string? StringMember(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
