using MiroClone.ServiceDefaults;
using MiroClone.Web;
using MiroClone.Web.Account;
using MiroClone.Web.Components;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProxyForwardedHeaders();
builder.Services.AddOriginGuard(builder.Configuration);
builder.Services.AddWebApp(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseProxyForwardedHeaders();
app.UseOriginGuard();
app.MapDefaultEndpoints();
app.MapStaticAssets();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapAccountEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
