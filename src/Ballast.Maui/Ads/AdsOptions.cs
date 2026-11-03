// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Ballast.Maui.Ads;

/// <summary>
/// How the app's ads are switched on and where their ids come from. Ads are off unless <see cref="Enabled"/> is true, and every
/// value that names the app (the ad unit ids, the pages without ads, the badge text, the divider's colour keys, the log tag) is
/// set here.
/// </summary>
/// <remarks>
/// The rules that decide whether an ad may be requested are in the computed properties, and they are pure: a test sets the flags
/// and reads the answers. The debug and platform facts come in as options, so nothing in the library reads a build symbol.
/// </remarks>
public sealed record AdsOptions
{
    /// <summary>The ads switch. Off by default; a document that does not set it true keeps ads off.</summary>
    public bool Enabled { get; init; }

    /// <summary>The banner's ad unit id, of the form <c>ca-app-pub-</c>, a 16-digit publisher id, a slash and a 10-digit unit id. Empty when not set.</summary>
    public string AndroidBannerAdUnitId { get; init; } = string.Empty;

    /// <summary>The native ad unit id, in the same form. Empty turns the native cards off without touching the banner.</summary>
    public string AndroidNativeAdUnitId { get; init; } = string.Empty;

    /// <summary>
    /// True in a debuggable build. A debug build always uses the ad network's own test units, and never a production id. The app
    /// sets this from its own build configuration.
    /// </summary>
    public bool IsDebugBuild { get; init; }

    /// <summary>
    /// True on a platform where the ad network runs (Android). The platform registration sets it; off elsewhere, where no ad is
    /// ever requested.
    /// </summary>
    public bool PlatformSupportsAds { get; init; }

    /// <summary>
    /// The page types that never show the banner, by type name. Read by the page hook, which does not wrap such a page, and by the
    /// shell's navigation, which takes the banner off it. A renamed page must be renamed here too.
    /// </summary>
    public IReadOnlyList<string> AdFreePageTypeNames { get; init; } = [];

    /// <summary>The small label the native ad card shows, for example "Ad".</summary>
    public string BadgeText { get; init; } = "Ad";

    /// <summary>The colour resource key of the banner's divider in the light theme.</summary>
    public string DividerLightResourceKey { get; init; } = "BorderSubtleLight";

    /// <summary>The colour resource key of the banner's divider in the dark theme.</summary>
    public string DividerDarkResourceKey { get; init; } = "DarkBorder";

    /// <summary>The tag the platform log lines carry, so a release build's logcat can be filtered for the ads.</summary>
    public string LogTag { get; init; } = "Ballast.Maui.Ads";

    /// <summary>
    /// Whether the ads may start: on a platform that runs them, with the switch on, and with either a debug build (which uses the test
    /// units) or a well-formed production banner id.
    /// </summary>
    public bool CanInitialize =>
        PlatformSupportsAds && Enabled && (IsDebugBuild || HasValidProductionBannerId);

    /// <summary>Whether <see cref="AndroidBannerAdUnitId"/> has the form of an ad unit id.</summary>
    public bool HasValidProductionBannerId => AdUnitIdSyntax.IsValidBannerId(AndroidBannerAdUnitId);

    /// <summary>Whether <see cref="AndroidNativeAdUnitId"/> has the form of an ad unit id.</summary>
    public bool HasValidProductionNativeId => AdUnitIdSyntax.IsValidNativeId(AndroidNativeAdUnitId);

    /// <summary>The banner unit to request, or null for the network's default (the test unit in a debug build).</summary>
    public string? EffectiveBannerAdUnitId =>
        IsDebugBuild ? null : HasValidProductionBannerId ? AndroidBannerAdUnitId : null;

    /// <summary>The native unit to request, or null for the network's default (the test unit in a debug build).</summary>
    public string? EffectiveNativeAdUnitId =>
        IsDebugBuild ? null : HasValidProductionNativeId ? AndroidNativeAdUnitId : null;

    /// <summary>
    /// Whether native cards are on: the ads can start, and in a release build a well-formed native unit id is set. A debug build always
    /// uses the test native unit when the ads are on, like the banner.
    /// </summary>
    public bool IsNativeEnabled =>
        CanInitialize && (IsDebugBuild || HasValidProductionNativeId);

    /// <summary>The switch and the two ids, read from a parsed document's section. Every other option keeps its default.</summary>
    /// <param name="document">The parsed settings document. Null, a missing section or a wrong shape gives the disabled options.</param>
    /// <param name="sectionName">The section's name in the document, for example <c>Ads</c>.</param>
    /// <returns>The options read from the document. Use <c>with</c> to set the rest.</returns>
    /// <remarks>
    /// A missing or non-string id is empty; a present one is trimmed. Only a JSON <c>true</c> switches the ads on.
    /// </remarks>
    public static AdsOptions Read(JsonDocument? document, string sectionName = "Ads")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        if (document is null || !document.RootElement.TryGetProperty(sectionName, out var section) || section.ValueKind is not JsonValueKind.Object)
            return new AdsOptions();

        var enabled = section.TryGetProperty(nameof(Enabled), out var enabledValue) && enabledValue.ValueKind is JsonValueKind.True;

        return new AdsOptions
        {
            Enabled = enabled,
            AndroidBannerAdUnitId = ReadUnitId(section, nameof(AndroidBannerAdUnitId)),
            AndroidNativeAdUnitId = ReadUnitId(section, nameof(AndroidNativeAdUnitId)),
        };
    }

    private static string ReadUnitId(JsonElement section, string name) =>
        section.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim()
            : string.Empty;
}
