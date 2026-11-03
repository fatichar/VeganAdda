using VeganAdda.Contracts;
using VeganAdda.Web.Features.Profiles;

namespace VeganAdda.Tests;

public class ProfileNameTests
{
    [Theory]
    [InlineData("Alex Example")]
    [InlineData("O'Connor")]
    [InlineData("Anne-Marie O’Neil")]
    [InlineData("Jane Doe, Jr.")]
    [InlineData("सना दीक्षित")]
    [InlineData("李 小明")]
    [InlineData("Jose\u0301")]
    public void Names_with_letters_and_common_punctuation_are_allowed(string name)
        => Assert.Equal(name, ProfilesService.ValidateName(name));

    [Theory]
    [InlineData("Alex Example, 19")]
    [InlineData("Alex 19")]
    [InlineData("123")]
    [InlineData("Jane@example.com")]
    [InlineData("Jane_Doe")]
    [InlineData("---")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Jane\nDoe")]
    public void Invalid_names_are_rejected(string name)
        => Assert.Equal(400, Assert.Throws<ApiException>(() => ProfilesService.ValidateName(name)).Status);

    [Fact]
    public void Names_are_trimmed_and_length_is_limited()
    {
        Assert.Equal("Alex Example", ProfilesService.ValidateName("  Alex Example  "));
        Assert.Equal(80, ProfilesService.ValidateName(new string('a', 80)).Length);
        Assert.Equal(400, Assert.Throws<ApiException>(() => ProfilesService.ValidateName(new string('a', 81))).Status);
    }
}
