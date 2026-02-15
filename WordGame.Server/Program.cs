using Microsoft.AspNetCore.Server.Kestrel.Core;
using WordGame.Server;
using WordGame.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5119, listen => listen.Protocols = HttpProtocols.Http1);
    options.ListenLocalhost(5118, listen => listen.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WordValidator>();
builder.Services.AddSingleton<GameManager>();
builder.Services.AddScoped<GameClientService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapGrpcService<WordGameGrpcService>();
app.MapBlazorHub();
app.MapRazorPages();
app.MapFallbackToPage("/Host");

app.Run();
