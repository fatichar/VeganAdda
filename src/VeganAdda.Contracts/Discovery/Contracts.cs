namespace VeganAdda.Contracts;

public class DiscoveryInput
{
    public string Sort { get; set; } = DiscoverySort.Age;
    public bool ConnectionsOnly { get; set; }
    public string Event { get; set; } = "";
    public bool CheckedIn { get; set; }
    public string Country { get; set; } = "";
    public string State { get; set; } = "";
    public string City { get; set; } = "";
    public string Search { get; set; } = "";
    public string Interest { get; set; } = "";
    public string[] Genders { get; set; } = [];
    public string Intent { get; set; } = "";
    public bool VerifiedVegansOnly { get; set; }
    public bool IncludeNonVegan { get; set; }
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusKm { get; set; }
    public int Page { get; set; } = 1;
}

public static class DiscoverySort
{
    public const string Name = "name";
    public const string NameDescending = "name-desc";
    public const string Age = "age";
    public const string AgeDescending = "age-desc";
    public const string Distance = "distance";
    public const string DistanceDescending = "distance-desc";
    public static bool IsDistance(string value) => value is Distance or DistanceDescending;
    public static bool IsValid(string? value) => value is Name or NameDescending or Age or AgeDescending or Distance or DistanceDescending;
}
