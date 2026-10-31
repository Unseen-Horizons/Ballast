# Ballast.Geo

Coordinate checks, great-circle distances, the length of a degree, bounding boxes for a nearby search, and polygon
containment, for latitude and longitude in degrees.

- `GeoBounds.IsUsable`: whether a pair is a finite position in range, and not (0, 0).
- `GeoDistance`: the distance in metres between two positions, and the length of a degree of latitude and longitude.
- `GeoBox.Around`: the box a nearby search filters on. It reports when it is cut at the 180th meridian.
- `GeoPolygon`: reads a polygon from JSON rings, and tests whether a position is inside it.

It depends on .NET alone, so a server and a phone app can both use it.

## Install

```
dotnet add package Ballast.Geo --prerelease
```

## Example

```csharp
using Ballast.Geo;

// A zone drawn as one ring of [longitude, latitude] positions, as GeoJSON writes them.
var zone = GeoPolygon.TryParseRings("[[15.9,45.7],[16.1,45.7],[16.1,45.9],[15.9,45.9],[15.9,45.7]]")
    ?? throw new InvalidOperationException("The zone is malformed.");

// The point is given latitude first, then longitude.
var inside = GeoPolygon.Contains(45.8, 16.0, zone);
Console.WriteLine($"Inside the zone: {inside}");

// The straight-line distance between two points, in metres.
var metres = GeoDistance.HaversineMeters(45.8, 15.98, 43.5, 16.44);
Console.WriteLine($"Distance: {metres:F0} m");

// The box a nearby search filters on: every position within 1 km of the centre.
var box = GeoBox.Around(45.8, 15.98, 1000);
Console.WriteLine($"Crosses the 180th meridian: {box.CrossesAntimeridian}");
```

The output is:

```
Inside the zone: True
Distance: 258323 m
Crosses the 180th meridian: False
```

The options of each member, and every way each one fails, are in `docs/Geo.md` in the repository.
