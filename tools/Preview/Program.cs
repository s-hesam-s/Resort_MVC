using Microsoft.AspNetCore.Identity;
using System.Reflection;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
var repoRoot = new DirectoryInfo(AppContext.BaseDirectory);
while (repoRoot is not null && !Directory.Exists(Path.Combine(repoRoot.FullName, "Resort.Web")))
    repoRoot = repoRoot.Parent;
if (repoRoot is null) throw new InvalidOperationException("Run this preview inside its repository checkout.");
var rootPath = repoRoot.FullName;
var webRoot = Path.Combine(rootPath, "Resort.Web", "wwwroot");
var webAssembly = typeof(Resort.Web.Controllers.HomeController).Assembly;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
    ApplicationName = webAssembly.GetName().Name,
    EnvironmentName = "Production",
    ContentRootPath = Path.Combine(rootPath, "tools", "Preview"),
    WebRootPath = webRoot
});
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection();
builder.WebHost.UseUrls("http://127.0.0.1:8810");
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllersWithViews().AddApplicationPart(webAssembly)
    .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new PreviewControllers()));
builder.Services.AddAuthentication().AddCookie();
builder.Services.AddHttpContextAccessor();
builder.Services.AddIdentityCore<Resort.Domain.Entities.ApplicationUser>().AddSignInManager();
builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.IUserStore<Resort.Domain.Entities.ApplicationUser>>(
    new Moq.Mock<Microsoft.AspNetCore.Identity.IUserStore<Resort.Domain.Entities.ApplicationUser>>().Object);

var app = builder.Build();
app.UseStaticFiles();

app.Use(async (context, next) => {
    if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 405; return; }
    var original = context.Response.Body;
    using var buffer = new MemoryStream();
    context.Response.Body = buffer;
    try {
        await next();
        buffer.Position = 0;
        if (context.Response.ContentType?.StartsWith("text/html") == true) {
            var html = await new StreamReader(buffer).ReadToEndAsync();
            const string banner = "<aside style='padding:14px 24px;background:#173641;color:#fff;font:15px system-ui;text-align:center'>READ-ONLY FIXTURE PREVIEW · Sample data · No database, payments or external APIs. Navigation and forms are inactive.</aside>";
            html = html.Replace("<body>", "<body>" + banner);
            context.Response.Body = original;
            await context.Response.WriteAsync(html);
        } else {
            context.Response.Body = original;
            await buffer.CopyToAsync(original);
        }
    } finally { context.Response.Body = original; }
});
app.MapGet("/preview-info", () => new { mode="read-only fixture", view="production Razor", database=false, externalApis=false });
app.MapControllerRoute("preview", "{controller=Preview}/{action=Index}");
app.Run();

public sealed class PreviewController : Controller
{
    public IActionResult Index() => View("~/Views/Home/Index.cshtml", new Resort.Web.ViewModels.HomeVM
        {
            Nights = 2, CheckInDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            VillaList = new[] {
                new Resort.Domain.Entities.Villa { Id=1, Name="Garden Villa", Description="Sample two-night stay with a private garden.", Price=180, Occupancy=4, Sqft=650, IsAvailable=true, VillaAmenity=new List<Resort.Domain.Entities.Amenity>(), ImageUrl="/images/Room Images/1.jpg" },
                new Resort.Domain.Entities.Villa { Id=2, Name="Family Villa", Description="Sample family accommodation with room to unwind.", Price=240, Occupancy=6, Sqft=900, IsAvailable=true, VillaAmenity=new List<Resort.Domain.Entities.Amenity>(), ImageUrl="/images/Room Images/2.jpg" }
            }
        });
}

public sealed class PreviewControllers : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        feature.Controllers.Clear();
        feature.Controllers.Add(typeof(PreviewController).GetTypeInfo());
    }
}
