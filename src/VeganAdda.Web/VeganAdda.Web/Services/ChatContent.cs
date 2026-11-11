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
            Text = text,
            BodyKeyId = encryption.CurrentKeyId,
            EncryptedBody = encryption.EncryptMessage(conversation, sender, clientId, text),
            PushPending = pushPending
        };

    public void SetIntroduction(Relationship relationship, Guid requester, string text)
    {
        relationship.EncryptedIntroduction = text.Length == 0 ? null
            : encryption.EncryptIntroduction(relationship.Key, requester, text);
        relationship.IntroductionKeyId = text.Length == 0 ? null : encryption.CurrentKeyId;
        relationship.Introduction = text;
        relationship.RequestedBy = requester;
    }

    public string MessageText(ChatMessage message)
    {
        if (message.BodyKeyId == null && message.EncryptedBody == null) return message.Text;
        try
        {
            if (message.BodyKeyId == null || message.EncryptedBody == null) throw new CryptographicException();
            return encryption.DecryptMessage(message.Conversation, message.SenderId, message.ClientId,
                message.BodyKeyId, message.EncryptedBody);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("Unable to decrypt chat message {MessageId}.", message.Id);
            return message.Text;
        }
    }

    public string IntroductionText(Relationship relationship)
    {
        if (relationship.IntroductionKeyId == null && relationship.EncryptedIntroduction == null) return relationship.Introduction;
        try
        {
            if (relationship.IntroductionKeyId == null || relationship.EncryptedIntroduction == null) throw new CryptographicException();
            return encryption.DecryptIntroduction(relationship.Key, relationship.RequestedBy,
                relationship.IntroductionKeyId, relationship.EncryptedIntroduction);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("Unable to decrypt a connection introduction.");
            return relationship.Introduction;
        }
    }

    public Message View(ChatMessage message)
        => new(message.Id, message.SenderId, message.ClientId, MessageText(message), message.SentAt);
}
