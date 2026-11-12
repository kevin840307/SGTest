using SGTest.Host;

var builder = WebApplication.CreateBuilder(args);
// Important: development-only anonymous demo, never expose on a public interface.
builder.WebHost.UseUrls("http://127.0.0.1:5678");
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<MapActor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MapActor>());

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapGet("/health", (MapActor actor) =>
    Results.Ok(new { status = "prototype", server_tick = actor.CurrentTick }));
WebSocketEndpoint.Map(app);
await app.RunAsync();
