// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

using Ballast.AppUpdates;

namespace Ballast.Maui.AppUpdates;

/// <summary>
/// The JSON the device keeps its last answers in. It is the default naming (PascalCase), the form earlier builds wrote, so an upgrade
/// reads what was stored. The server's own wire format is web (camelCase) and is read by <see cref="AppVersionClient"/>.
/// </summary>
[JsonSerializable(typeof(AppVersionResponse))]
[JsonSerializable(typeof(StoreUpdate))]
internal sealed partial class AppVersionStoreJson : JsonSerializerContext;
