using MiroClone.BoardsService;
using MiroClone.BoardsService.Boards;
using MiroClone.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProxyForwardedHeaders();
builder.Services.AddOriginGuard(builder.Configuration);
builder.Services.AddStandardRateLimiting(builder.Configuration);
builder.Services.AddBoardsService(builder.Configuration);

var app = builder.Build();
app.UseProxyForwardedHeaders();
app.UseOriginGuard();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapSessionEndpoints();
app.MapBoardEndpoints();
app.Run();
