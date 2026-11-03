// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ballast.Auth.Tests;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    /// <summary>The current instant. Set it to move the clock.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Counts every hash and every verification, so a test can see how much hashing a refusal spent.</summary>
internal sealed class HashCounter
{
    /// <summary>The number of calls so far.</summary>
    public int Calls { get; set; }
}

/// <summary>A password hasher that counts its calls and otherwise is the hasher it wraps.</summary>
/// <typeparam name="TUser">The user type.</typeparam>
internal sealed class CountingHasher<TUser> : IPasswordHasher<TUser>
    where TUser : class
{
    private readonly HashCounter _counter;
    private readonly IPasswordHasher<TUser> _inner;

    public CountingHasher(HashCounter counter, IPasswordHasher<TUser> inner)
    {
        _counter = counter;
        _inner = inner;
    }

    public string HashPassword(TUser user, string password)
    {
        _counter.Calls++;
        return _inner.HashPassword(user, password);
    }

    public PasswordVerificationResult VerifyHashedPassword(TUser user, string hashedPassword, string providedPassword)
    {
        _counter.Calls++;
        return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}

/// <summary>The audit row the test database holds.</summary>
public sealed class AuditRow
{
    /// <summary>The row's id.</summary>
    public int Id { get; set; }

    /// <summary>The user, as text.</summary>
    public string? UserId { get; set; }

    /// <summary>The action.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The kind of entity.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The entity's id, as text.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>When it happened, in UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The old values, or null.</summary>
    public string? OldValues { get; set; }

    /// <summary>The new values, or null.</summary>
    public string? NewValues { get; set; }
}

/// <summary>The audit sink an app would write: it adds the row to the app's context, which the library then saves.</summary>
/// <typeparam name="TContext">The context.</typeparam>
internal sealed class AuditSink<TContext> : IAuthAudit
    where TContext : DbContext
{
    private readonly TContext _db;

    public AuditSink(TContext db)
    {
        _db = db;
    }

    public void Add(AuthAuditEntry entry)
    {
        _db.Add(new AuditRow
        {
            UserId = entry.UserId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            CreatedAtUtc = entry.CreatedAtUtc,
            OldValues = entry.OldValues,
            NewValues = entry.NewValues,
        });
    }
}
