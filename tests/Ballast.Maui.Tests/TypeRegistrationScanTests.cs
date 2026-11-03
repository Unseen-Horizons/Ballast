// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Helpers;
using Ballast.Maui.Tests.ScanFixture.Other;
using Ballast.Maui.Tests.ScanFixture.Pages;
using Ballast.Maui.Tests.ScanFixture.ViewModels;

namespace Ballast.Maui.Tests;

/// <summary>
/// An app's registration scan: the concrete pages and view models are picked out of the type list in one pass, the same as the two passes an app used to make.
/// The fixed type list and the real assembly are both run, and compared with the previous logic copied verbatim below.
/// </summary>
public sealed class TypeRegistrationScanTests
{
    private const string PageNs = "Ballast.Maui.Tests.ScanFixture.Pages";
    private const string VmNs = "Ballast.Maui.Tests.ScanFixture.ViewModels";

    // The previous selection, one pass per kind.
    private static List<Type> PreviousPages(IEnumerable<Type> all) => all
        .Where(t => t.Namespace == PageNs
                 && t.IsClass
                 && !t.IsAbstract
                 && t.IsSubclassOf(typeof(FixturePageBase)))
        .ToList();

    private static List<Type> PreviousViewModels(IEnumerable<Type> all) => all
        .Where(t => t.Namespace == VmNs
                 && t.IsClass
                 && !t.IsAbstract
                 && t.IsSubclassOf(typeof(FixtureViewModelBase)))
        .ToList();

    private static Type[] FixedTypes() =>
    [
        typeof(HomeFixturePage),
        typeof(TripFixturePage),
        typeof(AbstractFixturePage),            // abstract: skipped
        typeof(NotAPage),                       // not a page: skipped
        typeof(IFixturePageMarker),             // interface: skipped
        typeof(FixturePageBase),                // the base itself is not its own subclass: skipped
        typeof(HomeFixtureViewModel),
        typeof(TripFixtureViewModel),
        typeof(AbstractFixtureViewModel),       // abstract: skipped
        typeof(FixtureViewModelBase),           // the base itself: skipped
        typeof(PlainHelper),                    // not a view model: skipped
        typeof(WrongNamespacePage),             // right base, wrong namespace: skipped
        typeof(WrongNamespaceViewModel),
    ];

    private static (List<Type> Pages, List<Type> ViewModels) Scan(IEnumerable<Type> types) =>
        TypeRegistrationScan.Select(types, PageNs, typeof(FixturePageBase), VmNs, typeof(FixtureViewModelBase));

    [Fact]
    public void The_scan_selects_exactly_the_named_pages_and_view_models()
    {
        var (pages, viewModels) = Scan(FixedTypes());

        Assert.Equal(
        [
            "Ballast.Maui.Tests.ScanFixture.Pages.HomeFixturePage",
            "Ballast.Maui.Tests.ScanFixture.Pages.TripFixturePage",
        ], pages.Select(t => t.FullName));

        Assert.Equal(
        [
            "Ballast.Maui.Tests.ScanFixture.ViewModels.HomeFixtureViewModel",
            "Ballast.Maui.Tests.ScanFixture.ViewModels.TripFixtureViewModel",
        ], viewModels.Select(t => t.FullName));
    }

    [Fact]
    public void The_single_pass_equals_the_previous_two_pass_selection_on_the_fixed_list()
    {
        var types = FixedTypes();
        var (pages, viewModels) = Scan(types);

        Assert.Equal(PreviousPages(types), pages);
        Assert.Equal(PreviousViewModels(types), viewModels);
    }

    [Fact]
    public void The_single_pass_equals_the_previous_selection_over_a_real_assembly_scan()
    {
        // Reflection over the test assembly: the same GetTypes() call an app makes, so nested, compiler-generated and unrelated types are in play too.
        var types = typeof(TypeRegistrationScanTests).Assembly.GetTypes();
        var (pages, viewModels) = Scan(types);

        Assert.Equal(PreviousPages(types), pages);
        Assert.Equal(PreviousViewModels(types), viewModels);
        Assert.Equal(2, pages.Count);
        Assert.Equal(2, viewModels.Count);
    }

    [Fact]
    public void A_type_list_with_no_matches_registers_nothing()
    {
        var (pages, viewModels) = Scan([typeof(string), typeof(List<int>)]);

        Assert.Empty(pages);
        Assert.Empty(viewModels);
    }
}
