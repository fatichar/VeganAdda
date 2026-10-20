using Microsoft.EntityFrameworkCore;

namespace VeganAdda.Web.Data;

public static class DevelopmentSeed
{
    public static readonly string[] Subjects = ["aarav", "ananya", "rohan", "sunita", "kabir", "new", "admin", "meera", "dev", "isha", "arjun"];
    public static async Task Run(CommunityDb db, IConfiguration config, string contentRoot)
    {
        var media = config["Storage:MediaPath"] ?? Path.Combine(contentRoot, "uploads"); Directory.CreateDirectory(media);
        var design = Path.GetFullPath(Path.Combine(contentRoot, "../../../design-reference"));
        string Asset(string prefix, string id)
        {
            if (!Directory.Exists(design)) return "";
            var dir = Directory.GetDirectories(design).FirstOrDefault(d => Path.GetFileName(d).StartsWith(prefix));
            if (dir == null) return "";
            var name = id.Replace("-", "") + ".png"; File.Copy(Path.Combine(dir, "screen.png"), Path.Combine(media, name), true); return "/media/" + name;
        }
        var names = new[] { "Aarav Sharma", "Ananya Sharma", "Rohan Mehta", "Sunita Rao", "Kabir Sen", "New Member", "Community Admin", "Meera Iyer", "Dev Patel", "Isha Nair", "Arjun Das" };
        var accounts = new Account[Subjects.Length];
        for (var i = 0; i < Subjects.Length; i++)
        {
            var subject = "dev:" + Subjects[i];
            accounts[i] = await db.Accounts.SingleOrDefaultAsync(a => a.Subject == subject) ?? new Account { Id = Guid.Parse($"10000000-0000-0000-0000-{i+1:000000000000}"), Subject = subject, Name = names[i], Demo = true, Admin = Subjects[i] == "admin" };
            if (db.Entry(accounts[i]).State == EntityState.Detached) db.Accounts.Add(accounts[i]);
        }
        var bios = new[] {
            "Here for shared meals, thoughtful conversations, and a kinder community. Always up for a weekend potluck.",
            "Architect & cruelty-free interior consultant. Love morning park runs, low-waste living, and ethical design spaces.",
            "Software engineer building open-source solar tools. Weekends spent volunteering in animal rescue sanctuaries and trail scouting.",
            "Plant-based home baker & culinary educator. Sharing traditional heritage dishes made dairy-free with cold-pressed oils.",
            "Documentary photographer focusing on ethical wildlife & indie music scenes. Always up for strong South Indian filter coffee & discussions."
        };
        for (int i = 0; i < 5; i++)
        {
            if (await db.Profiles.AnyAsync(p => p.Id == accounts[i].Id)) continue;
            db.Profiles.Add(new()
            {
                Id = accounts[i].Id, Name = names[i], City = i == 4 ? "Delhi NCR" : i == 1 || i == 3 ? "Bengaluru" : "Pune", State = i == 4 ? "Delhi" : i == 1 || i == 3 ? "Karnataka" : "Maharashtra", Bio = bios[i], Public = true, Approved = true, IsVegan = true, TestProfile = true,
                FeatureConsent = true, Featured = i == 1, AdultConfirmedAt = DateTimeOffset.UtcNow,
                LookingFor = i == 2 ? ["Friendship", "Activism", "Dating"] : ["Friendship", "Business & networking"],
                Interests = i == 1 ? ["Design", "Running", "Zero waste", "Rescues"] : i == 2 ? ["Tech", "Sanctuaries", "Hiking"] : i == 3 ? ["Baking", "Gardening", "Heritage food"] : i == 4 ? ["Photography", "Music", "Coffee"] : ["Cooking", "Running", "Volunteering"],
                Photo = i == 2 ? Asset("natural_candid", accounts[i].Id.ToString()) : i == 3 ? Asset("authentic_candid", accounts[i].Id.ToString()) : i == 4 ? Asset("portrait_of", accounts[i].Id.ToString()) : ""
            });
        }
        var extraProfiles = new (int Account, string City, string State, string Bio, string[] Interests)[]
        {
            (5, "Pune", "Maharashtra", "New to the community and keen to meet people over a shared meal.", ["Cooking", "Books", "Volunteering"]),
            (7, "Mumbai", "Maharashtra", "Community gardener who enjoys plant-based cooking and weekend walks.", ["Gardening", "Cooking", "Walking"]),
            (8, "Chennai", "Tamil Nadu", "Designer interested in low-waste living, local food, and animal care.", ["Design", "Zero waste", "Animals"]),
            (9, "Kochi", "Kerala", "Music fan and home cook looking to make thoughtful community connections.", ["Music", "Cooking", "Travel"]),
            (10, "Jaipur", "Rajasthan", "Teacher who loves nature, reading, and discovering vegan-friendly places.", ["Teaching", "Nature", "Reading"])
        };
        foreach (var sample in extraProfiles)
        {
            var account = accounts[sample.Account];
            if (await db.Profiles.AnyAsync(p => p.Id == account.Id)) continue;
            db.Profiles.Add(new Profile
            {
                Id = account.Id, Name = names[sample.Account], City = sample.City, State = sample.State, Country = "India",
                Bio = sample.Bio, Public = true, Approved = true, IsVegan = true, TestProfile = true,
                AdultConfirmedAt = DateTimeOffset.UtcNow, LookingFor = ["Friendship", "Community"], Interests = sample.Interests
            });
        }
        var evt = new Gathering { Id = Guid.Parse("20000000-0000-0000-0000-000000000001"), Title = "Sunday community potluck", Description = "Development sample event. Bring a favourite plant-based dish and meet your local community. This is fictional test data and is not a real gathering.", City = "Pune", Venue = "Sample community garden (test venue)", Organizer = "VeganAdda demo team", Start = DateTimeOffset.UtcNow.AddDays(7), End = DateTimeOffset.UtcNow.AddDays(7).AddHours(3), Published = true, Featured = true, Demo = true, Cover = Asset("vibrant_welcoming", "20000000000000000000000000000001") };
        if (!await db.Events.AnyAsync(e => e.Id == evt.Id)) db.Events.Add(evt);
        if (!await db.Events.AnyAsync(e => e.Title == "Ekatva")) db.Events.Add(new() { Title = "Ekatva", City = "Delhi NCR", Description = "Draft: owner must confirm official dates, venue, organizer, cover rights and any external registration link before publication.", Published = false });
        await db.SaveChangesAsync();
    }
}
