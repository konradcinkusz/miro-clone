using MiroClone.CanvasService;
using MiroClone.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProxyForwardedHeaders();
builder.Services.AddOriginGuard(builder.Configuration);
builder.Services.AddCanvasService(builder.Configuration);

var app = builder.Build();
app.UseProxyForwardedHeaders();
app.UseOriginGuard();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.Run();
