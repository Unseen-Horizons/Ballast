# Ballast.SqlServer

Helpers for an app that uses SQL Server through Entity Framework Core: classify SQL errors (a duplicate key, a deadlock, a busy
database, a transient fault) wherever EF Core wraps them, store enums as text, run a change in a transaction that retries cleanly,
claim a row so that only one request wins, read a sequence, split a script on its `GO` lines, and refuse a developer database in
production. It depends on EF Core 10 and Microsoft.Data.SqlClient, and on no Ballast library.

Install:

```
dotnet add package Ballast.SqlServer --prerelease
```

## Example

```csharp
using Ballast.SqlServer;
using Microsoft.EntityFrameworkCore;

// A context that stores enums as text of up to 32 characters, and restricts deletes.
public class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseEnumsAsStrings(maxLength: 32);
        modelBuilder.RestrictDeletes();
    }
}

public enum OrderStatus { Pending, Paid }

public class Order
{
    public int Id { get; set; }
    public OrderStatus Status { get; set; }
}

// The connection string the app reads at startup. Outside Development, a string for this computer is refused.
var connectionString = "Server=localhost;Database=Shop;Trusted_Connection=True";
Console.WriteLine(ConnectionStringCheck.Classify(connectionString));

// The model the context builds: the enum is a text column of up to 32 characters.
await using var db = new ShopContext(new DbContextOptionsBuilder<ShopContext>().UseSqlServer("Server=.;Database=Shop").Options);
var status = db.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Status))!;
Console.WriteLine(status.GetColumnType());

// A save that hit a unique index is a race, not a fault. SqlErrors looks through the DbUpdateException to the SQL error.
var duplicate = new DbUpdateException("save failed", new InvalidOperationException("no SQL error here"));
Console.WriteLine(SqlErrors.IsUniqueViolation(duplicate));

// A script with GO lines, split into the batches SQL Server runs one at a time.
Console.WriteLine(SqlBatches.Split("SELECT 1\nGO\nSELECT 2").Count);
```

The example prints `LocalHost`, `nvarchar(32)`, `False` and `2`.

Against a database, the same package also runs a claim (`db.Orders.Where(o => o.Id == id && o.Status == OrderStatus.Pending)
.ClaimAsync(s => s.SetProperty(o => o.Status, OrderStatus.Paid))`, which is true for the one request that wins) and a
transaction that retries (`db.ExecuteInTransactionAsync(async ct => { ... })`).

The usage guide, with every option, every error number and every way each piece fails, is `docs/SqlServer.md` in the repository.
