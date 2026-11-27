using VeganAdda.Web.Features.Verification;
using VeganAdda.Contracts;
using VeganAdda.Web.Features.Discovery;
using VeganAdda.Web.Features.Events;
using VeganAdda.Web.Features.Messaging;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Saved;
using VeganAdda.Web.Features.Moderation;
using VeganAdda.Web.Features.Stats;
using VeganAdda.Web.Client;

namespace VeganAdda.Web.Services;

// Each overlapping prerender operation has an independent DbContext. Typed results
// need no serialization round-trip through the path-based compatibility adapter.
public sealed class ServerFeatures(IServiceScopeFactory scopes, PrerenderedReads prerendered) : IMessagingApi, ISavedApi, IProfilesApi, IVerificationApi, IModerationApi, IStatsApi, IEventsApi, IProfilePhoneApi
{
    public Task<ProfilePhone> GetPhone() => Read(ReadRoutes.Phone, () => Call<ProfilePhoneService, ProfilePhone>(service => service.GetPhone()));
    public Task<ProfilePhoneAuthorization> AuthorizePhone(FirebaseLoginInput proof)
        => Call<ProfilePhoneService, ProfilePhoneAuthorization>(service => service.Authorize(proof));
    public Task<ProfilePhone> VerifyPhone(ProfilePhoneProof proof) => Call<ProfilePhoneService, ProfilePhone>(service => service.Verify(proof));
    public Task<ProfilePhone> SharePhone(ProfilePhoneVisibility visibility)
        => Call<ProfilePhoneService, ProfilePhone>(service => service.Share(visibility));
    public Task<ProfilePhone> RemovePhone() => Call<ProfilePhoneService, ProfilePhone>(service => service.Remove());
    public Task<Page<ScheduledEvent>> GetSchedule(string country, string state, string city, int page)
        => Read(ReadRoutes.Schedule(country, state, city, page), () => Call<EventsService, Page<ScheduledEvent>>(service => service.GetSchedule(country, state, city, page)));
    public Task<List<EventContact>> SearchEventContacts(string search)
        => Read(ReadRoutes.EventContacts(search), () => Call<EventsService, List<EventContact>>(service => service.SearchEventContacts(search)));
    public Task<Page<ProfileEvent>> GetProfileEvents(Guid personId, int page)
        => Read(ReadRoutes.ProfileEvents(personId, page), () => Call<EventsService, Page<ProfileEvent>>(service => service.GetProfileEvents(personId, page)));
    public Task<EventBatchResult> PublishBatch(EventBatchInput input)
        => Call<EventsService, EventBatchResult>(service => service.PublishBatch(input));
    public Task<StatsReport> GetStats(int days) => Read(ReadRoutes.Stats(days), () => Call<StatsService, StatsReport>(service => service.GetStats(days)));
    public Task<Ok> RequestProfileChanges(ProfileFeedbackInput input) => Call<ModerationService, Ok>(service => service.RequestProfileChanges(input));
    public Task<Ok> DeleteMemberProfile(Guid id, DeleteInput input) => Call<ProfilesService, Ok>(service => service.DeleteMemberProfile(id, input));
    public Task<ProfileReminderReceipt> QueueProfileReminders(ProfileReminderInput input)
        => Call<ProfileReminderService, ProfileReminderReceipt>(service => service.Queue(input));
    public Task<List<ProfileReminderBatchSummary>> GetProfileReminderBatches()
        => Read(ReadRoutes.ProfileReminderBatches, () => Call<ProfileReminderService, List<ProfileReminderBatchSummary>>(service => service.Recent()));
    public Task<ProfileReminderBatchResult> GetProfileReminderBatch(Guid id)
        => Read(ReadRoutes.ProfileReminderBatch(id), () => Call<ProfileReminderService, ProfileReminderBatchResult>(service => service.Get(id)));
    public Task<ProfileReminderReceipt> RetryProfileReminders(Guid id, ProfileReminderRetry input)
        => Call<ProfileReminderService, ProfileReminderReceipt>(service => service.Retry(id, input));
    private async Task<T> Call<TService, T>(Func<TService, Task<T>> operation) where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }
    // Reads are handed to the WebAssembly render so it starts from the prerendered data.
    private Task<T> Read<T>(string? path, Func<Task<T>> read) => prerendered.Record(path, read);
    public Task<VerificationStatus> GetVerification() => Read(ReadRoutes.Verification, () => Call<VerificationService, VerificationStatus>(s => s.GetVerification()));
    public Task<Ok> SubmitVerification(VerificationSubmission input) => Call<VerificationService, Ok>(s => s.SubmitVerification(input));
    public Task<Ok> WithdrawVerification(Guid id) => Call<VerificationService, Ok>(s => s.WithdrawVerification(id));
    public Task<List<VerificationCandidate>> SearchReferrers(string search) => Read(ReadRoutes.Referrers(search), () => Call<VerificationService, List<VerificationCandidate>>(s => s.SearchReferrers(search)));
    public Task<List<ReferralCard>> GetReferralCards(Guid personId) => Read(ReadRoutes.ReferralCards(personId), () => Call<VerificationService, List<ReferralCard>>(s => s.GetReferralCards(personId)));
    public Task<Ok> RespondToReferral(Guid id, bool confirm) => Call<VerificationService, Ok>(s => s.RespondToReferral(id, new VerificationResponse(confirm)));

    public Task<Inbox> GetInbox(int chatPage = 1, int requestPage = 1, string? key = null)
        => Read(ReadRoutes.Inbox(chatPage, requestPage, key), () => Call<InboxService, Inbox>(service => service.GetInbox(chatPage, requestPage, key)));
    public Task<SavedItems> GetSaved(int peoplePage = 1, int eventsPage = 1)
        => Read(ReadRoutes.Saved(peoplePage, eventsPage), () => Call<SavedService, SavedItems>(service => service.GetSaved(peoplePage, eventsPage)));
    public Task<ProfileInput> GetProfile() => Read(ReadRoutes.Profile, () => Call<ProfilesService, ProfileInput>(service => service.GetProfile()));
    public Task<Person> GetPerson(Guid id) => Read(ReadRoutes.Person(id), () => Call<ProfilesService, Person>(service => service.GetPerson(id)));
    public Task<Page<Person>> SearchPeople(DiscoveryInput input) => Read(ReadRoutes.People(input), () => Call<DiscoveryService, Page<Person>>(service => service.SearchPeople(input)));
    public Task<Ok> SaveProfile(ProfileInput input) => Call<ProfilesService, Ok>(service => service.SaveProfile(input));
}
