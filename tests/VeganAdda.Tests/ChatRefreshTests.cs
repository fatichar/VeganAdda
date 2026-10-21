using System.Reflection;
using VeganAdda.Contracts;
using VeganAdda.UI.Pages;

namespace VeganAdda.Tests;

public class ChatRefreshTests
{
    [Fact]
    public async Task Realtime_starts_when_session_finishes_loading_after_first_render()
    {
        var platform = new RecordingPlatform();
        var screen = new LateSessionScreen(platform);
        await screen.Render(first: true);
        Assert.Equal(0, platform.Starts);
        screen.CompleteSession();
        await screen.Render(first: false);
        Assert.Equal(1, platform.Starts);
    }

    [Fact]
    public async Task Send_acknowledgement_does_not_skip_an_unfetched_incoming_message()
    {
        var api = new HistoryApi();
        var screen = new ChatHarness(api);
        api.Pages.Enqueue(new([Message(1)], false, true));
        await screen.Reload();

        // A send completes before the incoming invalidation has been processed.
        screen.DisplayAcknowledgement(Message(3));
        api.Pages.Enqueue(new([Message(2), Message(3)], false, true));
        await screen.Reload();

        Assert.Equal("conversations/chat/messages?after=1", api.Paths[^1]);
        Assert.Equal(new long[] { 1, 2, 3 }, screen.History.Select(m => m.Id));
    }

    [Fact]
    public async Task Catchup_pages_advance_only_through_fetched_messages()
    {
        var api = new HistoryApi();
        var screen = new ChatHarness(api);
        api.Pages.Enqueue(new([Message(1)], false, true));
        await screen.Reload();
        screen.DisplayAcknowledgement(Message(5));
        api.Pages.Enqueue(new([Message(2), Message(3)], false, true, true));
        api.Pages.Enqueue(new([Message(4), Message(5)], false, true));
        await screen.Reload();

        Assert.Equal(new[] { "conversations/chat/messages", "conversations/chat/messages?after=1", "conversations/chat/messages?after=3" }, api.Paths);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, screen.History.Select(m => m.Id));
    }

    private static Message Message(long id) => new(id, Guid.NewGuid(), Guid.NewGuid(), "Test message", DateTimeOffset.UtcNow);

    private sealed class LateSessionScreen(RecordingPlatform platform) : VeganAdda.UI.Screen
    {
        public Task Render(bool first) { Platform = platform; return OnAfterRenderAsync(first); }
        public void CompleteSession() => Session = new(Guid.NewGuid(), "Member", true, false, true, false, BasicProfileComplete: true);
    }
    private sealed class RecordingPlatform : IPlatform
    {
        public int Starts;
        public Task StartRealtime() { Starts++; return Task.CompletedTask; }
        public event Action? Changed { add { } remove { } }
        public void SignIn(string returnTo) => throw new NotSupportedException();
        public Task SignInWithGoogle(string returnTo) => throw new NotSupportedException();
        public Task ClearFirebaseSession() => throw new NotSupportedException();
        public Task<string> Upload(Stream stream, string filename, string contentType) => throw new NotSupportedException();
    }

    // Exercise the component's refresh operation without introducing a renderer
    // dependency. Reflection only supplies its private injected client/state.
    private sealed class ChatHarness : Messages
    {
        private static readonly FieldInfo history = typeof(Messages).GetField("history", BindingFlags.Instance | BindingFlags.NonPublic)!;
        public ChatHarness(HistoryApi api)
        {
            Api = api;
            Session = new(Guid.NewGuid(), "Member", true, false, true, false);
            Key = "chat";
            typeof(Messages).GetProperty("MessagingApi", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, api);
        }
        public Task Reload() => Refresh();
        public List<Message> History => (List<Message>)history.GetValue(this)!;
        public void DisplayAcknowledgement(Message message) => History.Add(message);
    }

    private sealed class HistoryApi : ICommunityApi, IMessagingApi
    {
        public Queue<MessagePage> Pages { get; } = new();
        public List<string> Paths { get; } = [];
        public Task<T> Get<T>(string path)
        {
            Paths.Add(path);
            return Task.FromResult((T)(object)Pages.Dequeue());
        }
        public Task<T> Send<T>(string path, object? body = null, string method = "POST") => throw new InvalidOperationException("Unexpected write");
        public Task<Inbox> GetInbox(int chatPage = 1, int requestPage = 1, string? key = null)
            => Task.FromResult(new Inbox(new([], 0, 1), new([], 0, 1), null, null));
    }
}
