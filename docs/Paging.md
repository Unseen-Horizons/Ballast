# Ballast.Paging: usage guide

Ballast.Paging gives an API a page request with safe limits, an overflow-safe row offset, and the paged result that apps
read. A value above its limit is refused with a validation error that names the field. It is never cut down silently.
It depends on .NET alone.

The package README has the install line and a runnable example. This guide covers every type and member, and every way
each one fails.

## How the pieces fit

1. A query binds `page` and `perPage` to a `PageRequest`. ASP.NET model binding does this by name, without attributes.
2. The API validates the request with `Validator.TryValidateObject`, and refuses it when a value is out of range.
3. After validation, the API reads `Skip` for the row offset, and counts the rows of the whole list.
4. The API returns a `PagedResult<T>` built from the request and the total.

## PageRequest

```csharp
public record PageRequest : IValidatableObject
```

The 1-based page and the number of rows on each page. A query binds its `page` and `perPage` parameters to these properties
by name, in any letter case.

### Members

| Member | Type | Meaning |
|---|---|---|
| `MaxPage` (const) | `int` | `1_000_000`, the largest page a request may ask for. |
| `Page` | `int` | The 1-based page number. Default 1. |
| `PerPage` | `int` | The number of rows on a page. Default 50. The largest allowed is `MaxPerPage`. |
| `MaxPerPage` (protected virtual) | `int` | The largest `PerPage` allowed. It is 500 here. A derived record overrides it to lower the cap. |
| `Skip` | `int` | The rows before this page. It is `Paging.SkipFor(Page, PerPage)`, so it throws when either value is below 1. |
| `Validate(ValidationContext)` | `IEnumerable<ValidationResult>` | The limits check. See below. |

`Page` and `PerPage` are `init` properties. A request is built with an object initializer, or bound by the framework.

### Validate

`Validate` checks the request against these limits:

| Member | Valid | Refused |
|---|---|---|
| `Page` | 1 to `MaxPage` (1,000,000) | 0, negative, or above `MaxPage` |
| `PerPage` | 1 to `MaxPerPage` (500 here) | 0, negative, or above `MaxPerPage` |

Each value out of range gives one `ValidationResult`. Its `MemberNames` holds one name, `Page` or `PerPage`, and its message
names the field and its limits, for example `perPage must be from 1 to 500.` Two bad values give two results. A valid request
gives none.

`Validate` runs only when something calls it. The usual call is:

```csharp
using System.ComponentModel.DataAnnotations;
using Ballast.Paging;

var request = new PageRequest { Page = 2, PerPage = 25 };
List<ValidationResult> refusals = [];
bool valid = Validator.TryValidateObject(request, new ValidationContext(request), refusals, validateAllProperties: true);
```

`PageRequest` has no property attributes, so `validateAllProperties` changes nothing for it. The flag is set here because it
is the form the tests and the README use, and it keeps the check the same if an attribute is added later.

Refusals:

- A request is never corrected. `PerPage = 501` is refused, not cut to 500. `Page = 0` is refused, not set to 1.
- `Skip` does not clamp either. For a `Page` or `PerPage` below 1 it throws `ArgumentOutOfRangeException`, as
  `Paging.SkipFor` does. Check `Validate` before you use `Skip` for a query.

### Derived requests with a lower cap

A project that wants a lower page size derives a record and overrides `MaxPerPage`:

```csharp
public sealed record SmallPage : PageRequest
{
    protected override int MaxPerPage => 200;
}
```

With that record, `PerPage = 200` is valid, and `PerPage = 201` or `PerPage = 500` is refused with the message
`perPage must be from 1 to 200.` The default `PerPage` of 50 is still allowed here.

A cap below the default refuses a request that keeps the default:

```csharp
public sealed record TinyPage : PageRequest
{
    protected override int MaxPerPage => 20;
}

// new TinyPage() is refused: PerPage is 50, above the cap of 20.
```

A derived record that lowers the cap below 50 must set its own default at or below the cap, for example in its constructor:
`public SmallPage() { PerPage = 20; }`.

### Failures

| Situation | Result |
|---|---|
| `Page` or `PerPage` is out of range | A `ValidationResult` naming the member, from `Validate`. Nothing throws. |
| `Validate` is never called | Nothing is refused. The request holds whatever it was given. |
| `MaxPage` or `MaxPerPage` is set below a default | The default fails validation, as described above. |
| `Skip` is read when `Page` or `PerPage` is below 1 | `ArgumentOutOfRangeException` from `Paging.SkipFor`, with `ParamName` `page` or `perPage`. |

## Paging.SkipFor

```csharp
public static class Paging
{
    public static int SkipFor(int page, int perPage);
}
```

The number of rows to skip before a page. Pages are 1-based. The offset is computed in `long` and capped at `int.MaxValue`,
so a very large page never wraps to a negative offset.

The cap is a true answer, not a guess. An offset that large is past the end of any list a database can return, so the page
comes back empty with its real total.

| Input | Result |
|---|---|
| `page` 1, `perPage` 50 | 0 |
| `page` 2, `perPage` 500 | 500 |
| `page` 3, `perPage` 20 | 40 |
| `page` 6, `perPage` 1 | 5 |
| `page` `int.MaxValue`, `perPage` 500 | `int.MaxValue` |
| `page` 1,000,000, `perPage` 500 | 499,999,500 |

Ways it fails. It never clamps a value and never returns a guess:

| Input | Exception | `ParamName` |
|---|---|---|
| `page` is below 1 (0, -4, `int.MinValue`) | `ArgumentOutOfRangeException` | `page` |
| `perPage` is below 1 (0, -3) | `ArgumentOutOfRangeException` | `perPage` |

When both are below 1, `page` is the one named, because it is checked first.

```csharp
int skip = Paging.SkipFor(3, 20); // 40
```

## PagedResult&lt;T&gt;

```csharp
public record PagedResult<T>
```

One page of a list: its rows, the total number of rows, and where the page sits.

### Members

| Member | Type | Meaning |
|---|---|---|
| `Data` | `IReadOnlyList<T>` | The rows on the page. A copy is kept. It is empty for a page past the last one. |
| `Total` | `int` | The number of rows in the whole list. |
| `Page` | `int` | The 1-based page number. |
| `PerPage` | `int` | The number of rows on a full page. |
| `TotalPages` | `int` | The number of pages. It is 0 when the list is empty. |
| `HasNextPage` | `bool` | True when `Page < TotalPages`. |
| `HasPreviousPage` | `bool` | True when `Page > 1`. |

### Constructors

**From a request and a total.** This is the constructor a server uses:

```csharp
public PagedResult(IReadOnlyList<T> data, int total, PageRequest request)
```

`Page` and `PerPage` come from `request`. `TotalPages` is `Total` divided by `PerPage`, rounded up. The constructor refuses a
`request` whose `Page` or `PerPage` is below 1, so the division is never by zero. It does not check the upper limits. Those
are checked by `PageRequest.Validate`, which a server should run first.

**From its values.** This is the constructor System.Text.Json uses when it reads a result back:

```csharp
[JsonConstructor]
public PagedResult(IReadOnlyList<T> data, int total, int page, int perPage, int totalPages)
```

It stores each value as given. It does not compute `TotalPages` and does not check `page`, `perPage` or `totalPages`. Use it
to read a result back, not to build a new one.

Ways the constructors fail:

| Input | Exception | `ParamName` |
|---|---|---|
| `data` is null | `ArgumentNullException` | `data` |
| `request` is null (request constructor) | `ArgumentNullException` | `request` |
| `total` is negative (both constructors that take `total`) | `ArgumentOutOfRangeException` | `total` |
| `request.Page` is below 1 | `ArgumentOutOfRangeException`, message `request.Page must be 1 or more.` | `request` |
| `request.PerPage` is below 1 | `ArgumentOutOfRangeException`, message `request.PerPage must be 1 or more.` | `request` |

Other cases, which are not refused:

| Situation | Result |
|---|---|
| `total` is 0 | `TotalPages` is 0, and `HasNextPage` is false. `HasPreviousPage` is true for any page after the first. |
| `request.Page` is past the last page | `Data` is empty, `Total` and `TotalPages` are correct, and `HasPreviousPage` is true. |
| The `data` list is changed after the call | The result does not change, because a copy was kept. |

### JSON

With `JsonSerializerDefaults.Web`, a result has exactly these members: `data`, `total`, `page`, `perPage`, `totalPages`,
`hasNextPage` and `hasPreviousPage`. The apps read these names, so they do not change.

```csharp
using System.Text.Json;

var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
string json = JsonSerializer.Serialize(new PagedResult<string>(["a"], 10, new PageRequest { Page = 2, PerPage = 5 }), web);
// A JSON object with the seven members above, for example {"data":["a"],"total":10,"page":2,"perPage":5,"totalPages":2,"hasNextPage":false,"hasPreviousPage":true}.
// The member order is the serializer's; the names are what is fixed.
```

With default options the names are capitalised (`Data`, `Total`), not the names above. Use `JsonSerializerDefaults.Web`, or
an options object with `JsonNamingPolicy.CamelCase`, on both the writer and the reader.

Reading back, the computed `hasNextPage` and `hasPreviousPage` members are ignored: they have no setter, and the flags are
computed from `page` and `totalPages`.

## Changes from the earlier paging types

- Too large a page is refused with a validation error. It is not cut down. The earlier code silently cut a page size above
  200 to 200.
- A page or a page size below 1 is refused. The earlier code set a page below 1 to 1 and a page size below 1 to its default.
  `Paging.SkipFor`, `PageRequest.Skip` and the request constructor of `PagedResult` now throw `ArgumentOutOfRangeException`
  for these values, and `PageRequest.Validate` reports them as validation results.
- The row offset is computed without overflow. The earlier code's offset overflowed `int` for a large page, and the query
  failed.
- One result type replaces the three earlier shapes. Its `Data` is an `IReadOnlyList<T>`, so a caller cannot change the list
  after the result is built. Its JSON members are the same.
