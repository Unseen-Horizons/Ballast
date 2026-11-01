# Ballast.Health

Readiness and liveness for an ASP.NET Core API: a build route, a liveness route, a readiness route that names each check,
database and schema checks for an Entity Framework Core context, startup gates with their own caps, warm-up steps and a
startup hold. A platform moves traffic to a new copy only when its readiness route answers 200, and this library gives the
routes and the rules behind that answer.

Install:

```
dotnet add package Ballast.Health --prerelease
```

## Example

```csharp
using Ballast.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// The settings come from the "Health" section of configuration. The log lines go under the app's own category.
builder.Services.AddBallastHealth(builder.Configuration, options => options.LogCategory = "Shop.Readiness");

// A check of the app's own. Tagged ready, it joins the readiness route, the readiness log and the startup hold.
builder.Services.AddReadinessCheck<CartStoreCheck>("cart-store");

// The startup hold goes last. It does nothing unless Health:StartupHoldMaxSeconds is above zero.
builder.Services.AddStartupHold();

var app = builder.Build();
app.MapBallastHealth();
app.Run();

// The app's own check.
sealed class CartStoreCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy("The cart store answers."));
}
```

`MapBallastHealth` maps `/health` (the build), `/health/live` (the process is up) and `/health/ready` (the copy can serve).
A ready copy answers 200 with `{"status":"Healthy","checks":{"cart-store":"Healthy"}}`. An unready one answers 503 with the
same shape, and the status of each check shows which one is not ready.

The usage guide, with every option and every way the library fails, is `docs/Health.md` in the repository.
