// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;

using Ballast.Testing.Fakes;

namespace Ballast.Testing.Tests.Fakes;

public class LoopbackServerTests
{
    [Fact]
    public async Task The_server_answers_each_request_from_the_function_and_records_its_path()
    {
        await using var server = new LoopbackServer((method, path) => path == "/ok"
            ? (200, "X-Test: yes\r\n", "fine")
            : (404, string.Empty, "missing"));

        using var client = new HttpClient();

        var ok = await client.GetAsync(new Uri(server.BaseAddress, "/ok"));
        var missing = await client.GetAsync(new Uri(server.BaseAddress, "/nowhere"));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("yes", ok.Headers.GetValues("X-Test").Single());
        Assert.Equal("fine", await ok.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(["/ok", "/nowhere"], server.Paths);
    }

    [Fact]
    public async Task The_server_listens_on_a_free_loopback_port()
    {
        await using var server = new LoopbackServer((_, _) => (200, string.Empty, string.Empty));

        Assert.InRange(server.Port, 1, 65535);
        Assert.Equal(IPAddress.Loopback.ToString(), server.BaseAddress.Host);
    }

    [Fact]
    public void A_null_answer_function_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new LoopbackServer(null!));
    }
}
