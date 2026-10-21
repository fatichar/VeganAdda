namespace VeganAdda.Contracts;

// Public/member projection: never include private birth details, coordinates, or identity credentials.
public record Person(Guid Id, string Name, string City, string Bio, string Photo, string[] Interests,
    string[] LookingFor, string? Instagram, string? LinkedIn, string? Website, bool Featured,
    bool Saved, string Relationship, bool Demo = false, string Country = "India", string State = "", bool? IsVegan = null, bool VeganVerified = false, int? Age = null, bool IsBirthday = false, string Profession = "")
{
    public bool VeganStatusUnverified => IsVegan == true && !VeganVerified;
}
public class ProfileInput
{
    public int? BirthYear { get; set; }
    public int? BirthMonth { get; set; }
    public int? BirthDay { get; set; }
    public bool SavePreciseLocation { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "India";
    public string State { get; set; } = "";
    public string Bio { get; set; } = "";
    public string Profession { get; set; } = "";
    public string Gender { get; set; } = "";
    public string Photo { get; set; } = "";
    public string[] Interests { get; set; } = [];
    public string[] LookingFor { get; set; } = ["Friendship"];
    public string? Instagram { get; set; }
    public string? LinkedIn { get; set; }
    public string? Website { get; set; }
    public bool Public { get; set; }
    public bool DefaultAttendeeVisible { get; set; } = true;
    public bool? IsVegan { get; set; }
    public bool VeganStatusUnverified => IsVegan == true && !VeganVerified;
    public bool HasReferrer { get; set; }
    public bool VeganVerified { get; set; }
    public string? ReviewNote { get; set; }
    public string? ReferralCode { get; set; }
    public bool CanRefer { get; set; }
}

public interface IProfilesApi
{
    Task<ProfileInput> GetProfile();
    Task<Person> GetPerson(Guid id);
    Task<Page<Person>> SearchPeople(DiscoveryInput input);
    Task<Ok> SaveProfile(ProfileInput input);
}
