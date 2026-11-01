// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Storage;

/// <summary>A recognised file type: its kind, its media type, and the extension a stored file of that type takes.</summary>
/// <param name="Kind">The kind of file.</param>
/// <param name="MediaType">The media type, such as <c>image/jpeg</c>.</param>
/// <param name="Extension">The extension without the dot, such as <c>jpg</c>.</param>
public readonly record struct FileKindInfo(FileKind Kind, string MediaType, string Extension);
