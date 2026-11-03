// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Fixture types for TypeRegistrationScanTests, each kind in its own namespace, as an app keeps its pages and view models.
// Block namespaces because one file holds three of them.

namespace Ballast.Maui.Tests.ScanFixture.Pages
{
    public abstract class FixturePageBase;

    public interface IFixturePageMarker;

    public sealed class HomeFixturePage : FixturePageBase;

    public sealed class TripFixturePage : FixturePageBase;

    public abstract class AbstractFixturePage : FixturePageBase;

    public sealed class NotAPage;
}

namespace Ballast.Maui.Tests.ScanFixture.ViewModels
{
    public abstract class FixtureViewModelBase;

    public sealed class HomeFixtureViewModel : FixtureViewModelBase;

    public sealed class TripFixtureViewModel : FixtureViewModelBase;

    public abstract class AbstractFixtureViewModel : FixtureViewModelBase;

    public sealed class PlainHelper;
}

namespace Ballast.Maui.Tests.ScanFixture.Other
{
    public sealed class WrongNamespacePage : Ballast.Maui.Tests.ScanFixture.Pages.FixturePageBase;

    public sealed class WrongNamespaceViewModel : Ballast.Maui.Tests.ScanFixture.ViewModels.FixtureViewModelBase;
}
