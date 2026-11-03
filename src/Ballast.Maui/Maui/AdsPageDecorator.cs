// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Handlers;

namespace Ballast.Maui.Ads;

/// <summary>
/// Gives each page a row for the app's one banner, before the page's first measure.
/// </summary>
/// <remarks>
/// The wrap happens when the page's handler is first created (<see cref="RegisterPageHook"/>, once, from the app's start-up), which is after the
/// page's own constructor has set its content and before anything is measured. <see cref="Attach"/>, called on each navigation, only moves the banner
/// into the row. A page that derives from another that already wraps its content hands its own grid over through <see cref="Adopt"/>, so such a page
/// is wrapped once, not twice.
/// </remarks>
public static class AdsPageDecorator
{
    private static readonly BindableProperty BannerGridProperty = BindableProperty.CreateAttached(
        "BannerGrid",
        typeof(Grid),
        typeof(AdsPageDecorator),
        null);

    private static readonly BindableProperty BannerRowProperty = BindableProperty.CreateAttached(
        "BannerRow",
        typeof(int),
        typeof(AdsPageDecorator),
        0);

    private static readonly BindableProperty WrapperProperty = BindableProperty.CreateAttached(
        "Wrapper",
        typeof(ContentWrapper<View>),
        typeof(AdsPageDecorator),
        null);

    private static IReadOnlyList<string> _adFreePageTypeNames = [];

    /// <summary>
    /// True for a page that never shows the banner: a page whose type name is in the list given to <see cref="RegisterPageHook"/>. Read by the page
    /// hook (which does not wrap such a page) and by the shell's navigation (which takes the banner off it).
    /// </summary>
    /// <param name="page">The page, or null.</param>
    /// <returns>True for an ad-free page; false for null and for every other page.</returns>
    public static bool IsAdFree(Page? page) => page is not null && _adFreePageTypeNames.Contains(page.GetType().Name);

    /// <summary>
    /// Wraps every content page except the ad-free ones as its handler is created, before its native content is built. Call once, at start-up. The
    /// hook does nothing when ads are off or the page has no content yet (<see cref="Attach"/> then wraps it as a fallback).
    /// </summary>
    /// <param name="options">The ads options. Their <see cref="AdsOptions.AdFreePageTypeNames"/> name the pages that never show the banner.</param>
    public static void RegisterPageHook(AdsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _adFreePageTypeNames = options.AdFreePageTypeNames.ToList();

        PageHandler.Mapper.PrependToMapping(nameof(IContentView.Content), (handler, view) =>
        {
            if (view is not ContentPage page || IsAdFree(page))
                return;
            if (handler.MauiContext?.Services.GetService<AdBannerHost>() is not { IsEnabled: true })
                return;

            EnsureWrapped(page);
        });
    }

    /// <summary>
    /// For a page that builds its own wrapper grid: records the grid and the row of it that is left for the banner, so the page is not wrapped a
    /// second time.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="layout">The page's own grid.</param>
    /// <param name="bannerRow">The row of <paramref name="layout"/> that the banner takes.</param>
    public static void Adopt(ContentPage page, Grid layout, int bannerRow)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(layout);
        page.SetValue(BannerGridProperty, layout);
        page.SetValue(BannerRowProperty, bannerRow);
    }

    /// <summary>
    /// Moves the app's one shared banner into the page's banner row. Called on every navigation; it never re-parents the page's content. A page that
    /// was not wrapped when its handler was created is wrapped here, and the fact is traced, so a silent regression of the early path shows.
    /// </summary>
    /// <param name="page">The page the person is on.</param>
    /// <param name="host">The app's one banner.</param>
    public static void Attach(ContentPage page, AdBannerHost host)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(host);
        if (!host.IsEnabled)
            return;

        if (page.GetValue(BannerGridProperty) is not Grid layout)
        {
            System.Diagnostics.Trace.WriteLine($"[Ads] {page.GetType().Name} was not wrapped before its first layout; wrapping it now.");
            if (!EnsureWrapped(page))
                return;
            layout = (Grid)page.GetValue(BannerGridProperty);
        }

        host.MoveTo(layout, (int)page.GetValue(BannerRowProperty));
    }

    /// <summary>
    /// Wraps the page's content in a grid with an automatic row for the banner, once. False when there is nothing to wrap yet.
    /// </summary>
    /// <remarks>
    /// Every assignment to the page's content makes the page handler map the content again, which calls this method again. A re-entered call is a
    /// no-op: the banner grid is recorded before the wrapper is assigned, and <see cref="ContentWrapper{TContent}"/> also returns at once while its own
    /// wrap is running.
    /// </remarks>
    private static bool EnsureWrapped(ContentPage page)
    {
        if (page.GetValue(BannerGridProperty) is Grid)
            return true;

        if (page.GetValue(WrapperProperty) is not ContentWrapper<View> wrapper)
        {
            wrapper = new ContentWrapper<View>();
            page.SetValue(WrapperProperty, wrapper);
        }

        return wrapper.EnsureWrapped(
            () => page.Content,
            content => page.Content = content,
            original =>
            {
                var layout = new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition(GridLength.Star),
                        new RowDefinition(GridLength.Auto)
                    }
                };
                layout.Add(original);

                // Recorded before the wrapper becomes the page's content, so the mapping that assignment triggers already finds the page wrapped.
                Adopt(page, layout, bannerRow: 1);
                return layout;
            });
    }
}
