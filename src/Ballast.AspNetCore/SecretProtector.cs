// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Ballast.AspNetCore;

/// <summary>
/// Encrypts the credentials an application stores, so a database read is not a credential read. Each stored value carries
/// the <c>enc:v1:</c> marker, so ciphertext is never confused with a value written before protection existed.
/// <para>
/// A credential is encrypted where it is written and decrypted where it is spent, not in an EF value converter. A converter
/// runs inside the model and decrypts on every read, including reads that only ask whether a credential exists.
/// </para>
/// <para>
/// <b>Key management matters.</b> The data-protection key ring must be shared by every copy of the application that reads
/// the values, and kept across a rebuild of the host. Without it an authenticated call fails visibly. A credential is never
/// dropped silently: the default mode throws, and <see cref="SecretUnreadableMode.ReadAsNull"/> returns null for the caller
/// to refuse or warn about.
/// </para>
/// </summary>
public sealed class SecretProtector
{
    /// <summary>Marks a value this class produced. Without it there is no way to tell ciphertext from a plain value.</summary>
    private const string Prefix = "enc:v1:";

    private readonly IDataProtector _protector;
    private readonly ILogger<SecretProtector> _logger;
    private readonly SecretUnreadableMode _unreadableMode;

    // The sources whose unreadable credential was already logged by UnprotectForRead.
    private readonly ConcurrentDictionary<string, byte> _unreadableLogged = new(StringComparer.Ordinal);

    /// <summary>Creates the protector for the purpose in <paramref name="options"/>.</summary>
    /// <param name="provider">The data-protection provider. Required.</param>
    /// <param name="logger">The logger. Required.</param>
    /// <param name="options">The purpose and the unreadable mode. Required.</param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    /// <exception cref="ArgumentException"><see cref="SecretProtectorOptions.Purpose"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="SecretProtectorOptions.UnreadableMode"/> is not a defined value.</exception>
    public SecretProtector(IDataProtectionProvider provider, ILogger<SecretProtector> logger, SecretProtectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Purpose, nameof(options));
        if (!Enum.IsDefined(options.UnreadableMode))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.UnreadableMode, "The unreadable mode is not a defined value.");
        }

        // The purpose scopes the key: a payload protected for this purpose cannot be unprotected by a protector with another one.
        _protector = provider.CreateProtector(options.Purpose);
        _logger = logger;
        _unreadableMode = options.UnreadableMode;
    }

    /// <summary>
    /// Encrypts a credential for storage. Null and blank pass through unchanged, because "no credential" is a legitimate state.
    /// A value that already carries the marker is returned unchanged, so a round trip never double-wraps it.
    /// </summary>
    /// <param name="plaintext">The credential, or null or blank.</param>
    /// <returns>The stored form.</returns>
    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext)) return plaintext;

        if (plaintext.StartsWith(Prefix, StringComparison.Ordinal)) return plaintext;

        return Prefix + _protector.Protect(plaintext);
    }

    /// <summary>
    /// Decrypts a stored credential. A value without the marker is returned as it is, since nothing this class writes lacks it.
    /// </summary>
    /// <param name="stored">The stored value, or null or blank.</param>
    /// <returns>
    /// The credential, or null or blank when there was none. When the stored value cannot be decrypted, this throws
    /// <see cref="CredentialUnavailableException"/> in <see cref="SecretUnreadableMode.Throw"/>, and returns null in
    /// <see cref="SecretUnreadableMode.ReadAsNull"/>.
    /// </returns>
    /// <exception cref="CredentialUnavailableException">The value cannot be decrypted, and the mode is <see cref="SecretUnreadableMode.Throw"/>.</exception>
    public string? Unprotect(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;

        if (TryDecrypt(stored, out var plaintext)) return plaintext;

        if (_unreadableMode == SecretUnreadableMode.Throw) throw new CredentialUnavailableException();
        return null;
    }

    /// <summary>
    /// Decrypts one source's stored credential for one read. It never throws for an unreadable credential: the read goes on
    /// without it, and <paramref name="warning"/> says so.
    /// <para>
    /// The warning names <paramref name="sourceName"/> and never the value. The log line is written once per source until its
    /// credential decrypts again, so a source polled every few seconds does not repeat it. A blank or absent credential gives
    /// no warning.
    /// </para>
    /// </summary>
    /// <param name="stored">The stored value, or null or blank.</param>
    /// <param name="sourceName">The name of the source the credential belongs to, for the warning and the log. Required.</param>
    /// <param name="warning">Set when the credential exists but cannot be decrypted; otherwise null.</param>
    /// <returns>The credential, or null when there is none or it cannot be decrypted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sourceName"/> is null.</exception>
    public string? UnprotectForRead(string? stored, string sourceName, out string? warning)
    {
        ArgumentNullException.ThrowIfNull(sourceName);
        warning = null;
        if (string.IsNullOrWhiteSpace(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            _unreadableLogged.TryRemove(sourceName, out _);
            return stored;
        }

        if (TryDecrypt(stored, out var plaintext))
        {
            _unreadableLogged.TryRemove(sourceName, out _);
            return plaintext;
        }

        warning = $"{sourceName}: its stored credential could not be decrypted, so the request was sent without it. Re-enter the credential on the source.";
        if (_unreadableLogged.TryAdd(sourceName, 0))
        {
            _logger.LogWarning(
                "Source {SourceName}: its stored credential could not be decrypted; requests are sent without it until it is re-entered.",
                sourceName);
        }

        return null;
    }

    private bool TryDecrypt(string stored, out string? plaintext)
    {
        try
        {
            plaintext = _protector.Unprotect(stored[Prefix.Length..]);
            return true;
        }
        catch (CryptographicException ex)
        {
            // The key ring that wrote this is gone or unreadable, or the value was written under another purpose. The value is never logged.
            _logger.LogError(
                ex,
                "A stored credential could not be decrypted. The data-protection key ring that wrote it is unavailable, or it was written under another purpose; re-enter the credential on its source.");
            plaintext = null;
            return false;
        }
    }

    /// <summary>
    /// A stored credential exists but cannot be read back. Its message is safe to show an administrator: it names no value.
    /// </summary>
    public sealed class CredentialUnavailableException()
        : Exception("The stored credential could not be decrypted. Re-enter it on its source; the request was not sent without it.");
}
