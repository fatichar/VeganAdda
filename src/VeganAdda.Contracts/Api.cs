using System.Text.Json;

namespace VeganAdda.Contracts;

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Intents = ["Friendship", "Business & networking", "Dating", "Activism"];
    public static readonly string[] Cities = ["All India", "Pune", "Bengaluru", "Mumbai", "Delhi NCR", "Hyderabad", "Chennai"];
}
public interface ICommunityApi
{
    Task<T> Get<T>(string path);
    Task<T> Send<T>(string path, object? body = null, string method = "POST");
}
public interface IPlatform
{
    void SignIn(string returnTo);
    Task SignInWithGoogle(string returnTo);
    Task ClearFirebaseSession();
    Task StartRealtime();
    Task<string> Upload(Stream stream, string filename, string contentType);
    event Action? Changed;
}
public class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public record Ok(bool Success = true);
public record Page<T>(List<T> Items, int Total, int PageNumber, int PageSize = 12);
// This is the only profile projection returned to other people. Never add email or OAuth identity.
