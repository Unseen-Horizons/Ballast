// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Ballast.Auth;

/// <summary>
/// The password hasher the library registers. It is Identity's own hasher, with two additions that keep a bad stored hash a refusal:
/// a hash in a format Identity does not read is a failed check rather than an exception, and, when
/// <see cref="AuthOptions.LegacyBcryptPasswords"/> is on, a bcrypt hash that matches is reported as
/// <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>, so Identity rewrites it in its own format at the same sign-in.
/// </summary>
/// <typeparam name="TUser">The app's user type.</typeparam>
/// <remarks>
/// A new password is always hashed by Identity. A bcrypt hash is never accepted unless the option is on, so an app that does not use the
/// legacy format cannot be signed into with one.
/// </remarks>
public sealed class AuthPasswordHasher<TUser> : IPasswordHasher<TUser>
    where TUser : class
{
    private readonly PasswordHasher<TUser> _identity;
    private readonly bool _legacyBcrypt;

    /// <summary>Creates the hasher.</summary>
    /// <param name="options">The library's options. Its <see cref="AuthOptions.LegacyBcryptPasswords"/> decides whether bcrypt is read.</param>
    /// <param name="hasherOptions">Identity's password hasher options, so new hashes match the rest of the app's.</param>
    public AuthPasswordHasher(AuthOptions options, IOptions<PasswordHasherOptions> hasherOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        _identity = new PasswordHasher<TUser>(hasherOptions);
        _legacyBcrypt = options.LegacyBcryptPasswords;
    }

    /// <inheritdoc />
    public string HashPassword(TUser user, string password) => _identity.HashPassword(user, password);

    /// <inheritdoc />
    public PasswordVerificationResult VerifyHashedPassword(TUser user, string hashedPassword, string providedPassword)
    {
        if (IsBcrypt(hashedPassword))
        {
            return _legacyBcrypt ? VerifyBcrypt(hashedPassword, providedPassword) : PasswordVerificationResult.Failed;
        }

        try
        {
            return _identity.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
        catch (FormatException)
        {
            // Identity reads its own format only; a stored hash it cannot read is refused like a wrong password, not thrown at the caller.
            return PasswordVerificationResult.Failed;
        }
    }

    private static PasswordVerificationResult VerifyBcrypt(string hash, string password)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash)
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A damaged bcrypt hash is a refusal, the same as a wrong password.
            return PasswordVerificationResult.Failed;
        }
    }

    /// <summary>Whether a stored hash is in bcrypt's format: the version marker of $2a$, $2b$ or $2y$.</summary>
    /// <param name="hash">The stored hash.</param>
    /// <returns>True for a bcrypt hash.</returns>
    internal static bool IsBcrypt(string hash) =>
        hash.StartsWith("$2a$", StringComparison.Ordinal)
        || hash.StartsWith("$2b$", StringComparison.Ordinal)
        || hash.StartsWith("$2y$", StringComparison.Ordinal);
}
