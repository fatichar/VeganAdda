using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Shared;
using VeganAdda.Web.Features.Profiles;

namespace VeganAdda.Web.Features.Discovery;

public sealed class DiscoveryService(FeatureContext context, ProfileVisibility visibility, PeopleProjection projection) : FeatureService(context)
{
    // Signed-out visitors see a single page of teaser cards.
    public const int AnonymousPages = 1;
    public async Task<Page<Person>> SearchPeople(DiscoveryInput input)
    {
        await ActiveViewer();
        if (!DiscoverySort.IsValid(input.Sort)) throw new ApiException(400, "Choose a supported sort order.");
        var genders = input.Genders ?? throw new ApiException(400, "Choose supported genders.");
        var distanceSort = DiscoverySort.IsDistance(input.Sort);
        var locationSupplied = input.Latitude.HasValue || input.Longitude.HasValue || input.RadiusKm.HasValue;
        var nearby = input.RadiusKm.HasValue;
        if (!Viewer.HasValue && (input.ConnectionsOnly || locationSupplied || distanceSort || input.MinAge.HasValue || input.MaxAge.HasValue || input.IncludeNonVegan || genders.Length > 0
            || !string.IsNullOrEmpty(input.Search) || !string.IsNullOrEmpty(input.Interest) || !string.IsNullOrEmpty(input.Intent) || !string.IsNullOrEmpty(input.Event) || input.CheckedIn))
            throw new ApiException(401, "Sign in to use member filters.");
        if (!Viewer.HasValue && input.Page > AnonymousPages) throw new ApiException(401, "Sign in to see more people.");
        if (input.MinAge is < 18 or > 120 || input.MaxAge is < 18 or > 120 || input.MinAge > input.MaxAge)
            throw new ApiException(400, "Choose an age range between 18 and 120.");
        if ((locationSupplied || distanceSort) && (input.Latitude is not double lat || !double.IsFinite(lat) || lat < -90 || lat > 90
            || input.Longitude is not double lon || !double.IsFinite(lon) || lon < -180 || lon > 180
            || (input.RadiusKm is not (>= 1 and <= 500) && !(distanceSort && input.RadiusKm == null))))
            throw new ApiException(400, "Choose a valid current location and a radius between 1 and 500 km.");
        var people = visibility.Query().Where(p => p.Id != Viewer);
        if (Viewer is { } viewer)
            people = people.Where(p => db.Relationships.Any(r => r.State == "accepted"
                && ((r.A == viewer && r.B == p.Id) || (r.B == viewer && r.A == p.Id))) == input.ConnectionsOnly);
        if (genders.Length > 0)
        {
            if (genders.Length > Wire.GenderFilters.Length || genders.Any(gender => !Wire.GenderFilters.Contains(gender))) throw new ApiException(400, "Choose supported genders.");
            people = people.Where(p => genders.Contains(p.Gender) || (genders.Contains("Unspecified") && p.Gender == ""));
        }
        people = input.IncludeNonVegan ? people.Where(p => p.IsVegan != null) : people.Where(p => p.IsVegan == true);
        if (input.VerifiedVegansOnly) people = people.Where(p => p.IsVegan == true && p.VeganVerified);
        if (nearby)
        {
            // Evaluate distance in PostgreSQL before counting/pagination. Search coordinates
            // exist only in this request; never write them to the viewer's profile.
            const double radians = Math.PI / 180d;
            var latitude = input.Latitude!.Value; var longitude = input.Longitude!.Value;
            var latitudeRadians = latitude * radians;
            var angularRadius = input.RadiusKm!.Value / 6371.0088d;
            var latDelta = angularRadius / radians;
            var limit = Math.Pow(Math.Sin(angularRadius / 2), 2);
            people = people.Where(p => p.Latitude.HasValue && p.Longitude.HasValue
                && p.Latitude >= latitude - latDelta && p.Latitude <= latitude + latDelta
                && Math.Pow(Math.Sin((p.Latitude.Value - latitude) * radians / 2), 2)
                 + Math.Cos(latitudeRadians) * Math.Cos(p.Latitude.Value * radians)
                 * Math.Pow(Math.Sin((p.Longitude.Value - longitude) * radians / 2), 2) <= limit);
        }
        else
        {
            var country = Text(input.Country, 80).ToLowerInvariant(); var state = Text(input.State, 80).ToLowerInvariant(); var city = Text(input.City, 80).ToLowerInvariant();
            if (country != "") people = people.Where(p => p.Country.ToLower() == country);
            if (state != "") people = people.Where(p => p.State.ToLower() == state);
            if (city != "" && city != "all india") people = people.Where(p => p.City.ToLower() == city);
        }
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (input.MinAge is { } minAge) { var latest = today.AddYears(-minAge); people = people.Where(p => p.BirthDate <= latest); }
        if (input.MaxAge is { } maxAge) { var earliest = today.AddYears(-maxAge - 1); people = people.Where(p => p.BirthDate > earliest); }
        if ((input.Search ?? "").Trim() is { Length: > 0 } search)
        {
            var term = "%" + Text(search, 200).Replace("%", "").Replace("_", "") + "%";
            people = people.Where(p => EF.Functions.ILike(p.Name, term) || EF.Functions.ILike(p.Bio, term) || p.Interests.Any(i => EF.Functions.ILike(i, term)));
        }
        if (!string.IsNullOrEmpty(input.Interest)) people = people.Where(p => p.Interests.Contains(input.Interest));
        if (!string.IsNullOrEmpty(input.Intent))
        {
            if (!Wire.Intents.Contains(input.Intent)) throw new ApiException(400, "Choose a supported intention.");
            if (input.Intent == "Dating" && !await db.Profiles.AnyAsync(p => p.Id == Viewer && p.LookingFor.Contains("Dating"))) throw Denied();
            people = people.Where(p => p.LookingFor.Contains(input.Intent));
        }
        if (input.CheckedIn && string.IsNullOrEmpty(input.Event)) throw new ApiException(400, "Choose an event to find checked-in members.");
        if (!string.IsNullOrEmpty(input.Event))
        {
            if (!Guid.TryParse(input.Event, out var eventId)) throw new ApiException(400, "Choose a valid event.");
            if (!await db.Events.AnyAsync(e => e.Id == eventId && e.Published)) throw Missing();
            people = people.Where(p => db.Attendance.Any(a => a.EventId == eventId && a.UserId == p.Id && a.Visible));
            if (input.CheckedIn)
            {
                var now = DateTimeOffset.UtcNow;
                people = people.Where(p => db.Attendance.Any(a => a.EventId == eventId && a.UserId == p.Id && a.CheckedInUntil > now)
                    && db.Events.Any(e => e.Id == eventId && e.Start <= now && (e.End ?? e.Start.Value.AddHours(4)) > now));
            }
        }
        var page = Math.Clamp(input.Page, 1, 10000);
        var total = await people.CountAsync();
        IQueryable<Profile> ordered;
        if (distanceSort)
        {
            const double radians = Math.PI / 180d;
            var latitude = input.Latitude!.Value; var longitude = input.Longitude!.Value;
            // The haversine term increases with great-circle distance. Order in SQL,
            // retaining profiles without coordinates at the end in both directions.
            var distances = people.Select(p => new { Profile = p, Distance = p.Latitude.HasValue && p.Longitude.HasValue
                ? (double?)(Math.Pow(Math.Sin((p.Latitude.Value - latitude) * radians / 2), 2)
                    + Math.Cos(latitude * radians) * Math.Cos(p.Latitude.Value * radians)
                    * Math.Pow(Math.Sin((p.Longitude.Value - longitude) * radians / 2), 2)) : null });
            ordered = (input.Sort == DiscoverySort.Distance
                ? distances.OrderBy(p => p.Distance == null).ThenBy(p => p.Distance).ThenBy(p => p.Profile.Id)
                : distances.OrderBy(p => p.Distance == null).ThenByDescending(p => p.Distance).ThenBy(p => p.Profile.Id)).Select(p => p.Profile);
        }
        else if (input.Sort is DiscoverySort.Name or DiscoverySort.NameDescending)
            ordered = input.Sort == DiscoverySort.Name
                ? people.OrderBy(p => p.Name).ThenBy(p => p.Id)
                : people.OrderByDescending(p => p.Name).ThenBy(p => p.Id);
        else
            ordered = input.Sort == DiscoverySort.Age
                ? people.OrderBy(p => p.BirthDate == null).ThenByDescending(p => p.BirthDate).ThenBy(p => p.Id)
                : people.OrderBy(p => p.BirthDate == null).ThenBy(p => p.BirthDate).ThenBy(p => p.Id);
        var rows = await ordered
            .Skip((page - 1) * 12).Take(12).ToListAsync();
        var dtos = await projection.Many(rows);
        return new(dtos, total, page);
    }
}
