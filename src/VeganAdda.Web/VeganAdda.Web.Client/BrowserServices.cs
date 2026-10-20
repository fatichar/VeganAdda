using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.SignalR.Client;
using VeganAdda.Contracts;

namespace VeganAdda.Web.Client;

public class BrowserApi(HttpClient http) : ICommunityApi
{
    public async Task<T> Get<T>(string path) => await Result<T>(await http.GetAsync("api/" + path));
    public async Task<T> Send<T>(string path, object? body = null, string method = "POST")
    {
        var token = await http.GetFromJsonAsync<Csrf>("api/csrf");
        var request = new HttpRequestMessage(new HttpMethod(method), path.StartsWith("auth/") ? path : "api/" + path);
        request.Headers.Add("X-CSRF-TOKEN", token!.Token);
        request.Content = JsonContent.Create(body, options: Wire.Json);
        return await Result<T>(await http.SendAsync(request));
    }
    public async Task<string> Upload(Stream stream, string filename, string contentType)
    {
        var token = await http.GetFromJsonAsync<Csrf>("api/csrf");
        using var form = new MultipartFormDataContent(); using var content = new StreamContent(stream);
        content.Headers.ContentType = new(contentType); form.Add(content, "file", filename);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/upload") { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", token!.Token);
        return (await Result<UploadResult>(await http.SendAsync(request))).Path;
    }
    private static async Task<T> Result<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            string error = "Please try again. Your connection may have been interrupted.";
            try { error = (await response.Content.ReadFromJsonAsync<Failure>())?.Error ?? error; } catch (JsonException) { }
            throw new ApiException((int)response.StatusCode, error);
        }
        return (await response.Content.ReadFromJsonAsync<T>(Wire.Json))!;
    }
    private record Csrf(string Token);
    private record UploadResult(string Path);
    private record Failure(string Error);
}
public class BrowserPlatform(NavigationManager navigation, ICommunityApi api, IJSRuntime js, ILogger<BrowserPlatform> logger, IWebAssemblyHostEnvironment environment) : IPlatform, IAsyncDisposable
{
    private HubConnection? hub;
    private IJSObjectReference? firebaseModule;
    private bool starting;
    private bool disposed;
    private CancellationTokenSource? invalidation;
    private ChangeArea pendingChanges;
    public ChangeArea LastChange { get; private set; } = ChangeArea.All;
    public async Task<bool> IsVisible()
    {
        try { return await js.InvokeAsync<bool>("veganAddaVisible"); }
        catch (JSException) { return true; }
    }
    private async Task Notify(ChangeArea area)
    {
        if (disposed) return;
        pendingChanges |= area;
        invalidation?.Cancel();
        invalidation?.Dispose();
        invalidation = new CancellationTokenSource();
        var token = invalidation.Token;
        try
        {
            await Task.Delay(150, token);
            if (disposed) return;
            LastChange = pendingChanges; pendingChanges = 0;
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public event Action? Changed;
    public Task<string> Upload(Stream stream, string filename, string contentType) => ((BrowserApi)api).Upload(stream, filename, contentType);
    public void SignIn(string returnTo) => navigation.NavigateTo("/signin?returnUrl=" + Uri.EscapeDataString(returnTo));
    public async Task SignInWithGoogle(string returnTo)
    {
        try
        {
            firebaseModule ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/firebase-auth.js");
            var idToken = await firebaseModule.InvokeAsync<string>("signInWithGoogle");
            await api.Send<Ok>("auth/firebase", new FirebaseLoginInput(idToken));
            var session = await api.Get<Session>("session");
            var target = session.HasProfile ? returnTo : "/onboarding?returnUrl=" + Uri.EscapeDataString(returnTo);
            navigation.NavigateTo(target, forceLoad: true);
        }
        catch (ApiException ex) when (ex.Status != 429)
        {
            logger.LogWarning(ex, "Firebase Google sign-in failed at the server with HTTP {Status}.", ex.Status);
            var message = environment.IsDevelopment()
                ? $"Google sign-in failed (HTTP {ex.Status}): {ex.Message}"
                : "Google sign-in couldn't be completed. Please try again.";
            throw new ApiException(400, message);
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Firebase Google sign-in failed in the browser.");
            var message = environment.IsDevelopment()
                ? $"Google sign-in failed in the browser: {ex.Message}"
                : "Google sign-in was closed or couldn't be completed. Please try again.";
            throw new ApiException(400, message);
        }
    }
    public async Task ClearFirebaseSession()
    {
        try
        {
            firebaseModule ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/firebase-auth.js");
            await firebaseModule.InvokeVoidAsync("signOut");
        }
        catch (JSException) { }
    }
    public async Task StartRealtime()
    {
        if (starting || disposed) return;
        starting = true;
        try
        {
            if (hub != null)
            {
                if (hub.State == HubConnectionState.Disconnected) await hub.StartAsync();
                return;
            }
            if ((await api.Get<Session>("session")).Id == null) return;
            hub = new HubConnectionBuilder().WithUrl(navigation.ToAbsoluteUri("/hubs/community")).WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)]).Build();
            hub.On<string>("Changed", area => Notify(Enum.TryParse<ChangeArea>(area, out var parsed) ? parsed : ChangeArea.All));
            hub.Reconnected += _ => Notify(ChangeArea.All);
            hub.Closed += _ => Notify(ChangeArea.All);
            await hub.StartAsync();
        }
        catch { if (hub != null) await hub.DisposeAsync(); hub = null; }
        finally { starting = false; }
    }
    public async ValueTask DisposeAsync()
    {
        disposed = true;
        invalidation?.Cancel(); invalidation?.Dispose();
        if (hub != null) await hub.DisposeAsync();
        if (firebaseModule != null) await firebaseModule.DisposeAsync();
    }
}
