using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;

namespace VeganAdda.Web.Services;

public partial class CommunityService
{
    private static DateOnly ReadBirthDetails(ProfileInput input)
    {
        if (input.BirthYear is not { } year || input.BirthMonth is not { } month)
            throw new ApiException(400, "Add your birth month and year.");
        if (year < 1 || year > 9999 || month < 1 || month > 12 || input.BirthDay is < 1 or > 31)
            throw new ApiException(400, "Choose valid birth details.");
        var days = DateTime.DaysInMonth(year, month);
        if (input.BirthDay > days) throw new ApiException(400, "Choose a day that exists in your birth month.");
        var birth = new DateOnly(year, month, input.BirthDay ?? days);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (birth > today.AddYears(-18) || birth <= today.AddYears(-121))
            throw new ApiException(400, "Your birth details must indicate an age between 18 and 120. If you turned 18 this month, add your birth day.");
        return birth;
    }
    private static bool BirthdayToday(DateOnly? birthDate, bool hasExactBirthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return hasExactBirthDate && birthDate is { } birth && birth.Month == today.Month && birth.Day == today.Day;
    }
    private static int? ParseAge(string value) => value == "" ? null : int.TryParse(value, out var age) ? age : throw new ApiException(400, "Choose a valid age range.");
    private static int? Age(DateOnly? birthDate)
    {
        if (birthDate is not { } birth) return null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var years = today.Year - birth.Year;
        return today < birth.AddYears(years) ? years - 1 : years;
    }

    private async Task<Page<Person>> SearchPeople(DiscoveryInput input)
    {
        var nearby = input.Latitude.HasValue || input.Longitude.HasValue || input.RadiusKm.HasValue;
        if (!Viewer.HasValue && (nearby || input.MinAge.HasValue || input.MaxAge.HasValue || input.IncludeNonVegan
            || !string.IsNullOrEmpty(input.Search) || !string.IsNullOrEmpty(input.Interest) || !string.IsNullOrEmpty(input.Intent) || !string.IsNullOrEmpty(input.Event) || input.CheckedIn))
            throw new ApiException(401, "Sign in to use member filters.");
        if (input.MinAge is < 18 or > 120 || input.MaxAge is < 18 or > 120 || input.MinAge > input.MaxAge)
            throw new ApiException(400, "Choose an age range between 18 and 120.");
        if (nearby && (input.Latitude is not double lat || !double.IsFinite(lat) || lat < -90 || lat > 90
            || input.Longitude is not double lon || !double.IsFinite(lon) || lon < -180 || lon > 180 || input.RadiusKm is not (>= 1 and <= 500)))
            throw new ApiException(400, "Choose a valid current location and a radius between 1 and 500 km.");
        var people = VisiblePeople().Where(p => p.Id != Viewer);
        people = input.IncludeNonVegan ? people.Where(p => p.IsVegan != null) : people.Where(p => p.IsVegan == true);
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
                    && db.Events.Any(e => e.Id == eventId && e.Start <= now && e.End > now));
            }
        }
        var page = Math.Clamp(input.Page, 1, 10000);
        var total = await people.CountAsync();
        var rows = await people.OrderByDescending(p => p.Featured && p.Public && p.FeatureConsent).ThenBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((page - 1) * 12).Take(12).ToListAsync();
        var dtos = new List<Person>(); foreach (var p in rows) dtos.Add(await PersonDto(p));
        return new(dtos, total, page);
    }
}
