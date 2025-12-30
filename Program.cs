using NationsCities.Components;
using NationsCities.Hubs;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Polska kultura
var polishCulture = new CultureInfo("pl-PL");
CultureInfo.DefaultThreadCurrentCulture = polishCulture;
CultureInfo.DefaultThreadCurrentUICulture = polishCulture;

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// SignalR
builder.Services.AddSignalR();

// Game Services (Singleton dla współdzielonego stanu)
builder.Services.AddSingleton<NationsCities.Services.RoomService>();
builder.Services.AddSingleton<NationsCities.Services.GameService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

// SignalR Hub
app.MapHub<GameHub>("/gamehub");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
