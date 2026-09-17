using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Claims.Web;
using Claims.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddScoped(sp =>
{
    var client = new HttpClient { BaseAddress = new Uri("http://localhost:5102/") };
    return client;
});
builder.Services.AddScoped<ClaimsClient>();
await builder.Build().RunAsync();
