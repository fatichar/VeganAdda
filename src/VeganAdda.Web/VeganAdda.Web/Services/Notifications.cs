using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VeganAdda.Contracts;

namespace VeganAdda.Web.Services;

public partial class CommunityService
{
    private async Task<object> Unread()
    {
        var me = await User();
        var messages = await (from message in db.Messages
                              join relationship in db.Relationships on message.Conversation equals relationship.Key
                              where message.SenderId != me.Id &&
                                  ((relationship.A == me.Id && message.Id > relationship.ReadA) ||
                                   (relationship.B == me.Id && message.Id > relationship.ReadB))
                              select message.Id).CountAsync();
        return new UnreadCounts(messages, await db.Notifications.CountAsync(n => n.UserId == me.Id && n.Kind != "message" && !n.Read));
    }

    private async Task<object> NotificationAction(string method, string[] parts, JsonElement? body)
    {
        var me = await User();
        var owned = db.Notifications.Where(n => n.UserId == me.Id);
        if (method == "POST" && parts.Length == 2 && parts[1] == "read")
            await owned.Where(n => !n.Read).ExecuteUpdateAsync(s => s.SetProperty(n => n.Read, true));
        else if (method == "POST" && parts.Length == 3 && parts[2] == "read")
        {
            var id = Guid.Parse(parts[1]);
            if (await owned.Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.Read, true)) == 0) throw Missing();
        }
        else if (method == "DELETE" && parts.Length == 1)
        {
            var selection = Read<NotificationSelection>(body);
            if (selection.Ids == null || selection.Ids.Length > 100 || (!selection.All && selection.Ids.Length == 0))
                throw new ApiException(400, "Select notifications to delete.");
            await (selection.All ? owned : owned.Where(n => selection.Ids.Contains(n.Id))).ExecuteDeleteAsync();
        }
        else throw Missing();
        await changes.Changed(me.Id);
        return new Ok();
    }
}
