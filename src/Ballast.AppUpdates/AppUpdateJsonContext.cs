// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ballast.AppUpdates;

/// <summary>
/// The source-generated JSON for the version answer. It reads and writes the same names and defaults as the web defaults
/// (<see cref="JsonSerializerDefaults.Web"/>), without reflection, so the library stays trimmable and AOT-compatible.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AppVersionResponse))]
internal sealed partial class AppUpdateJsonContext : JsonSerializerContext
{
}
