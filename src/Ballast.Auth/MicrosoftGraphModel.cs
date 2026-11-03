// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>Why a directory read failed. The read writes nothing when it fails.</summary>
public enum DirectorySyncFailure
{
    /// <summary>Microsoft refused the token request, or its answer had no token.</summary>
    Token,

    /// <summary>A Graph request was refused, or failed at the network.</summary>
    Graph,

    /// <summary>A request to Microsoft timed out.</summary>
    Timeout,

    /// <summary>Microsoft answered with a shape the library does not read, or with more pages than <see cref="MicrosoftGraphClient.MaxPages"/>.</summary>
    Response,

    /// <summary>Something else failed while the directory was written.</summary>
    Unexpected,
}

/// <summary>An app role of this application in the directory.</summary>
/// <param name="Id">The app role's id.</param>
/// <param name="Value">The role value, such as <c>Api.Admin</c>.</param>
public sealed record MicrosoftAppRole(Guid Id, string Value);

/// <summary>One assignment of an app role to a principal.</summary>
/// <param name="PrincipalId">The user or group's id.</param>
/// <param name="PrincipalType">The principal's type: <c>User</c>, <c>Group</c> and so on.</param>
/// <param name="AppRoleId">The app role's id.</param>
public sealed record MicrosoftAssignment(Guid PrincipalId, string PrincipalType, Guid AppRoleId);

/// <summary>The fields of a directory user that the sync reads.</summary>
/// <param name="Id">The user's object id.</param>
/// <param name="GivenName">The given name, or null.</param>
/// <param name="Surname">The family name, or null.</param>
/// <param name="UserPrincipalName">The sign-in name, or null.</param>
/// <param name="AccountEnabled">Whether the account is enabled in the directory.</param>
public sealed record MicrosoftUser(Guid Id, string? GivenName, string? Surname, string? UserPrincipalName, bool AccountEnabled);

/// <summary>One complete read of the directory: the app roles, the assignments, the users, and the end of the newest client secret.</summary>
/// <param name="AppRoles">The app roles.</param>
/// <param name="Assignments">Every assignment.</param>
/// <param name="Users">The users who are assigned, by id.</param>
/// <param name="ClientSecretEndsUtc">When the newest client secret ends, or null when none is listed.</param>
public sealed record MicrosoftDirectorySnapshot(
    IReadOnlyList<MicrosoftAppRole> AppRoles,
    IReadOnlyList<MicrosoftAssignment> Assignments,
    IReadOnlyDictionary<Guid, MicrosoftUser> Users,
    DateTimeOffset? ClientSecretEndsUtc);

/// <summary>A failed directory read. The message names no secret and no token.</summary>
/// <param name="failure">Why it failed.</param>
/// <param name="httpStatus">The HTTP status Microsoft answered with, or null.</param>
/// <param name="message">A sentence for the log.</param>
/// <param name="inner">The exception that caused it, or null.</param>
public sealed class MicrosoftGraphException(DirectorySyncFailure failure, int? httpStatus, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>Why it failed.</summary>
    public DirectorySyncFailure Failure { get; } = failure;

    /// <summary>The HTTP status Microsoft answered with, or null when there was none.</summary>
    public int? HttpStatus { get; } = httpStatus;
}

/// <summary>Reads the directory. An interface, so a test or an app can answer for Microsoft.</summary>
public interface IMicrosoftGraphClient
{
    /// <summary>Reads the app roles, the assignments, the assigned users and the client secret's end. Throws when the read fails.</summary>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="MicrosoftGraphException">The read failed. Nothing has been written.</exception>
    Task<MicrosoftDirectorySnapshot> ReadDirectoryAsync(CancellationToken ct = default);
}
