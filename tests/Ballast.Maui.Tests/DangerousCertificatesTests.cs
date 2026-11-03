// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net.Security;

using Ballast.Maui.Http;

namespace Ballast.Maui.Tests;

/// <summary>The certificate option: off keeps the system's trust, on accepts any certificate only in a debuggable build, and a build that says it is not debuggable refuses it.</summary>
public sealed class DangerousCertificatesTests
{
    [Fact]
    public void Without_the_option_the_system_trust_stays()
    {
        using var handler = Assert.IsType<HttpClientHandler>(DangerousCertificates.CreateHandler(false, new FakeRuntimeBuild(false)));

        Assert.Null(handler.ServerCertificateCustomValidationCallback);
    }

    [Fact]
    public void A_debuggable_build_that_asks_accepts_every_certificate()
    {
        using var handler = Assert.IsType<HttpClientHandler>(DangerousCertificates.CreateHandler(true, new FakeRuntimeBuild(true)));
        var callback = handler.ServerCertificateCustomValidationCallback;

        Assert.NotNull(callback);
        Assert.True(callback!(null!, null, null, SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact]
    public void A_build_that_reports_itself_not_debuggable_refuses_the_option()
    {
        Assert.Throws<InvalidOperationException>(() => DangerousCertificates.CreateHandler(true, new FakeRuntimeBuild(false)));
    }

    [Fact]
    public void A_build_that_reports_no_debuggable_answer_is_not_refused()
    {
        using var handler = DangerousCertificates.CreateHandler(true, new FakeRuntimeBuild(null));

        Assert.NotNull(handler);
    }

    [Fact]
    public void A_null_build_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => DangerousCertificates.CreateHandler(false, null!));
    }
}
