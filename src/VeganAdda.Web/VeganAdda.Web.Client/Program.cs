using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using VeganAdda.Contracts;
using VeganAdda.Web.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<BrowserApi>();
builder.Services.AddScoped<ICommunityApi>(s => s.GetRequiredService<BrowserApi>());
builder.Services.AddScoped<IPlatform, BrowserPlatform>();

await builder.Build().RunAsync();
