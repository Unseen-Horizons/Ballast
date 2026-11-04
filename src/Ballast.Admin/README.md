# Ballast.Admin

The server half of a staff console. It gives the console's static pages a content security policy that allows no inline script
and no outside origin, a cache rule that checks the file before it is used, and headers that keep the console out of search
results and out of the referrer. It also answers "who am I" for the console's navigation: the signed-in person, their roles and
permissions, and whether a role gives full access. It depends on Ballast.AspNetCore, and on ASP.NET Core.

The browser half is `@ballast/admin` (`js/admin` in the repository). It is not a .NET package.

Install:

```
dotnet add package Ballast.Admin --prerelease
```

## Example

```csharp
using Ballast.Admin;
using Ballast.AspNetCore;

builder.Services.AddSecurityHeaders(headers => headers.AddAdminConsole(console =>
{
    console.StyleSources.Add("https://fonts.googleapis.com");
    console.FontSources.Add("https://fonts.gstatic.com");
}));

app.UseSecurityHeaders();
app.UseSecureStaticFiles();

app.MapGet("/admin/api/me", (ClaimsPrincipal user) =>
{
    var me = AdminWhoAmI.Create(user, new AdminWhoAmIOptions { FullAccessRole = "Admin" });
    return me is null ? Results.Unauthorized() : Results.Ok(me);
});
```

The app maps the endpoint behind its own sign-in. The example answers 401 when no one is signed in, because `AdminWhoAmI.Create`
returns null for a principal that is not authenticated.

The usage guide, with every option, every public member and every way the library fails, is `docs/Admin.md` in the repository.
