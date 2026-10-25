using MiroClone.ServiceDefaults;
using MiroClone.Web;
using MiroClone.Web.Components;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddOriginGuard(builder.Configuration);
builder.Services.AddWebApp();

var app = builder.Build();
app.UseOriginGuard();
app.MapDefaultEndpoints();
app.MapStaticAssets();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
