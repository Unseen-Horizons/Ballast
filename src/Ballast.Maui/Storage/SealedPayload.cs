// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;

namespace Ballast.Maui.Storage;

/// <summary>
/// The sealed-file format: AES-256-GCM with a fresh 12-byte nonce for each write, laid out as nonce, then tag, then ciphertext.
/// Pure, so the format is pinned by tests without a device.
/// </summary>
/// <remarks>
/// The tag makes a tampered or truncated file fail to open. A file edited on a rooted device fails here rather than deserialising
/// into a record someone chose the contents of. Text and bytes share one layout, so one key opens both.
/// </remarks>
public static class SealedPayload
{
    /// <summary>The nonce length: the AES-GCM standard.</summary>
    public const int NonceBytes = 12;

    /// <summary>The authentication tag length.</summary>
    public const int TagBytes = 16;

    /// <summary>
    /// Seals text as UTF-8. The layout is nonce, then tag, then ciphertext, and the nonce is fresh for every call.
    /// </summary>
    /// <param name="key">The 32-byte key (any AES-GCM key length also works).</param>
    /// <param name="plaintext">The text to seal.</param>
    /// <returns>The sealed bytes.</returns>
    public static byte[] Seal(byte[] key, string plaintext) => SealBytes(key, Encoding.UTF8.GetBytes(plaintext));

    /// <summary>
    /// Seals bytes that are not text (an image, an original file). The same layout and cipher as <see cref="Seal(byte[], string)"/>.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="plain">The bytes to seal.</param>
    /// <returns>The sealed bytes.</returns>
    public static byte[] SealBytes(byte[] key, byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[NonceBytes + TagBytes + cipher.Length];
        nonce.CopyTo(output, 0);
        tag.CopyTo(output, NonceBytes);
        cipher.CopyTo(output, NonceBytes + TagBytes);
        return output;
    }

    /// <summary>Reverses <see cref="Seal(byte[], string)"/>.</summary>
    /// <param name="key">The key the bytes were sealed under.</param>
    /// <param name="payload">The sealed bytes.</param>
    /// <returns>The text.</returns>
    /// <exception cref="CryptographicException">The file is too short, tampered with, or sealed under another key.</exception>
    public static string Open(byte[] key, byte[] payload) => Encoding.UTF8.GetString(OpenBytes(key, payload));

    /// <summary>Reverses <see cref="SealBytes"/>.</summary>
    /// <param name="key">The key the bytes were sealed under.</param>
    /// <param name="payload">The sealed bytes.</param>
    /// <returns>The plain bytes.</returns>
    /// <exception cref="CryptographicException">The file is too short, tampered with, or sealed under another key.</exception>
    public static byte[] OpenBytes(byte[] key, byte[] payload)
    {
        if (payload.Length < NonceBytes + TagBytes)
            throw new CryptographicException("Sealed file is too short to be valid.");

        var nonce = payload.AsSpan(0, NonceBytes);
        var tag = payload.AsSpan(NonceBytes, TagBytes);
        var cipher = payload.AsSpan(NonceBytes + TagBytes);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
