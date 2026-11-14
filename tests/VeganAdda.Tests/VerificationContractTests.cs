using System.Text.Json;
using VeganAdda.Contracts;
using VeganAdda.UI;

namespace VeganAdda.Tests;

public class VerificationContractTests
{
    [Fact]
    public void Verification_evidence_links_web_addresses_without_unsafe_schemes()
    {
        var segments = VeganAdda.UI.Components.LinkedText.Segments(
            "My Instagram: https://www.instagram.com/vegan_me/, also instagram.com/other. Not javascript:alert(1)").ToList();
        Assert.Equal("My Instagram: ", segments[0].Text); Assert.Null(segments[0].Href);
        Assert.Equal(("https://www.instagram.com/vegan_me/", "https://www.instagram.com/vegan_me/"), segments[1]);
        Assert.Equal(("instagram.com/other", "https://instagram.com/other"), segments[3]);
        Assert.Equal(". Not javascript:alert(1)", segments[4].Text); Assert.Null(segments[4].Href);
        Assert.Equal(2, segments.Count(s => s.Href != null));
        Assert.Equal("Plain text", Assert.Single(VeganAdda.UI.Components.LinkedText.Segments("Plain text")).Text);
    }

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
