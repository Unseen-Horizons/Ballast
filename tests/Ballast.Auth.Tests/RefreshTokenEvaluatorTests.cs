// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Auth.Tests;

public class RefreshTokenEvaluatorTests
{
    [Theory]
    [InlineData(false, false, false, RefreshTokenVerdict.Invalid)]
    [InlineData(false, true, true, RefreshTokenVerdict.Invalid)]
    [InlineData(true, false, true, RefreshTokenVerdict.Rotate)]
    [InlineData(true, false, false, RefreshTokenVerdict.Invalid)]
    [InlineData(true, true, false, RefreshTokenVerdict.ReuseDetected)]
    [InlineData(true, true, true, RefreshTokenVerdict.ReuseDetected)]
    public void Each_combination_has_one_verdict(bool found, bool hasReplacement, bool isActive, RefreshTokenVerdict expected)
    {
        Assert.Equal(expected, RefreshTokenEvaluator.Evaluate(found, hasReplacement, isActive));
    }

    [Fact]
    public void A_replayed_token_is_reuse_even_though_it_is_no_longer_active()
    {
        // Rotation sets RevokedAt and ReplacedByToken together, so a replayed token is inactive. Reuse must still win.
        Assert.Equal(RefreshTokenVerdict.ReuseDetected, RefreshTokenEvaluator.Evaluate(found: true, hasReplacement: true, isActive: false));
    }
}
