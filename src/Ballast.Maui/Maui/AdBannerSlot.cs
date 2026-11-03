// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Threading;

namespace Ballast.Maui.Ads;

/// <summary>
/// The one banner the app shows. <see cref="AdBannerHost"/> owns the single instance and moves it from page to page. A slot per page would build a
/// native banner for every screen.
/// </summary>
/// <remarks>
/// <para>
/// The slot is a frame (<see cref="AdBannerFrame"/>): an 8 dp gap, a 1 dp divider, the ad, and an 8 dp gap before the tab bar. The ad row is sized,
/// as soon as the slot is placed, to the height the network gives an anchored adaptive banner at this screen width, so the page does not jump when the
/// ad arrives. A failed load collapses the slot by height, and the next successful load brings the reserved height back.
/// </para>
/// <para>
/// The banner exists on Android only. Elsewhere the slot is an empty frame, and it never requests an ad.
/// </para>
/// </remarks>
public sealed class AdBannerSlot : Microsoft.Maui.Controls.ContentView
{
    private readonly AdsOptions _options;

#if ANDROID
    private readonly Plugin.AdMob.BannerAd? _banner;
    private readonly Microsoft.Maui.Controls.RowDefinition? _adRow;
    private readonly Microsoft.Maui.Controls.BoxView? _divider;

    /// <summary>The adaptive height reserved before the request, or null on the fallback path.</summary>
    private readonly double? _reservedDp;

    /// <summary>True from a failed request until the next load shows the banner again.</summary>
    private bool _collapsed;

    private static int _loadCount;
    private static int _failCount;
#endif

    private static int _handlerCount;

    /// <summary>Creates the slot.</summary>
    /// <param name="adsService">The app's ad state: whether the banner is on, and its unit.</param>
    /// <param name="options">The ads options: the divider's colour keys and the log tag.</param>
    public AdBannerSlot(IAdsService adsService, AdsOptions options)
    {
        ArgumentNullException.ThrowIfNull(adsService);
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Fill;

        // The slot outlives every page that hosts it. The platform disconnects the handlers of a popped page's whole visual tree, and a slot still
        // inside that page would be torn down and rebuilt, which is a new native view and a new ad request. Manual excludes the slot from that walk.
        HandlerProperties.SetDisconnectPolicy(this, HandlerDisconnectPolicy.Manual);

        // Counters for the device check: a tab switch or a pop must not raise "ad load" or "slot handler" again.
        HandlerChanged += (_, _) =>
        {
            if (Handler is not null)
                LogCounter("slot handler attached", Interlocked.Increment(ref _handlerCount));
        };

#if ANDROID
        if (!adsService.IsEnabled)
            return;

        // SmartBanner is the plugin's only adaptive size: the network's current-orientation anchored adaptive banner.
        _banner = new Plugin.AdMob.BannerAd
        {
            AdSize = Plugin.AdMob.AdSize.SmartBanner,
            HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Fill,
            VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Fill
        };
        if (adsService.BannerAdUnitId is { } bannerAdUnitId)
            _banner.AdUnitId = bannerAdUnitId;

        _banner.OnAdLoaded += OnAdLoaded;
        _banner.OnAdFailedToLoad += OnAdFailedToLoad;

        // The row is sized now, from the adaptive height for this width, so the frame has its full height before the first measure.
        var reserved = AdaptiveHeightDp();
        _reservedDp = reserved;
        _adRow = new Microsoft.Maui.Controls.RowDefinition(new Microsoft.Maui.GridLength(reserved ?? 0));

        var frame = new Microsoft.Maui.Controls.Grid
        {
            RowDefinitions =
            [
                new Microsoft.Maui.Controls.RowDefinition(new Microsoft.Maui.GridLength(AdBannerFrame.GapDp)),
                new Microsoft.Maui.Controls.RowDefinition(new Microsoft.Maui.GridLength(AdBannerFrame.DividerDp)),
                _adRow,
                new Microsoft.Maui.Controls.RowDefinition(new Microsoft.Maui.GridLength(AdBannerFrame.GapDp)),
            ],
        };

        // The divider is a line, not a control: InputTransparent, so nothing in the frame takes a tap but the ad.
        _divider = new Microsoft.Maui.Controls.BoxView
        {
            HeightRequest = AdBannerFrame.DividerDp,
            InputTransparent = true,
            Color = DividerColor(),
        };
        if (Microsoft.Maui.Controls.Application.Current is { } app)
            app.RequestedThemeChanged += OnRequestedThemeChanged;

        frame.Add(_divider, 0, 1);
        frame.Add(_banner, 0, 2);
        Content = frame;

        if (reserved is { } height)
        {
            HeightRequest = AdBannerFrame.Height(height);
            _banner.HeightRequest = height;
        }
        else
        {
            // Height zero until the loaded ad has measured itself; see OnAdLoaded. The view stays visible, so the native view is attached and can load.
            HeightRequest = 0;
            LogLine("[Ads] adaptive banner height unavailable; the slot waits for the loaded ad to size it");
        }
#endif
    }

#if ANDROID
    /// <summary>The divider's colour in the theme in use: the light border in light, the dark border in dark.</summary>
    private Microsoft.Maui.Graphics.Color DividerColor() =>
        ThemeColor(Microsoft.Maui.Controls.Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark
            ? _options.DividerDarkResourceKey
            : _options.DividerLightResourceKey);

    private static Microsoft.Maui.Graphics.Color ThemeColor(string key) =>
        Microsoft.Maui.Controls.Application.Current?.Resources[key] as Microsoft.Maui.Graphics.Color ?? Microsoft.Maui.Graphics.Colors.Transparent;

    private void OnRequestedThemeChanged(object? sender, Microsoft.Maui.Controls.AppThemeChangedEventArgs e)
    {
        if (_divider is not null)
            _divider.Color = DividerColor();
    }

    /// <summary>
    /// The height, in dp, of the anchored adaptive banner the network gives this screen's width. Null when the platform call is unavailable or throws,
    /// which is logged; the slot then sizes itself from the loaded ad.
    /// </summary>
    private double? AdaptiveHeightDp()
    {
        try
        {
            var display = Microsoft.Maui.Devices.DeviceDisplay.Current.MainDisplayInfo;
            if (display.Density <= 0)
            {
                LogLine("[Ads] display density is unknown; the adaptive banner height is not reserved");
                return null;
            }

            var widthDp = (int)(display.Width / display.Density);
            var size = Android.Gms.Ads.AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSize(Android.App.Application.Context, widthDp);
            if (size.Height > 0)
                return size.Height;

            LogLine($"[Ads] adaptive banner for {widthDp} dp has no height; the slot waits for the loaded ad to size it");
        }
        catch (Exception ex)
        {
            LogLine($"[Ads] adaptive banner height unavailable ({ex.Message}); the slot waits for the loaded ad to size it");
        }

        return null;
    }

    private void OnAdLoaded(object? sender, EventArgs e)
    {
        LogCounter("banner loaded", Interlocked.Increment(ref _loadCount));
        if (_banner is null || _adRow is null)
            return;

        // The reserved height, or on the fallback path the height the loaded ad measures.
        double height;
        if (_reservedDp is { } reserved)
        {
            height = reserved;
        }
        else
        {
            height = _banner.Measure(double.PositiveInfinity, double.PositiveInfinity).Height;
            if (height <= 0)
            {
                LogLine("[Ads] banner loaded with no measured height; the slot stays collapsed");
                return;
            }
        }

        // Also the way back after a failed request: the height comes back with the ad.
        var recovering = _collapsed;
        _collapsed = false;
        _adRow.Height = new Microsoft.Maui.GridLength(height);
        _banner.HeightRequest = height;
        HeightRequest = AdBannerFrame.Height(height);

        if (recovering)
            LogLine("[Ads] banner shown again after a failed request");
        else if (_reservedDp is null)
            LogLine($"[Ads] banner sized from the loaded ad ({height} dp), the fallback path");
    }

    private void OnAdFailedToLoad(object? sender, Plugin.AdMob.IAdError e)
    {
        Collapse();
        LogCounter("banner request failed; the slot remains collapsed", Interlocked.Increment(ref _failCount));
    }

    /// <summary>
    /// Collapses by height only. The view stays visible and attached: the network does not refresh a view that is not shown, so hiding it would keep
    /// the banner away for the rest of the session after one no-fill.
    /// </summary>
    private void Collapse()
    {
        _collapsed = true;
        if (_adRow is not null)
            _adRow.Height = new Microsoft.Maui.GridLength(0);
        if (_banner is not null)
            _banner.HeightRequest = 0;
        HeightRequest = 0;
    }
#endif

    private void LogCounter(string what, int count) => LogLine($"[Ads] {what} (#{count})");

    private void LogLine(string line)
    {
        // The same tag labels both sinks: the trace on every target, and logcat on Android.
        Trace.WriteLine($"{_options.LogTag}: {line}");
#if ANDROID
        Android.Util.Log.Info(_options.LogTag, line);
#endif
    }
}
