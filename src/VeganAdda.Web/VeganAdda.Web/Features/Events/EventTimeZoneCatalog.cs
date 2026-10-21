using System.IO.Compression;
using System.Text.Json;
using VeganAdda.Contracts;

namespace VeganAdda.Web.Features.Events;

public static class EventTimeZoneCatalog
{
    private sealed record Entry(string Country, string State, string City, string TimeZone);
    private static readonly Lazy<Dictionary<(string Country, string State, string City), string>> TimeZones = new(Read);

    private static Dictionary<(string Country, string State, string City), string> Read()
    {
        using var resource = typeof(EventTimeZoneCatalog).Assembly.GetManifestResourceStream("EventLocations.time-zones.json.gz")
            ?? throw new InvalidOperationException("The event time zone catalog is missing.");
        using var compressed = new GZipStream(resource, CompressionMode.Decompress);
        var entries = JsonSerializer.Deserialize<Entry[]>(compressed, Wire.Json) ?? [];
        var zones = new Dictionary<(string Country, string State, string City), string>();
        foreach (var group in entries.GroupBy(entry => Key(entry.Country, entry.State, entry.City)))
        {
            var values = group.Select(entry => entry.TimeZone).Distinct(StringComparer.Ordinal).Take(2).ToArray();
            if (values.Length == 1) zones[group.Key] = values[0];
        }
        return zones;
    }

    public static string Resolve(string country, string state, string city)
    {
        var key = Key(country, state, city);
        if (TimeZones.Value.TryGetValue(key, out var zone))
        {
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone); return zone; }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new ApiException(400, "We couldn't determine the time zone for this city. Choose a listed city with a known local time zone.");
    }

    private static (string Country, string State, string City) Key(string country, string state, string city)
        => (country.Trim().ToUpperInvariant(), state.Trim().ToUpperInvariant(), city.Trim().ToUpperInvariant());
}
