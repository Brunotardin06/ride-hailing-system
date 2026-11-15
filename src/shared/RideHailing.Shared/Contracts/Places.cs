namespace RideHailing.Shared.Contracts;

/// <summary>A point on the map (WGS84 degrees, like GPS).</summary>
public readonly record struct GeoPoint(double Lat, double Lng);

/// <summary>
/// The demo places with their coordinates. MUST match frontend/src/api/places.ts (same names, same points),
/// so "Eindhoven Centraal" means the same location in the browser and in the backend.
/// </summary>
public static class Places
{
    private static readonly Dictionary<string, GeoPoint> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Fontys Rachelsmolen (R1)"] = new(51.4512, 5.4810),
        ["Eindhoven Centraal"] = new(51.4433, 5.4797),
        ["Strijp-S"] = new(51.4480, 5.4570),
        ["Philips Stadion"] = new(51.4417, 5.4673),
        ["Eindhoven Airport"] = new(51.4501, 5.3745),
        ["High Tech Campus"] = new(51.4110, 5.4590),
        ["Van Abbemuseum"] = new(51.4347, 5.4818),
        ["TU/e Campus"] = new(51.4480, 5.4900),
    };

    /// <summary>Coordinates of a known place; false for free text the passenger typed.</summary>
    public static bool TryGet(string name, out GeoPoint point) => ByName.TryGetValue(name.Trim(), out point);
}

public static class Geo
{
    /// <summary>Great-circle distance in metres (haversine formula): "as the crow flies", not by road.</summary>
    public static double DistanceMeters(GeoPoint a, GeoPoint b)
    {
        const double earthRadius = 6_371_000;
        static double Rad(double deg) => deg * Math.PI / 180;
        var dLat = Rad(b.Lat - a.Lat);
        var dLng = Rad(b.Lng - a.Lng);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * earthRadius * Math.Asin(Math.Sqrt(h));
    }
}
