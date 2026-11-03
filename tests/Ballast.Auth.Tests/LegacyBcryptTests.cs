// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Errors;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Ballast.Auth.Tests;

/// <summary>The legacy bcrypt hash option: a stored bcrypt hash signs in, and is rewritten in Identity's format at that sign-in.</summary>
public sealed class LegacyBcryptTests
{
    private const string Email = "legacy@example.test";
    private const string Password = "Correct-horse-42!";

    /// <summary>Sets a user's stored hash, as an app's earlier code wrote it.</summary>
    private static async Task SetHashAsync(Harness<StringUser, string, StringRefresh> harness, string hash)
    {
        await using var scope = harness.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        var user = await users.FindByEmailAsync(Email) ?? throw new InvalidOperationException("No test user.");
        user.PasswordHash = hash;
        await users.UpdateAsync(user);
    }

    private static async Task<string?> StoredHashAsync(Harness<StringUser, string, StringRefresh> harness)
    {
        await using var scope = harness.Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<StringUser>>();
        return (await users.FindByEmailAsync(Email))?.PasswordHash;
    }

    private static async Task<AppException?> TrySignInAsync(Harness<StringUser, string, StringRefresh> harness, string password)
    {
        await using var scope = harness.Scope();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordSignInService<StringUser, string, AuthTestDb<StringUser, string, StringRefresh>>>();
        try
        {
            _ = await passwords.CheckPasswordAsync(Email, password);
            return null;
        }
        catch (AppException error)
        {
            return error;
        }
    }

    [Fact]
    public async Task A_bcrypt_hash_signs_in_and_is_rewritten_in_identitys_format()
    {
        await using var harness = await Harness<StringUser, string, StringRefresh>.CreateAsync(configure: o => o.LegacyBcryptPasswords = true);
        await harness.CreateUserAsync(Email, null);
        await SetHashAsync(harness, BCrypt.Net.BCrypt.HashPassword(Password, 4));

        var error = await TrySignInAsync(harness, Password);

        Assert.Null(error);
        var stored = await StoredHashAsync(harness);
        Assert.NotNull(stored);
        Assert.StartsWith("AQAAAA", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_password_against_a_bcrypt_hash_is_refused_and_the_hash_is_kept()
    {
        await using var harness = await Harness<StringUser, string, StringRefresh>.CreateAsync(configure: o => o.LegacyBcryptPasswords = true);
        await harness.CreateUserAsync(Email, null);
        var bcrypt = BCrypt.Net.BCrypt.HashPassword(Password, 4);
        await SetHashAsync(harness, bcrypt);

        var error = await TrySignInAsync(harness, "wrong-Password-1!");

        Assert.NotNull(error);
        Assert.Equal("INVALID_CREDENTIALS", error.Code);
        Assert.Equal(bcrypt, await StoredHashAsync(harness));
    }

    [Fact]
    public async Task A_damaged_bcrypt_looking_hash_is_a_refusal_not_an_exception()
    {
        await using var harness = await Harness<StringUser, string, StringRefresh>.CreateAsync(configure: o => o.LegacyBcryptPasswords = true);
        await harness.CreateUserAsync(Email, null);
        await SetHashAsync(harness, "$2a$not-a-real-hash");

        var error = await TrySignInAsync(harness, Password);

        Assert.NotNull(error);
        Assert.Equal("INVALID_CREDENTIALS", error.Code);
    }

    [Fact]
    public async Task Without_the_option_a_bcrypt_hash_is_not_accepted()
    {
        await using var harness = await Harness<StringUser, string, StringRefresh>.CreateAsync();
        await harness.CreateUserAsync(Email, null);
        await SetHashAsync(harness, BCrypt.Net.BCrypt.HashPassword(Password, 4));

        var error = await TrySignInAsync(harness, Password);

        Assert.NotNull(error);
        Assert.Equal("INVALID_CREDENTIALS", error.Code);
    }
}
