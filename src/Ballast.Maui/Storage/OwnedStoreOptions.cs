// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Storage;

/// <summary>
/// How one owner-sealed store lays out its files. Every name is an option: the library has no folder or extension of its own.
/// </summary>
/// <remarks>
/// The names are part of the device's stored data. Changing one makes an existing install's files unreadable to the new code, so
/// a store keeps the names it shipped with.
/// </remarks>
public sealed record OwnedStoreOptions
{
    /// <summary>
    /// The folder under the app's data folder that holds this store, for example <c>tickets</c>. Required; a name that is blank or
    /// contains a path separator is refused by the store's constructor.
    /// </summary>
    public required string FolderName { get; init; }

    /// <summary>
    /// The extension of every sealed file, with its dot, for example <c>.bin</c>. The default is <c>.bin</c>.
    /// </summary>
    public string FileExtension { get; init; } = ".bin";

    /// <summary>
    /// How many hexadecimal digits of the owner key's SHA-256 name the owner's folder. The default is 32, which is what existing
    /// installs use. Must be from 16 to 64.
    /// </summary>
    public int OwnerDigits { get; init; } = 32;
}
