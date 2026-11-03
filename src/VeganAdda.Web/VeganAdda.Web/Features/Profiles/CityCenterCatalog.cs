using System.IO.Compression;
using System.Text.Json;
using VeganAdda.Contracts;

namespace VeganAdda.Web.Features.Profiles;

public static class CityCenterCatalog
{
    public sealed record Entry(string Country, string State, string City, double Latitude, double Longitude);
    private static readonly Lazy<Dictionary<(string, string, string), Entry>> Centers = new(Read);
    private static (string, string, string) Key(string country, string state, string city) =>
        (country.Trim().ToUpperInvariant(), state.Trim().ToUpperInvariant(), city.Trim().ToUpperInvariant());

    private static Dictionary<(string, string, string), Entry> Read()
    {
        using var stream = typeof(CityCenterCatalog).Assembly.GetManifestResourceStream("VeganAdda.city-centers.json.gz")
            ?? throw new InvalidOperationException("The city-center catalog is missing.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        var entries = JsonSerializer.Deserialize<Entry[]>(gzip, Wire.Json) ?? [];
        return entries.GroupBy(e => Key(e.Country, e.State, e.City))
            .Where(g => g.Select(e => (e.Latitude, e.Longitude)).Distinct().Take(2).Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public static Entry? Find(string country, string state, string city) => Centers.Value.GetValueOrDefault(Key(country, state, city));
    public static IEnumerable<Entry> All() => Centers.Value.Values;
}
