using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VeganAdda.Web.Services;

public sealed class ChatEncryption
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;
    private const byte Format = 1;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Dictionary<string, byte[]> keys;

    public string CurrentKeyId { get; }

    private ChatEncryption(string currentKeyId, Dictionary<string, byte[]> keys)
    {
        CurrentKeyId = currentKeyId;
        this.keys = keys;
    }

    public static ChatEncryption Load(string path)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var current = json.RootElement.GetProperty("current").GetString();
            var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var property in json.RootElement.GetProperty("keys").EnumerateObject())
            {
                var id = property.Name;
                var key = property.Value.GetBytesFromBase64();
                if (id.Length is < 1 or > 32 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
                    || key.Length != KeySize || keys.Values.Any(existing => existing.AsSpan().SequenceEqual(key))
                    || !keys.TryAdd(id, key))
                    throw new FormatException();
            }
            if (current == null || !keys.ContainsKey(current)) throw new FormatException();
            return new ChatEncryption(current, keys);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException("The chat encryption key file is missing, unreadable, or invalid.");
        }
    }

    public static ChatEncryption LoadForHost(IConfiguration config, IWebHostEnvironment environment)
    {
        var path = config["Storage:MessageKeysPath"];
        if (!string.IsNullOrWhiteSpace(path)) return Load(path);
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("Storage:MessageKeysPath must point to the chat encryption key file.");

        var keyDirectory = config["Storage:KeysPath"] ?? Path.Combine(environment.ContentRootPath, "keys");
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(keyDirectory))!, "message-keys");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "keys.json");
        if (!File.Exists(path)) CreateKeyFile(path);
        return Load(path);
    }

    public static void CreateKeyFile(string path)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using (var stream = new FileStream(temporary, options))
                JsonSerializer.Serialize(stream, new
                {
                    current = "v1",
                    keys = new Dictionary<string, byte[]> { ["v1"] = RandomNumberGenerator.GetBytes(KeySize) }
                });
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path)) { }
        }
        finally { File.Delete(temporary); }
    }

    public byte[] EncryptMessage(string conversation, Guid sender, Guid clientId, string text)
        => Encrypt(text, Context("message", CurrentKeyId, conversation, sender, clientId));

    public string DecryptMessage(string conversation, Guid sender, Guid clientId, string keyId, byte[] envelope)
        => Decrypt(keyId, envelope, Context("message", keyId, conversation, sender, clientId));

    public byte[] EncryptIntroduction(string conversation, Guid requester, string text)
        => Encrypt(text, Context("introduction", CurrentKeyId, conversation, requester, null));

    public string DecryptIntroduction(string conversation, Guid requester, string keyId, byte[] envelope)
        => Decrypt(keyId, envelope, Context("introduction", keyId, conversation, requester, null));

    // Version 1: ASCII fields delimited by newlines. Key IDs and pair keys cannot
    // contain the delimiter. GUIDs use the lowercase 32-digit hexadecimal form.
    private static byte[] Context(string purpose, string keyId, string conversation, Guid actor, Guid? clientId)
        => Utf8.GetBytes($"VeganAdda/chat/1\n{purpose}\n{keyId}\n{conversation}\n{actor:N}\n{clientId:N}");

    private byte[] Encrypt(string text, byte[] context)
    {
        var plaintext = Utf8.GetBytes(text);
        try
        {
            var envelope = new byte[HeaderSize + plaintext.Length];
            envelope[0] = Format;
            var nonce = envelope.AsSpan(1, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(keys[CurrentKeyId], TagSize);
            aes.Encrypt(nonce, plaintext, envelope.AsSpan(HeaderSize), envelope.AsSpan(1 + NonceSize, TagSize), context);
            return envelope;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private string Decrypt(string keyId, byte[] envelope, byte[] context)
    {
        if (envelope.Length < HeaderSize || envelope[0] != Format || !keys.TryGetValue(keyId, out var key))
            throw new CryptographicException("Chat content cannot be decrypted.");
        var plaintext = new byte[envelope.Length - HeaderSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(envelope.AsSpan(1, NonceSize), envelope.AsSpan(HeaderSize),
                envelope.AsSpan(1 + NonceSize, TagSize), plaintext, context);
            return Utf8.GetString(plaintext);
        }
        catch (DecoderFallbackException)
        {
            throw new CryptographicException("Chat content cannot be decrypted.");
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
