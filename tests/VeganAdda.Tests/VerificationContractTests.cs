using System.Text.Json;
using VeganAdda.Contracts;
using VeganAdda.UI;

namespace VeganAdda.Tests;

public class VerificationContractTests
{
    [Fact]
    public void Typed_statuses_preserve_existing_json_values()
    {
        foreach (var state in Enum.GetValues<VerificationState>())
        {
            var json = JsonSerializer.Serialize(state, Wire.Json);
            Assert.Equal(JsonSerializer.Serialize(state.Label()), json);
            Assert.Equal(state, JsonSerializer.Deserialize<VerificationState>(json, Wire.Json));
        }
        foreach (var state in Enum.GetValues<ReferralState>())
        {
            var json = JsonSerializer.Serialize(state, Wire.Json);
            Assert.Equal(JsonSerializer.Serialize(state.Label()), json);
            Assert.Equal(state, JsonSerializer.Deserialize<ReferralState>(json, Wire.Json));
        }
    }

    [Fact]
    public void Chat_actions_distinguish_viewer_confirmation_from_another_reviewers_verification()
    {
        var action = new ReferralActivity(Guid.NewGuid(), ReferralActivityKind.Verified, DateTimeOffset.UtcNow, true);
        Assert.Equal("You confirmed their vegan status.", ReferralActivityText.Format(action, "Sachin", false));
        Assert.Equal("Sachin's vegan status was verified.", ReferralActivityText.Format(action with { ByViewer = false }, "Sachin", false));
        Assert.Equal("Your vegan status was verified.", ReferralActivityText.Format(action with { ByViewer = false }, "Sachin", true));
        Assert.Equal("You confirmed their vegan status for moderator review.",
            ReferralActivityText.Format(action with { Kind = ReferralActivityKind.Confirmed }, "Sachin", false));
    }
}
