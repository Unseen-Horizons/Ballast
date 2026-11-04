// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth;

/// <summary>
/// The app's Microsoft staff profile hook (<see cref="IMicrosoftStaffProfile{TUser,TKey}"/>) threw. The change the hook was part of is rolled back,
/// so nothing of it is saved. <see cref="Exception.InnerException"/> is the hook's own exception. The directory sync catches this per person and
/// lists that person; a sign-in lets it reach the app, which answers its own error.
/// </summary>
public sealed class MicrosoftStaffProfileException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="subject">The person the change was for: the address, or the account's key when there is no address.</param>
    /// <param name="inner">The hook's exception.</param>
    public MicrosoftStaffProfileException(string subject, Exception inner)
        : base($"The Microsoft staff profile hook failed for {subject}. Its change was rolled back.", inner)
    {
        Subject = subject;
    }

    /// <summary>The person the change was for: the address, or the account's key when there is no address.</summary>
    public string Subject { get; }
}
