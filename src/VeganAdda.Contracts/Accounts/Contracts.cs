namespace VeganAdda.Contracts;

public record Session(Guid? Id, string Name, bool HasProfile, bool IsAdmin, bool Development, bool AuthConfigured, bool IsModerator = false, bool? IsVegan = null, bool VeganVerified = false, bool BasicProfileComplete = false)
{
    public bool VeganVerificationPending => IsVegan == true && !VeganVerified;
}
public record FirebaseClientConfig(string ApiKey, string AuthDomain, string ProjectId, string AppId, bool Configured);
public record FirebaseLoginInput(string IdToken);
public record DeleteInput(string Confirm);
