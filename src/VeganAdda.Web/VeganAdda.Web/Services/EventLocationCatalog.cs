using System.Collections.Concurrent;
using System.Text.Json;
using VeganAdda.Contracts;

namespace VeganAdda.Web.Services;

// Events currently store a city, so broader filters resolve that city against the
// same location catalog used by the picker. Embed it so published builds work too.
public static class EventLocationCatalog
{
    private record Country(string Code, string Name);
    private record Region(string Name, string[] Cities);
    private static readonly Lazy<Country[]> Countries = new(() => Read<Country>("countries"));
    private static readonly ConcurrentDictionary<string, Region[]> Regions = new();

    private static T[] Read<T>(string file)
    {
        using var stream = typeof(EventLocationCatalog).Assembly.GetManifestResourceStream($"EventLocations.{file}.json")
            ?? throw new InvalidOperationException("The event location catalog is missing.");
        return JsonSerializer.Deserialize<T[]>(stream, Wire.Json) ?? [];
    }

    public static string[] Cities(string country, string state)
    {
        var match = Countries.Value.FirstOrDefault(c => c.Name.Equals(country, StringComparison.OrdinalIgnoreCase));
        if (match == null) return [];
        var regions = Regions.GetOrAdd(match.Code, Read<Region>);
        var cities = regions.Where(r => state == "" || r.Name.Equals(state, StringComparison.OrdinalIgnoreCase))
            .SelectMany(r => r.Cities).Select(c => c.ToLowerInvariant()).ToHashSet();
        if (match.Code == "IN")
        {
            if (cities.Contains("delhi") || cities.Contains("new delhi")) cities.Add("delhi ncr");
            if (cities.Contains("bengaluru")) cities.Add("bangalore");
            if (cities.Contains("mumbai")) cities.Add("bombay");
            if (cities.Contains("kolkata")) cities.Add("calcutta");
            if (cities.Contains("chennai")) cities.Add("madras");
        }
        return cities.ToArray();
    }
}
