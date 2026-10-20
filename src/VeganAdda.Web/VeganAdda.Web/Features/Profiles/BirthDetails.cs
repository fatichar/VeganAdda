using VeganAdda.Contracts;
using VeganAdda.Web.Features.Shared;
using static VeganAdda.Web.Features.Profiles.BirthDetails;

namespace VeganAdda.Web.Features.Profiles;

public static class BirthDetails
{
    public static DateOnly ReadBirthDetails(ProfileInput input)
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
    public static bool BirthdayToday(DateOnly? birthDate, bool hasExactBirthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return hasExactBirthDate && birthDate is { } birth && birth.Month == today.Month && birth.Day == today.Day;
    }
    public static int? Age(DateOnly? birthDate)
    {
        if (birthDate is not { } birth) return null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var years = today.Year - birth.Year;
        return today < birth.AddYears(years) ? years - 1 : years;
    }
    public static int? ParseAge(string value) => value == "" ? null : int.TryParse(value, out var age) ? age : throw new ApiException(400, "Choose a valid age range.");
}
