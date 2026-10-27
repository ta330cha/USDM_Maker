using Microsoft.Extensions.FileProviders;
using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.VisualTests;

var builder = WebApplication.CreateBuilder(args);
// This development-only host runs directly from build output, including when
// ASPNETCORE_ENVIRONMENT is unset. Load the framework asset manifest explicitly.
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<AutoLayoutService>();
var app = builder.Build();

// Serve the production assets directly so visual checks exercise the same CSS and JS.
var webProject = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../../src/USDM_MindMap_Editor.Web"));
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(webProject, "wwwroot")) });
var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
app.MapGet("/USDM_MindMap_Editor.Web.styles.css", () => Results.File(
    Path.Combine(webProject, "obj", configuration, "net10.0", "scopedcss", "bundle", "USDM_MindMap_Editor.Web.styles.css"),
    "text/css"));
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<VisualTestApp>().AddInteractiveServerRenderMode();
app.Run();
