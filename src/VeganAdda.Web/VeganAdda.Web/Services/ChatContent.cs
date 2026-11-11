using System.Security.Cryptography;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Services;

public sealed class ChatContent(ChatEncryption encryption, ILogger<ChatContent> logger)
{
    public ChatMessage NewMessage(string conversation, Guid sender, Guid clientId, string text, bool pushPending = false)
        => new()
        {
            Conversation = conversation,
            SenderId = sender,
            ClientId = clientId,
            BodyKeyId = encryption.CurrentKeyId,
            EncryptedBody = encryption.EncryptMessage(conversation, sender, clientId, text),
            PushPending = pushPending
        };

    public void SetIntroduction(Relationship relationship, Guid requester, string text)
    {
        relationship.EncryptedIntroduction = text.Length == 0 ? null
            : encryption.EncryptIntroduction(relationship.Key, requester, text);
        relationship.IntroductionKeyId = text.Length == 0 ? null : encryption.CurrentKeyId;
        relationship.RequestedBy = requester;
    }

    public string MessageText(ChatMessage message) => TryMessageText(message) ?? "This message is unavailable.";

    /// <summary>Decrypts a message body, or returns null and logs when the envelope cannot be read.</summary>
    public string? TryMessageText(ChatMessage message)
    {
        try
        {
            return encryption.DecryptMessage(message.Conversation, message.SenderId, message.ClientId,
                message.BodyKeyId, message.EncryptedBody);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("Unable to decrypt chat message {MessageId}.", message.Id);
            return null;
        }
    }

    public string IntroductionText(Relationship relationship) => TryIntroductionText(relationship) ?? "This introduction is unavailable.";

    /// <summary>Decrypts an introduction ("" when none), or returns null and logs when the envelope cannot be read.</summary>
    public string? TryIntroductionText(Relationship relationship)
    {
        if (relationship.IntroductionKeyId == null && relationship.EncryptedIntroduction == null) return "";
        try
        {
            if (relationship.IntroductionKeyId == null || relationship.EncryptedIntroduction == null) throw new CryptographicException();
            return encryption.DecryptIntroduction(relationship.Key, relationship.RequestedBy,
                relationship.IntroductionKeyId, relationship.EncryptedIntroduction);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("Unable to decrypt a connection introduction.");
            return null;
        }
    }

    public Message View(ChatMessage message)
        => new(message.Id, message.SenderId, message.ClientId, MessageText(message), message.SentAt);
}
