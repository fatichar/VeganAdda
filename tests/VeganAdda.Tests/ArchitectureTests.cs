using NetArchTest.Rules;
using VeganAdda.Contracts;
using VeganAdda.UI;
using VeganAdda.Web.Features;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Shared_contracts_and_UI_do_not_depend_on_server_or_persistence()
    {
        Assert.True(Types.InAssembly(typeof(Person).Assembly).ShouldNot()
            .HaveDependencyOnAny("VeganAdda.Web", "VeganAdda.UI", "Microsoft.EntityFrameworkCore").GetResult().IsSuccessful);
        Assert.True(Types.InAssembly(typeof(Screen).Assembly).ShouldNot()
            .HaveDependencyOnAny("VeganAdda.Web", "Microsoft.EntityFrameworkCore").GetResult().IsSuccessful);
    }

    [Fact]
    public void Feature_implementations_cannot_call_the_legacy_dispatcher()
    {
        var result = Types.InAssembly(typeof(ProfilesService).Assembly).That()
            .ResideInNamespace("VeganAdda.Web.Features").And().DoNotHaveName(nameof(FeatureRegistration))
            .ShouldNot().HaveDependencyOn(typeof(CommunityService).FullName!).GetResult();
        Assert.True(result.IsSuccessful);
    }

    [Theory]
    [InlineData("Profiles", "Messaging")]
    [InlineData("Profiles", "Moderation")]
    [InlineData("Referrals", "Profiles")]
    [InlineData("Events", "Messaging")]
    [InlineData("Events", "Moderation")]
    [InlineData("Shared", "Profiles")]
    [InlineData("Shared", "Events")]
    [InlineData("Shared", "Messaging")]
    public void Foundational_features_do_not_depend_on_their_consumers(string owner, string consumer)
        => Assert.True(Types.InAssembly(typeof(ProfilesService).Assembly).That()
            .ResideInNamespace($"VeganAdda.Web.Features.{owner}").ShouldNot()
            .HaveDependencyOn($"VeganAdda.Web.Features.{consumer}").GetResult().IsSuccessful);

    [Fact]
    public void Feature_operations_return_explicit_contracts()
    {
        var methods = typeof(ProfilesService).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("VeganAdda.Web.Features.", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly));
        Assert.DoesNotContain(methods, m => m.ReturnType == typeof(Task<object>));
    }
}
