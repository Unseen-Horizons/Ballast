// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.AppUpdates;

/// <summary>
/// What the client learned from one ask of the version endpoint. <see cref="Answered"/> is false only for a failure (offline, a
/// timeout, an error status, an answer with no usable build number), and then the last answer the app kept still stands. An answer
/// with <see cref="Info"/> null is a 404: the server has no build numbers for the platform, which means "no minimum".
/// </summary>
/// <param name="Answered">True when the server answered: with build numbers, or with "no minimum" (a 404).</param>
/// <param name="Info">The server's build numbers, or null for "no minimum" or for a failure.</param>
public readonly record struct AppVersionAnswer(bool Answered, AppVersionResponse? Info);
