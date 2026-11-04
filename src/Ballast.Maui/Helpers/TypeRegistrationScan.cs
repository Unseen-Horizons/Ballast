// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Maui.Helpers;

/// <summary>
/// Which of an assembly's types become transient registrations: the concrete subclasses of a base type that live in one namespace. Pure (types in,
/// types out), so a unit test can pin the selection with a fixed type list. The app gets the assembly's types once and picks pages and view models
/// from that same list in a single pass.
/// </summary>
public static class TypeRegistrationScan
{
    /// <summary>Selects the concrete page and view-model types of an app.</summary>
    /// <param name="types">The types to select from, usually an assembly's.</param>
    /// <param name="pageNamespace">The namespace the pages live in.</param>
    /// <param name="pageBase">The base type every page derives from.</param>
    /// <param name="viewModelNamespace">The namespace the view models live in.</param>
    /// <param name="viewModelBase">The base type every view model derives from.</param>
    /// <returns>The pages and the view models, in the order of <paramref name="types"/>.</returns>
    public static (List<Type> Pages, List<Type> ViewModels) Select(
        IEnumerable<Type> types,
        string pageNamespace,
        Type pageBase,
        string viewModelNamespace,
        Type viewModelBase)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(pageBase);
        ArgumentNullException.ThrowIfNull(viewModelBase);

        List<Type> pages = [];
        List<Type> viewModels = [];
        foreach (var type in types)
        {
            if (!type.IsClass || type.IsAbstract)
                continue;

            if (type.Namespace == pageNamespace && type.IsSubclassOf(pageBase))
                pages.Add(type);
            if (type.Namespace == viewModelNamespace && type.IsSubclassOf(viewModelBase))
                viewModels.Add(type);
        }

        return (pages, viewModels);
    }
}
