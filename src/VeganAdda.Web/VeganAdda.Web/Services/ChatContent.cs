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

    public string MessageText(ChatMessage message, bool required = false)
    {
        try
        {
            return encryption.DecryptMessage(message.Conversation, message.SenderId, message.ClientId,
                message.BodyKeyId, message.EncryptedBody);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("Unable to decrypt chat message {MessageId}.", message.Id);
            if (required) throw new ApiException(503, "This message is temporarily unavailable. Please try again.");
            return "This message is unavailable. Reload the conversation to try again.";
        }
    }

    public string IntroductionText(Relationship relationship, bool required = false)
    {
        if (relationship.EncryptedIntroduction == null) return "";
        try
        {
            return encryption.DecryptIntroduction(relationship.Key, relationship.RequestedBy,
                relationship.IntroductionKeyId!, relationship.EncryptedIntroduction);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("Unable to decrypt a connection introduction.");
            if (required) throw new ApiException(503, "The introduction is temporarily unavailable. Please try again.");
            return "This introduction is unavailable.";
        }
    }

    public Message View(ChatMessage message)
        => new(message.Id, message.SenderId, message.ClientId, MessageText(message), message.SentAt);
}
