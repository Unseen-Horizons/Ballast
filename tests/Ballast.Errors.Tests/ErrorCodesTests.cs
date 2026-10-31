// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

namespace Ballast.Errors.Tests;

public class ErrorCodesTests
{
    [Fact]
    public void The_codes_keep_their_released_values()
    {
        Assert.Equal("NOT_FOUND", ErrorCodes.NotFound);
        Assert.Equal("VALIDATION_FAILED", ErrorCodes.ValidationFailed);
        Assert.Equal("UPSTREAM_UNAVAILABLE", ErrorCodes.UpstreamUnavailable);
        Assert.Equal("INTERNAL_ERROR", ErrorCodes.InternalError);
        Assert.Equal("RATE_LIMITED", ErrorCodes.RateLimited);
        Assert.Equal("SERVICE_BUSY", ErrorCodes.ServiceBusy);
        Assert.Equal("ENDPOINT_NOT_FOUND", ErrorCodes.EndpointNotFound);
    }
}
