using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
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
public class BrowserPlatform(NavigationManager navigation, ICommunityApi api) : IPlatform, IAsyncDisposable
{
    private HubConnection? hub;
    private bool starting;
    public event Action? Changed;
    public Task<string> Upload(Stream stream, string filename, string contentType) => ((BrowserApi)api).Upload(stream, filename, contentType);
    public void SignIn(string returnTo) => navigation.NavigateTo("/signin?returnUrl=" + Uri.EscapeDataString(returnTo));
    public async Task StartRealtime()
    {
        if (hub != null || starting) return;
        starting = true;
        try
        {
            if ((await api.Get<Session>("session")).Id == null) return;
            hub = new HubConnectionBuilder().WithUrl(navigation.ToAbsoluteUri("/hubs/community")).WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)]).Build();
            hub.On("Changed", () => Changed?.Invoke());
            hub.Reconnected += _ => { Changed?.Invoke(); return Task.CompletedTask; };
            hub.Closed += _ => { Changed?.Invoke(); return Task.CompletedTask; };
            await hub.StartAsync();
        }
        catch { if (hub != null) await hub.DisposeAsync(); hub = null; }
        finally { starting = false; }
    }
    public async ValueTask DisposeAsync() { if (hub != null) await hub.DisposeAsync(); }
}
