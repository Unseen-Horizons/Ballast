// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace Ballast.Maui.AppUpdates;

/// <summary>
/// The build number the app stamps into its assembly as a metadata attribute (<c>AssemblyMetadata</c> in the project file). The
/// update check compares it with the server's numbers, so it must be a plain positive whole number.
/// </summary>
public static class AppBuildStamp
{
    /// <summary>The build number from the assembly's metadata, or null when there is none it can use (the reason is logged).</summary>
    /// <param name="attributes">The assembly's metadata attributes.</param>
    /// <param name="metadataKey">The key the project file stamps the build under.</param>
    public static int? FromAttributes(IEnumerable<AssemblyMetadataAttribute> attributes, string metadataKey)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataKey);
        var text = attributes.FirstOrDefault(a => a.Key == metadataKey)?.Value;
        if (TryParse(text, out var build))
            return build;

        Trace.WriteLine($"[AppBuild] The assembly carries no usable '{metadataKey}' (value '{text}').");
        return null;
    }

    /// <summary>True when the text is a positive whole number (surrounding spaces are ignored).</summary>
    /// <param name="text">The text.</param>
    /// <param name="build">The number, or zero when it is not one.</param>
    public static bool TryParse(string? text, out int build)
    {
        build = 0;
        return int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out build) && build > 0;
    }
}
