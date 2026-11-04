# Ballast.Jobs

Background work that writes runs in one copy of an app at a time. During a deploy the old and the new copy of an app run side by
side for a while, and two copies running the same writing job would double the work or corrupt it. Ballast.Jobs holds one SQL
Server application lock, on a connection of its own, and only the copy that holds it runs the work. It also gives leader-only
hosted services, a 409 for admin actions that a copy without the lock must refuse, and a readiness check that says whether a copy
is a sensible part of the lock.

Install:

```
dotnet add package Ballast.Jobs --prerelease
```

## Example

```csharp
using Ballast.Jobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Shop")
    ?? "Server=localhost;Database=Shop;Trusted_Connection=True;TrustServerCertificate=True";

// The lock goes first: it starts before every other hosted service and stops after them. It is off until
// JobLeadership:Enabled is true, and then only the copy that holds the lock runs the workers below.
builder.Services.AddJobLeadership(builder.Configuration, "Shop:jobs", connectionString);
builder.Services.ConfigureGracefulShutdown(builder.Configuration);

// A worker that writes. It runs only while this copy holds the lock.
builder.Services.AddLeaderHostedService<OrderExpiryWorker>();

var app = builder.Build();
app.Run();

// The app's own worker: it loops until the host or the lock's term ends it.
sealed class OrderExpiryWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // ... expire the orders that are due ...
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

With `JobLeadership:Enabled` off, which is the default, this copy is always the holder and no lock is taken. Set it to `true` in
Production. A copy that does not hold the lock keeps asking for it, and takes over the moment the holder lets go.

The usage guide, with every option, every public entry point and every way the library fails, is `docs/Jobs.md` in the repository.
