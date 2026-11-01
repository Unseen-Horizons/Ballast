// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel.DataAnnotations;

namespace Ballast.AspNetCore;

/// <summary>
/// Every string in a list property is at most <see cref="Max"/> characters long. DataAnnotations has no per-item length check,
/// so this is the check for a list of strings. A null list or a null item passes, and so does a value that is not a list of
/// strings. See the usage guide, docs/AspNetCore.md, for the rules and for the placement on a property.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class EachStringLengthAttribute(int max) : ValidationAttribute
{
    /// <summary>The longest a string in the list may be, in characters.</summary>
    public int Max { get; } = max;

    /// <inheritdoc/>
    public override bool IsValid(object? value) =>
        value is not IEnumerable<string?> items || items.All(item => item is null || item.Length <= Max);
}
