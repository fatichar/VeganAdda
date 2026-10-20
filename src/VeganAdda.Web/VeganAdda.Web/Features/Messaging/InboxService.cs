using VeganAdda.Contracts;
using VeganAdda.Web.Features.Connections;

namespace VeganAdda.Web.Features.Messaging;

public sealed class InboxService(MessagingService messages, ConnectionsService connections) : IMessagingApi
{
    public async Task<Inbox> GetInbox(int chatPage = 1, int requestPage = 1, string? key = null)
    {
        var chats = await messages.ConversationsPage(chatPage);
        var requests = await connections.ConnectionsPage(requestPage, state: "pending");
        var selected = key == null ? null : (await messages.ConversationsPage(key: key, includePending: true)).Items.SingleOrDefault();
        var request = key == null ? null : (await connections.ConnectionsPage(state: "pending", key: key)).Items.SingleOrDefault();
        return new(chats, requests, selected, request);
    }
}
