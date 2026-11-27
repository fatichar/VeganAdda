using VeganAdda.Contracts;

namespace VeganAdda.Web.Client;

public sealed class BrowserFeatures(BrowserApi transport) : IMessagingApi, ISavedApi, IProfilesApi, IVerificationApi, IModerationApi, IStatsApi, IEventsApi, IProfilePhoneApi
{
    public Task<ProfilePhone> GetPhone() => transport.Get<ProfilePhone>(ReadRoutes.Phone);
    public Task<ProfilePhoneAuthorization> AuthorizePhone(FirebaseLoginInput proof)
        => transport.Send<ProfilePhoneAuthorization>("profile/phone/authorize", proof);
    public Task<ProfilePhone> VerifyPhone(ProfilePhoneProof proof) => transport.Send<ProfilePhone>("profile/phone/verify", proof);
    public Task<ProfilePhone> SharePhone(ProfilePhoneVisibility visibility) => transport.Send<ProfilePhone>("profile/phone/visibility", visibility);
    public Task<ProfilePhone> RemovePhone() => transport.Send<ProfilePhone>("profile/phone", method: "DELETE");
    public Task<Page<ScheduledEvent>> GetSchedule(string country, string state, string city, int page)
        => transport.Get<Page<ScheduledEvent>>(ReadRoutes.Schedule(country, state, city, page));
    public Task<List<EventContact>> SearchEventContacts(string search)
        => transport.Get<List<EventContact>>(ReadRoutes.EventContacts(search));
    public Task<Page<ProfileEvent>> GetProfileEvents(Guid personId, int page)
        => transport.Get<Page<ProfileEvent>>(ReadRoutes.ProfileEvents(personId, page));
    public Task<EventBatchResult> PublishBatch(EventBatchInput input)
        => transport.Send<EventBatchResult>("event-submissions/batch", input);
    public Task<Ok> RequestProfileChanges(ProfileFeedbackInput input) => transport.Send<Ok>("admin/profile-feedback", input);
    public Task<Ok> DeleteMemberProfile(Guid id, DeleteInput input) => transport.Send<Ok>($"admin/members/{id}", input, "DELETE");
    public Task<ProfileReminderReceipt> QueueProfileReminders(ProfileReminderInput input)
        => transport.Send<ProfileReminderReceipt>("admin/profile-reminders", input);
    public Task<List<ProfileReminderBatchSummary>> GetProfileReminderBatches()
        => transport.Get<List<ProfileReminderBatchSummary>>(ReadRoutes.ProfileReminderBatches);
    public Task<ProfileReminderBatchResult> GetProfileReminderBatch(Guid id)
        => transport.Get<ProfileReminderBatchResult>(ReadRoutes.ProfileReminderBatch(id));
    public Task<ProfileReminderReceipt> RetryProfileReminders(Guid id, ProfileReminderRetry input)
        => transport.Send<ProfileReminderReceipt>($"admin/profile-reminders/{id}/retry", input);
    public Task<VerificationStatus> GetVerification() => transport.Get<VerificationStatus>(ReadRoutes.Verification);
    public Task<Ok> SubmitVerification(VerificationSubmission input) => transport.Send<Ok>("verification", input);
    public Task<Ok> WithdrawVerification(Guid id) => transport.Send<Ok>("verification/withdraw", new VerificationRequestAction(id));
    public Task<List<VerificationCandidate>> SearchReferrers(string search) => transport.Get<List<VerificationCandidate>>(ReadRoutes.Referrers(search));
    public Task<List<ReferralCard>> GetReferralCards(Guid personId) => transport.Get<List<ReferralCard>>(ReadRoutes.ReferralCards(personId));
    public Task<Ok> RespondToReferral(Guid id, bool confirm) => transport.Send<Ok>($"verification/referrals/{id}/respond", new VerificationResponse(confirm));

    public Task<Inbox> GetInbox(int chatPage = 1, int requestPage = 1, string? key = null)
        => transport.Get<Inbox>(ReadRoutes.Inbox(chatPage, requestPage, key));
    public Task<SavedItems> GetSaved(int peoplePage = 1, int eventsPage = 1)
        => transport.Get<SavedItems>(ReadRoutes.Saved(peoplePage, eventsPage));
    public Task<StatsReport> GetStats(int days) => transport.Get<StatsReport>(ReadRoutes.Stats(days));
    public Task<ProfileInput> GetProfile() => transport.Get<ProfileInput>(ReadRoutes.Profile);
    public Task<Person> GetPerson(Guid id) => transport.Get<Person>(ReadRoutes.Person(id));
    public Task<Ok> SaveProfile(ProfileInput input) => transport.Send<Ok>("profile", input);
    public Task<Page<Person>> SearchPeople(DiscoveryInput input) => ReadRoutes.People(input) is { } path
        ? transport.Get<Page<Person>>(path)
        : transport.Send<Page<Person>>("people/search", input);
}
