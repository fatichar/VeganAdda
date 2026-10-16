using Microsoft.EntityFrameworkCore;

namespace VeganAdda.Web.Data;

public static class DevelopmentSeed
{
    public static readonly string[] Subjects = ["aarav", "ananya", "rohan", "sunita", "kabir", "new", "admin"];
    public static async Task Run(CommunityDb db, IConfiguration config, string contentRoot)
    {
        if (await db.Accounts.AnyAsync(a => a.Subject.StartsWith("dev:"))) return;
        var media = config["Storage:MediaPath"] ?? Path.Combine(contentRoot, "uploads"); Directory.CreateDirectory(media);
        var design = Path.GetFullPath(Path.Combine(contentRoot, "../../../design-reference"));
        string Asset(string prefix, string id)
        {
            if (!Directory.Exists(design)) return "";
            var dir = Directory.GetDirectories(design).FirstOrDefault(d => Path.GetFileName(d).StartsWith(prefix));
            if (dir == null) return "";
            var name = id.Replace("-", "") + ".png"; File.Copy(Path.Combine(dir, "screen.png"), Path.Combine(media, name), true); return "/media/" + name;
        }
        var names = new[] { "Aarav Sharma", "Ananya Sharma", "Rohan Mehta", "Sunita Rao", "Kabir Sen", "New Member", "Community Admin" };
        var accounts = Subjects.Select((s, i) => new Account { Id = Guid.Parse($"10000000-0000-0000-0000-{i+1:000000000000}"), Subject = "dev:" + s, Name = names[i], Demo = true, Admin = s == "admin" }).ToArray();
        db.Accounts.AddRange(accounts);
        var bios = new[] {
            "Here for shared meals, thoughtful conversations, and a kinder community. Always up for a weekend potluck.",
            "Architect & cruelty-free interior consultant. Love morning park runs, low-waste living, and ethical design spaces.",
            "Software engineer building open-source solar tools. Weekends spent volunteering in animal rescue sanctuaries and trail scouting.",
            "Plant-based home baker & culinary educator. Sharing traditional heritage dishes made dairy-free with cold-pressed oils.",
            "Documentary photographer focusing on ethical wildlife & indie music scenes. Always up for strong South Indian filter coffee & discussions."
        };
        for (int i = 0; i < 5; i++) db.Profiles.Add(new()
        {
            Id = accounts[i].Id, Name = names[i], City = i == 4 ? "Delhi NCR" : i == 1 || i == 3 ? "Bengaluru" : "Pune", Bio = bios[i], Public = true,
            FeatureConsent = true, Featured = i == 1, AdultConfirmedAt = DateTimeOffset.UtcNow,
            LookingFor = i == 2 ? ["Friendship", "Activism", "Dating"] : ["Friendship", "Business & networking"],
            Interests = i == 1 ? ["Design", "Running", "Zero waste", "Rescues"] : i == 2 ? ["Tech", "Sanctuaries", "Hiking"] : i == 3 ? ["Baking", "Gardening", "Heritage food"] : i == 4 ? ["Photography", "Music", "Coffee"] : ["Cooking", "Running", "Volunteering"],
            Photo = i == 2 ? Asset("natural_candid", accounts[i].Id.ToString()) : i == 3 ? Asset("authentic_candid", accounts[i].Id.ToString()) : i == 4 ? Asset("portrait_of", accounts[i].Id.ToString()) : ""
        });
        var evt = new Gathering { Id = Guid.Parse("20000000-0000-0000-0000-000000000001"), Title = "Sunday community potluck", Description = "Development sample event. Bring a favourite plant-based dish and meet your local community. This is fictional test data and is not a real gathering.", City = "Pune", Venue = "Sample community garden (test venue)", Organizer = "VeganAdda demo team", Start = DateTimeOffset.UtcNow.AddDays(7), End = DateTimeOffset.UtcNow.AddDays(7).AddHours(3), Published = true, Featured = true, Demo = true, Cover = Asset("vibrant_welcoming", "20000000000000000000000000000001") };
        db.Events.Add(evt);
        db.Events.Add(new() { Title = "Ekatva", City = "Delhi NCR", Description = "Draft: owner must confirm official dates, venue, organizer, cover rights and any external registration link before publication.", Published = false });
        await db.SaveChangesAsync();
    }
}
