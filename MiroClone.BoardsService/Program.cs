using MiroClone.BoardsService;
using MiroClone.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddOriginGuard(builder.Configuration);
builder.Services.AddBoardsService(builder.Configuration);

var app = builder.Build();
app.UseOriginGuard();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapSessionEndpoints();
app.Run();
