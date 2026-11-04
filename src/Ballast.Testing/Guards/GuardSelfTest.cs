// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Xunit;

namespace Ballast.Testing.Guards;

/// <summary>
/// Proves that a guard can see what it guards against. A scan that finds nothing in a clean repository passes for the wrong reason
/// when its pattern is broken, so every guard plants a violation in a string it builds itself and checks that the scan reports it.
/// </summary>
public static class GuardSelfTest
{
    /// <summary>
    /// Asserts that <paramref name="scan"/> reports a finding containing <paramref name="expectedFragment"/> for the planted source.
    /// The failure message lists what the scan did find, so a broken pattern is easy to see.
    /// </summary>
    /// <param name="scan">The guard's scan: it takes source text and returns its findings. Required.</param>
    /// <param name="planted">A source text that contains the violation. Written in the test, never read from the repository.</param>
    /// <param name="expectedFragment">Text the finding must contain, such as the rule's name or the banned call.</param>
    public static void AssertCatches(Func<string, IEnumerable<string>> scan, string planted, string expectedFragment)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(planted);
        var findings = scan(planted).ToList();
        Assert.True(
            findings.Any(finding => finding.Contains(expectedFragment, StringComparison.Ordinal)),
            $"The guard did not catch the planted violation '{expectedFragment}'. It reported: "
            + (findings.Count == 0 ? "nothing" : string.Join("; ", findings)));
    }

    /// <summary>
    /// Asserts that <paramref name="scan"/> reports nothing for <paramref name="clean"/>, which is the planted source with the
    /// violation removed. A pattern that also matches the fixed code is a broken pattern, so this pairs with <see cref="AssertCatches"/>.
    /// </summary>
    /// <param name="scan">The guard's scan.</param>
    /// <param name="clean">A source text that is correct.</param>
    public static void AssertPasses(Func<string, IEnumerable<string>> scan, string clean)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(clean);
        var findings = scan(clean).ToList();
        Assert.True(findings.Count == 0, "The guard reported a correct source: " + string.Join("; ", findings));
    }
}
