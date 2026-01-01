using NationsCities.Components;
using NationsCities.Hubs;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

var polishCulture = new CultureInfo("pl-PL");
CultureInfo.DefaultThreadCurrentCulture = polishCulture;
CultureInfo.DefaultThreadCurrentUICulture = polishCulture;

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSignalR();

builder.Services.AddSingleton<NationsCities.Services.RoomService>();
builder.Services.AddSingleton<NationsCities.Services.GameService>();
builder.Services.AddHostedService<NationsCities.Services.RoomCleanupService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapHub<GameHub>("/gamehub");

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
