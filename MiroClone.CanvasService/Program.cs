using MiroClone.CanvasService;
using MiroClone.CanvasService.Api;
using MiroClone.CanvasService.Realtime;
using MiroClone.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddOriginGuard(builder.Configuration);
builder.Services.AddCanvasService(builder.Configuration);

var app = builder.Build();
app.UseOriginGuard();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapDefaultEndpoints();
app.MapCanvasEndpoints();
app.MapHub<CanvasHub>(CanvasHubMessages.HubPath);
app.Run();
