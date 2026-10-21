using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using VeganAdda.Contracts;

namespace VeganAdda.Web.Services;

public sealed record VerifiedFirebaseIdentity(string Uid, string Name, string Provider);

public interface IFirebaseIdentityVerifier
{
    Task<VerifiedFirebaseIdentity?> Verify(string idToken);
}

/// <summary>Verifies Firebase client tokens against the configured project using the Admin SDK.</summary>
public sealed class FirebaseIdentityVerifier(IConfiguration configuration, IWebHostEnvironment environment, ILogger<FirebaseIdentityVerifier> logger) : IFirebaseIdentityVerifier
{
    private readonly object appLock = new();
    private FirebaseApp? firebaseApp;

    public async Task<VerifiedFirebaseIdentity?> Verify(string idToken)
    {
        var projectId = configuration["Authentication:Firebase:ProjectId"];
        if (string.IsNullOrWhiteSpace(projectId))
        {
            logger.LogError("Firebase token verification has no configured project ID.");
            throw new ApiException(503, "Firebase project ID is not configured.");
        }

        FirebaseToken token;
        try
        {
            FirebaseApp app;
            lock (appLock)
            {
                firebaseApp ??= FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.GetApplicationDefault(),
                    ProjectId = projectId
                });
                app = firebaseApp;
            }
            if (!string.Equals(app.Options.ProjectId, projectId, StringComparison.Ordinal))
            {
                logger.LogError("Firebase Admin project {AdminProjectId} does not match configured project {ConfiguredProjectId}.", app.Options.ProjectId, projectId);
                throw Rejected($"Firebase Admin project '{app.Options.ProjectId}' does not match '{projectId}'.");
            }
            token = await FirebaseAuth.GetAuth(app).VerifyIdTokenAsync(idToken);
        }
        catch (FirebaseAuthException ex)
        {
            logger.LogWarning(ex, "Firebase rejected the ID token. Auth error code: {FirebaseErrorCode}", ex.AuthErrorCode?.ToString() ?? "unknown");
            throw Rejected($"Firebase rejected the ID token: {ex.Message}");
        }

        // The Admin SDK has verified this exact token. Read the nested provider claim
        // from its original JSON payload because the SDK's object-valued Claims map
        // can represent nested JSON differently across serializer versions.
        var provider = GetProvider(idToken);
        if (provider is not ("google.com" or "phone"))
        {
            logger.LogWarning("Firebase verified the token, but its sign-in provider claim was {Provider}.", provider ?? "missing");
            throw Rejected($"Firebase verified the token, but its sign-in provider claim was '{provider ?? "missing"}'.");
        }
        var uid = token.Uid;
        if (string.IsNullOrWhiteSpace(uid) || uid.Length > 128)
        {
            logger.LogWarning("Firebase verified the token but returned an invalid UID.");
            throw Rejected("Firebase verified the token but returned an invalid UID.");
        }
        var name = token.Claims.TryGetValue("name", out var rawName) ? StringValue(rawName) : "";
        return new(uid, name, provider);
    }

    private ApiException Rejected(string developmentMessage)
        => new(401, environment.IsDevelopment() ? developmentMessage : "The Firebase sign-in token could not be verified.");

    private static string? GetProvider(string verifiedIdToken)
    {
        try
        {
            var segments = verifiedIdToken.Split('.');
            if (segments.Length != 3) return null;

            var payload = segments[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch
            {
                0 => "",
                2 => "==",
                3 => "=",
                _ => throw new FormatException("Invalid base64url payload length.")
            };

            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            var root = document.RootElement;
            if (!root.TryGetProperty("firebase", out var firebase) ||
                firebase.ValueKind != JsonValueKind.Object ||
                !firebase.TryGetProperty("sign_in_provider", out var provider) ||
                provider.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return provider.GetString();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string StringValue(object value) => value switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() ?? "",
        _ => ""
    };
}
