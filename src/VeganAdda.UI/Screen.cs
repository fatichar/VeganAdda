using Microsoft.AspNetCore.Components;
using VeganAdda.Contracts;

namespace VeganAdda.UI;

public class Screen : ComponentBase, IDisposable
{
    [Inject] protected IProfilesApi Profiles { get; set; } = default!;
    [Inject] protected ICommunityApi Api { get; set; } = default!;
    [Inject] protected IPlatform Platform { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    protected Session? Session;
    protected string Error = "";
    protected string Success = "";
    protected bool Busy;
    protected bool Loaded;
    protected string Current => "/" + Nav.ToBaseRelativePath(Nav.Uri);
    protected override async Task OnInitializedAsync()
    {
        await Run(async () => Session = await Api.Get<Session>("session"));
        Platform.Changed += Changed;
    }
    protected async Task Run(Func<Task> action)
    {
        Error = ""; Busy = true;
        try { await action(); }
        catch (ApiException ex)
        {
            Error = ex.Message;
            if (ex.Status == 401) Platform.SignIn(Current);
            if (ex.Status == 409 && ex.Message.Contains("profile first")) Nav.NavigateTo("/onboarding?returnUrl=" + Uri.EscapeDataString(Current));
        }
        catch (HttpRequestException) { Error = "We couldn't reach the community. Check your connection and try again."; }
        finally { Busy = false; Loaded = true; }
    }
    protected async Task Act(Func<Task> action)
    {
        if (Busy) return;
        if (Session?.Id == null) { Platform.SignIn(Current); return; }
        if (!Session.BasicProfileComplete) { Nav.NavigateTo("/onboarding?returnUrl=" + Uri.EscapeDataString(Current)); return; }
        await Run(action);
    }
    protected bool ReadyForAction(string target)
    {
        if (Session?.Id == null) { Platform.SignIn(target); return false; }
        if (!Session.BasicProfileComplete) { Nav.NavigateTo("/onboarding?returnUrl=" + E(target)); return false; }
        return true;
    }
    protected virtual ChangeArea ChangeInterests => ChangeArea.All;
    protected virtual Task Refresh() => Task.CompletedTask;
    private void Changed()
    {
        if ((Platform.LastChange & ChangeInterests) == 0) return;
        _ = InvokeAsync(async () => { await Run(Refresh); StateHasChanged(); });
    }
    protected override async Task OnAfterRenderAsync(bool firstRender) { if (firstRender && Session?.BasicProfileComplete == true) await Platform.StartRealtime(); }
    public virtual void Dispose() => Platform.Changed -= Changed;
    protected static string E(string? value) => Uri.EscapeDataString(value ?? "");
    protected static string Date(DateTimeOffset? value, string zone = "Asia/Kolkata")
        => value.HasValue ? TimeZoneInfo.ConvertTime(value.Value, TimeZoneInfo.FindSystemTimeZoneById(zone)).ToString("ddd, d MMM · h:mm tt") : "Details to be confirmed";
}
