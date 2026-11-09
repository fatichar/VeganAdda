using System.Security.Cryptography;
using System.Text.Json;
using VeganAdda.Web.Services;

namespace VeganAdda.Tests;

public class ChatEncryptionTests
{
    [Fact]
    public void Historical_keys_decrypt_after_rotation_and_ciphertext_is_bound_to_its_message()
    {
        var path = Path.Combine(Path.GetTempPath(), $"veganadda-chat-test-{Guid.NewGuid():N}.json");
        var keys = new Dictionary<string, byte[]> { ["old"] = RandomNumberGenerator.GetBytes(32) };
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { current = "old", keys }));
            var first = ChatEncryption.Load(path);
            const string original = "नमस्ते 🌱";
            var sender = Guid.NewGuid();
            var clientId = Guid.NewGuid();
            var envelope = first.EncryptMessage("pair", sender, clientId, original);
            Assert.False(envelope.SequenceEqual(first.EncryptMessage("pair", sender, clientId, original)));

            keys["new"] = RandomNumberGenerator.GetBytes(32);
            File.WriteAllText(path, JsonSerializer.Serialize(new { current = "new", keys }));
            var rotated = ChatEncryption.Load(path);
            Assert.Equal("new", rotated.CurrentKeyId);
            Assert.Equal(original, rotated.DecryptMessage("pair", sender, clientId, "old", envelope));
            Assert.ThrowsAny<CryptographicException>(() => rotated.DecryptMessage("other", sender, clientId, "old", envelope));
            Assert.ThrowsAny<CryptographicException>(() => rotated.DecryptMessage("pair", sender, Guid.NewGuid(), "old", envelope));
            Assert.ThrowsAny<CryptographicException>(() => rotated.DecryptMessage("pair", sender, clientId, "new", envelope));
            Assert.ThrowsAny<CryptographicException>(() => rotated.DecryptIntroduction("pair", sender, "old", envelope));
            envelope[^1] ^= 1;
            Assert.ThrowsAny<CryptographicException>(() => rotated.DecryptMessage("pair", sender, clientId, "old", envelope));
        }
        finally { File.Delete(path); }
    }
}
