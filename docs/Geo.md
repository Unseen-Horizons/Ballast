# Ballast.Geo: usage guide

`Ballast.Geo` checks latitude and longitude pairs, measures the distance between two of them, makes the box a nearby search
filters on, and tests whether a position lies inside a polygon. It has the namespace `Ballast.Geo`, targets `net10.0`, and
depends on no package.

Install it with:

```
dotnet add package Ballast.Geo --prerelease
```

The package README has a short example that runs as written. This guide covers every public member, what it accepts, and
every way it fails.

## Conventions

- **Units.** Latitude and longitude are decimal degrees. Distances and lengths are metres.
- **Ranges.** Latitude is -90 to 90 and longitude is -180 to 180. A value outside those ranges, NaN, and an infinity are not
  positions.
- **Order.** A point is written latitude first: `Contains(latitude, longitude, rings)` and
  `HaversineMeters(latitude1, longitude1, latitude2, longitude2)`. A **ring** is written longitude first, as `[longitude, latitude]`,
  because that is the order GeoJSON uses. This is where a mix-up happens, so check it whenever a ring is typed by hand.
- **The sphere.** Distances are on a sphere with the Earth's mean radius, 6,371,000 m. A sphere is an approximation: a distance on
  the real, slightly flattened Earth can differ from it by about half a percent. That is enough to rank stops or estimate a walk.
  It is not a survey.
- **No silent substitution.** A bad coordinate is refused, never turned into NaN or a guess. The members that compute a number
  throw `ArgumentOutOfRangeException`. The members that answer a question (`IsUsable`, `TryParseRings`) return `false` or `null`.
- **No options.** None of the members takes a setting that changes what it computes. The radius, the coordinates and the rings
  are the inputs.

---

## GeoBounds

A static class with one member, the rule that decides whether a pair is a position worth storing or measuring.

### `GeoBounds.IsUsable(double latitude, double longitude)` → `bool`

Returns `true` when both values are numbers in range and the pair is not (0, 0).

- **Never throws.**
- **Returns `false`** for: NaN in either value; an infinity in either value; latitude outside -90 to 90; longitude outside
  -180 to 180; (0, 0); (-0.0, 0.0) and (0.0, -0.0), because negative zero equals zero.

```csharp
GeoBounds.IsUsable(45.8131, 15.9619);   // true
GeoBounds.IsUsable(0, 0);               // false: (0, 0) is refused
GeoBounds.IsUsable(double.NaN, 15.9);   // false
GeoBounds.IsUsable(91, 15.9);           // false
GeoBounds.IsUsable(0, 15.9);            // true: only a pair that is exactly (0, 0) is refused
```

**Why (0, 0) is refused.** It is a legal coordinate in the Gulf of Guinea. In data, though, it is almost always a missing value that
defaulted to zero. Use this check to decide whether to store a position. A distance from (0, 0) is still a valid calculation, and
`GeoDistance` does not refuse it.

---

## GeoDistance

A static class with the Earth's radius, the length of one degree of latitude, and two distance members.

### `GeoDistance.EarthRadiusMeters` (constant, `6_371_000.0`)

The mean radius every distance and degree length in this library uses.

### `GeoDistance.MetersPerDegreeLatitude` (constant, about `111,194.93`)

The length of one degree of latitude in metres: π × `EarthRadiusMeters` ÷ 180. It is the same everywhere on the sphere.

```csharp
GeoDistance.MetersPerDegreeLatitude;   // 111194.93...
```

### `GeoDistance.HaversineMeters(double latitude1, double longitude1, double latitude2, double longitude2)` → `double`

The great-circle distance in metres between two positions, by the haversine formula.

- **Returns** a distance from 0 up to half the circumference, about 20,015,086.8 m. Two points on opposite sides of the Earth give that
  maximum, not NaN. Rounding can push the internal value a hair past 1 for such a pair, and the library holds it to [0, 1] before the
  square root.
- **Throws `ArgumentOutOfRangeException`** when any coordinate is NaN, an infinity, or out of range. The check runs in the order
  `latitude1`, `longitude1`, `latitude2`, `longitude2`, and the exception names the first bad one in `ParamName`.
- **Does not refuse (0, 0).**

```csharp
GeoDistance.HaversineMeters(0, 0, 0, 1);       // 111194.93: one degree along the equator
GeoDistance.HaversineMeters(0, 0, 0, 180);     // 20015086.8: half the circumference
GeoDistance.HaversineMeters(45.8, 15.98, 43.5, 16.44);   // 258322.7 m, shown here rounded to whole metres

try
{
    GeoDistance.HaversineMeters(91, 0, 0, 0);  // latitude1 is out of range
}
catch (ArgumentOutOfRangeException error)
{
    Console.WriteLine(error.ParamName);        // latitude1
}
```

The formula, the choice of the radius and the accuracy are the ones in the class remarks: a sphere, not the ellipsoid.

### `GeoDistance.MetersPerDegreeLongitude(double latitude)` → `double`

The length in metres of one degree of longitude at a latitude: `MetersPerDegreeLatitude` × cos(latitude).

- **Returns** about 111,194.93 at the equator, 55,597.46 at 60°, and a value close to zero at the poles.
- **Throws `ArgumentOutOfRangeException`** when the latitude is NaN, an infinity, or out of range. `ParamName` is `latitude`.

```csharp
GeoDistance.MetersPerDegreeLongitude(0);    // 111194.93
GeoDistance.MetersPerDegreeLongitude(60);   // 55597.46
```

---

## GeoBox

A rectangle in latitude and longitude. A "nearby" search uses it as a first filter, before the exact distance check.

### The record members

`GeoBox` is a `readonly record struct` with five properties, set by its constructor:

| Member | Meaning |
|---|---|
| `MinLat` | The southern edge, in degrees, from -90 to 90. |
| `MinLon` | The western edge, in degrees, from -180 to 180. |
| `MaxLat` | The northern edge, in degrees, from -90 to 90. |
| `MaxLon` | The eastern edge, in degrees, from -180 to 180. |
| `CrossesAntimeridian` | `true` when the box was cut at the 180th meridian. The part on the other side is given by `GeoBox.FarSide`. |

The constructor `new GeoBox(minLat, minLon, maxLat, maxLon, crossesAntimeridian)` takes the values as given and checks nothing.
`GeoBox.Around` is the way to make a box from a point and a radius.

As a record struct it also has value equality, `==` and `!=`, `ToString`, and a `Deconstruct` method:
`var (minLat, minLon, maxLat, maxLon, crosses) = box;`.

### `GeoBox.Around(double latitude, double longitude, double radiusMeters)` → `GeoBox`

The box that holds every position within `radiusMeters` of the centre.

- **The latitude edges** are the centre's latitude ± the radius in degrees (one degree is 111,194.93 m).
- **The longitude edges** are the centre's longitude ± the reach. The reach is the widest longitude difference a circle of that
  radius covers at that latitude, from sin(reach) = sin(radius) ÷ cos(latitude). It is wider at high latitudes.
- **It is a superset.** Every position within the radius is inside the box, and the box's corners can lie outside the circle. A search
  takes the candidates the box returns and keeps those whose `HaversineMeters` distance is within the radius.
- **A radius of zero** gives a box of one point.
- **A circle that reaches a pole** (its latitude edge would pass 90 or -90) gives a box that spans -180 to 180. Latitude is cut at
  the pole. Nothing is cut in longitude, so `CrossesAntimeridian` is `false`.
- **`CrossesAntimeridian` is `true`** when a longitude edge passed 180 or -180 and was cut to it. The cut is reported, never silent.
  `GeoBox.FarSide` gives the part on the other side.

**Throws `ArgumentOutOfRangeException`** when:
- the centre is not usable (see `GeoBounds.IsUsable`): NaN, infinite, out of range, or (0, 0). `ParamName` is `latitude`.
- `radiusMeters` is negative, NaN, or infinite. `ParamName` is `radiusMeters`.

```csharp
var box = GeoBox.Around(45.8, 15.98, 1000);
// MinLat 45.791007, MaxLat 45.808993, MinLon 15.967100, MaxLon 15.992900, CrossesAntimeridian false

var east = GeoBox.Around(0, 179.9, 50_000);
// MaxLon 180, MinLon 179.4503, CrossesAntimeridian true

var pole = GeoBox.Around(89.99, 10, 100_000);
// MinLat 89.0907, MaxLat 90, MinLon -180, MaxLon 180, CrossesAntimeridian false

var point = GeoBox.Around(45, 15, 0);
// MinLat = MaxLat = 45, MinLon = MaxLon = 15

GeoBox.Around(0, 0, 100);      // throws: (0, 0) is not a usable centre
GeoBox.Around(45, 15, -1);     // throws: the radius is negative
```

### `GeoBox.FarSide(double latitude, double longitude, double radiusMeters)` → `GeoBox?`

The part of the circle's box beyond the 180th meridian, for a circle that `Around` cuts there.

- **Returns** a box with `CrossesAntimeridian` set to `true`, when `Around` with the same arguments cut the box. Its latitude edges are
  `Around`'s. Its longitude runs from -180 to the far edge when the east edge was cut, and from the far edge to 180 when the west edge
  was cut.
- **Returns `null`** when `Around` with the same arguments cuts nothing: a circle that stays on one side of the line, or a circle that
  reaches a pole.
- **At most one side is cut.** The reach of a circle is at most 90 degrees, so it cannot pass the 180th meridian on both sides. There is
  at most one far box.
- **Throws** what `Around` throws, with the same `ParamName`s. Both use one private check, so the two cannot disagree.

```csharp
GeoBox.FarSide(0, 179.9, 50_000);     // MinLon -180, MaxLon -179.6503, CrossesAntimeridian true
GeoBox.FarSide(0, -179.9, 50_000);    // MinLon 179.6503, MaxLon 180, CrossesAntimeridian true
GeoBox.FarSide(45.8, 15.98, 1000);    // null: nothing is cut
GeoBox.FarSide(89.99, 10, 100_000);   // null: the circle reaches a pole
```

### Reading a cut box

When `CrossesAntimeridian` is `true`, also search `GeoBox.FarSide` with the same arguments. The box holds only the part of the circle on
the near side of the 180th meridian, and the far side is what `FarSide` returns.

For `GeoBox.Around(0, 179.9, 50_000)` the box runs from 179.4503 to 180. `GeoBox.FarSide(0, 179.9, 50_000)` gives the far range
[-180, -179.6503]. A search that needs the far side also searches that range, then keeps the candidates within the radius by
`HaversineMeters`.

---

## GeoPolygon

Polygons written as rings, and the test for whether a position is inside one. A static class with two members.

### Rings

A **ring** is an array of positions, each `[longitude, latitude]`. A polygon is one or more rings:

- **The usual layout** is a first ring for the outer edge and later rings for holes.
- **Islands** are rings inside a hole.
- **The rule** is even-odd over all rings together, so a point is inside when a ray from it crosses the rings' edges an odd number
  of times. A point in a hole is outside, and a point in an island in that hole is inside again. The order of the rings does not
  change the answer.

### `GeoPolygon.TryParseRings(string? geoJson)` → `double[][][]?`

Reads a polygon from JSON. Two shapes are accepted, in the coordinate layout of GeoJSON without the wrapping object:

- one ring: `[[lon,lat], …]`
- several rings: `[[[lon,lat], …], …]`

A position needs at least two numbers, each of which fits a `double`. A third number, such as an altitude, is ignored. A ring needs
at least three positions.

- **Returns** the rings as `double[][][]`, or `null` when the text is not one of those shapes. `null` also comes back for a blank
  or `null` input, for text that is not JSON, for an empty array, for a ring of fewer than three positions, for a position that
  is not an array of at least two numbers, for a value that is not a number (for example `"a"`), and for a number too large for a
  `double` (for example `1e400`).
- **Never throws.**
- **Does not range-check coordinates.** A longitude of 500 is read as given.
- **Does not limit the number of vertices.** A caller that caps a ring applies its own cap to the result.
- **Does not close a ring.** A ring may repeat its first position at the end, or not.

```csharp
var zone = GeoPolygon.TryParseRings("[[0,0],[10,0],[10,10],[0,10],[0,0]]");   // one ring of five positions
var holed = GeoPolygon.TryParseRings("[[[0,0],[10,0],[10,10],[0,10]],[[4,4],[6,4],[6,6],[4,6]]]");  // outer ring and a hole

GeoPolygon.TryParseRings("[[1,2],[3,4]]");     // null: a ring needs three positions
GeoPolygon.TryParseRings("[[1e400,1],[2,2],[3,3]]");   // null: 1e400 does not fit a double
GeoPolygon.TryParseRings(null);                // null
```

### `GeoPolygon.Contains(double latitude, double longitude, double[][][] rings)` → `bool`

Whether a position is inside the polygon made of the rings. The rings are those `TryParseRings` returns.

- **Returns** `true` when the position is inside, and `false` otherwise. An empty array of rings encloses nothing, so the answer
  is `false`.
- **A point exactly on an edge or a vertex** can come out either way. The answer there is not defined. Test points that are not on
  an edge when the answer matters.
- **A ring that repeats its first position** gives the same answers as the open ring. The repeat adds no edge.
- **A ring that crosses the 180th meridian** is not supported. Rings are plane shapes in longitude and latitude, so such a zone is
  split at the line.

**Throws:**
- `ArgumentNullException` when `rings` is `null`. Check the result of `TryParseRings` before calling.
- `ArgumentOutOfRangeException` when the latitude or the longitude is NaN, an infinity, or out of range. `ParamName` is `latitude`
  or `longitude`.
- `ArgumentException` when a ring is `null` or has fewer than three positions, when a position is `null` or has fewer than two
  values, or when a value is NaN or an infinity. `ParamName` is `rings`. The library refuses such a ring rather than skipping it.

```csharp
var rings = GeoPolygon.TryParseRings("[[0,0],[10,0],[10,10],[0,10],[0,0]]")
    ?? throw new InvalidOperationException("The zone is malformed.");

GeoPolygon.Contains(5, 5, rings);    // true: latitude 5, longitude 5, inside the square
GeoPolygon.Contains(11, 5, rings);   // false: latitude 11 is north of the square

// Holes: the outer ring 0..10, a hole 4..6, and an island 4.5..5.5 inside the hole.
var zone = GeoPolygon.TryParseRings(
    "[[[0,0],[10,0],[10,10],[0,10]],[[4,4],[6,4],[6,6],[4,6]],[[4.5,4.5],[5.5,4.5],[5.5,5.5],[4.5,5.5]]]")
    ?? throw new InvalidOperationException("The zone is malformed.");
GeoPolygon.Contains(1, 1, zone);     // true: in the outer ring, out of the hole
GeoPolygon.Contains(5, 4.2, zone);   // false: in the hole, outside the island
GeoPolygon.Contains(5, 5, zone);     // true: in the island inside the hole

try
{
    GeoPolygon.Contains(5, 5, [[[0.0, 0.0], [1.0, 1.0]]]);   // a ring of two positions
}
catch (ArgumentException error)
{
    Console.WriteLine(error.ParamName);   // rings
}
```

---

## Ways it fails

| Member | Refuses with | Returns instead |
|---|---|---|
| `GeoBounds.IsUsable` | nothing: it never throws | `false` for NaN, infinities, out of range, (0, 0), -0.0 |
| `GeoDistance.HaversineMeters` | `ArgumentOutOfRangeException` (`latitude1`, `longitude1`, `latitude2`, `longitude2`) | nothing: a bad coordinate is never a number |
| `GeoDistance.MetersPerDegreeLongitude` | `ArgumentOutOfRangeException` (`latitude`) | nothing |
| `GeoBox.Around` | `ArgumentOutOfRangeException` (`latitude` for an unusable centre, `radiusMeters` for a bad radius) | a box with `CrossesAntimeridian` set when it was cut |
| `GeoBox.FarSide` | `ArgumentOutOfRangeException` (`latitude`, `radiusMeters`), as `GeoBox.Around` | `null` when `Around` cuts nothing, or the circle reaches a pole |
| `GeoPolygon.TryParseRings` | nothing: never throws | `null` for every malformed text, including a number too large for a `double` |
| `GeoPolygon.Contains` | `ArgumentNullException` (`rings` null); `ArgumentOutOfRangeException` (`latitude`, `longitude`); `ArgumentException` (`rings`, a ring or position is malformed) | `false` when outside, or for an empty array of rings |

---

## Not in this library

- Distances on the WGS 84 ellipsoid, and geodesic routes.
- Polygon area, buffers, intersections, and other geometry operations.
- Circle-against-polygon tests.
- Spatial indexes and country or region data. An application builds those on the members here.
- GeoJSON geometry types other than rings of positions.
