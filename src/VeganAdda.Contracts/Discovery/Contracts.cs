namespace VeganAdda.Contracts;

public class DiscoveryInput
{
    public string Event { get; set; } = "";
    public bool CheckedIn { get; set; }
    public string Country { get; set; } = "";
    public string State { get; set; } = "";
    public string City { get; set; } = "";
    public string Search { get; set; } = "";
    public string Interest { get; set; } = "";
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
