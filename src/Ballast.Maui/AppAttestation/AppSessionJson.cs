// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Ballast.Maui.AppAttestation;

/// <summary>The session request the device sends. Its JSON names match the server's <c>CreateAppSessionRequest</c>.</summary>
/// <param name="Platform">The platform name.</param>
/// <param name="InstallId">The random id kept for this install.</param>
/// <param name="Nonce">The nonce the server issued, which the platform token answers.</param>
/// <param name="IntegrityToken">The platform token, or null when the platform gave none.</param>
public sealed record AppSessionRequestDto(string Platform, string InstallId, string Nonce, string? IntegrityToken);

/// <summary>The server's nonce answer. Its JSON name matches the server's <c>AppNonceResponse</c>.</summary>
public sealed class AppNonceDto
{
    /// <summary>The nonce. Empty when the server sent none.</summary>
    public string Nonce { get; set; } = string.Empty;
}

/// <summary>The server's session answer. Its JSON names match the server's <c>AppSessionResponse</c>.</summary>
public sealed class AppSessionDto
{
    /// <summary>The session token. Empty when the server sent none.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>How long the session lasts, in seconds. Zero when the server sent none.</summary>
    public int ExpiresInSeconds { get; set; }
}

/// <summary>The server's web-ticket answer. Its JSON name matches the server's <c>AppWebTicketResponse</c>.</summary>
public sealed class AppWebTicketDto
{
    /// <summary>The one-use ticket. Empty when the server sent none.</summary>
    public string Ticket { get; set; } = string.Empty;
}

/// <summary>
/// The JSON the app session reads and writes: camelCase names, source-generated for a trimmed app. The names are the server's, so
/// a change on either side breaks the parity test.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(AppSessionRequestDto))]
[JsonSerializable(typeof(AppNonceDto))]
[JsonSerializable(typeof(AppSessionDto))]
[JsonSerializable(typeof(AppWebTicketDto))]
internal sealed partial class AppSessionJson : JsonSerializerContext;
