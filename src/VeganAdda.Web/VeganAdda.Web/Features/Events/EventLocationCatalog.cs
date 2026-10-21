using System.Collections.Concurrent;
using System.Text.Json;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Features.Events;

// The catalog also supports location filters for older events that only stored a city.
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

    public static IQueryable<Gathering> Filter(IQueryable<Gathering> events, string country, string state, string city)
    {
        country = country.ToLowerInvariant(); state = state.ToLowerInvariant(); city = city.ToLowerInvariant();
        if (country != "" || state != "")
        {
            var legacyCities = Cities(country, state);
            events = events.Where(e => (e.Country != "" && (country == "" || e.Country.ToLower() == country) && (state == "" || e.State.ToLower() == state))
                || (e.Country == "" && legacyCities.Contains(e.City.ToLower())));
        }
        if (city != "" && city != "all india") events = events.Where(e => e.City.ToLower() == city);
        return events;
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
