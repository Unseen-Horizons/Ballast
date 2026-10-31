# Ballast.Paging

A page request with safe limits, an overflow-safe row offset, and the paged result shape that apps read. A request above
its limit is refused with a validation error that names the field. It is never cut down silently. It depends on .NET alone.

Install:

```
dotnet add package Ballast.Paging --prerelease
```

## Example

```csharp
using System.ComponentModel.DataAnnotations;
using Ballast.Paging;

// The request a query binds: ?page=2&perPage=25. Check it before the query runs.
var request = new PageRequest { Page = 2, PerPage = 25 };
List<ValidationResult> refusals = [];
bool valid = Validator.TryValidateObject(request, new ValidationContext(request), refusals, validateAllProperties: true);

// The rows to skip, and the answer for the page.
var rows = new[] { "row 26", "row 27" };
var page = new PagedResult<string>(rows, total: 60, request);
Console.WriteLine($"valid={valid} skip={request.Skip} totalPages={page.TotalPages} next={page.HasNextPage}");
```

The example prints `valid=True skip=25 totalPages=3 next=True`.

The usage guide, with every option and every way the library fails, is `docs/Paging.md` in the repository.
