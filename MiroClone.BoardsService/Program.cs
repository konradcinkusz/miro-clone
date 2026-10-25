using MiroClone.BoardsService;
using MiroClone.BoardsService.Boards;
using MiroClone.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddOriginGuard(builder.Configuration);
builder.Services.AddJwtValidation(builder.Configuration);
builder.Services.AddStandardRateLimiting(builder.Configuration);
builder.Services.AddBoardsService(builder.Configuration);

var app = builder.Build();
app.UseOriginGuard();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapBoardEndpoints();
app.Run();
