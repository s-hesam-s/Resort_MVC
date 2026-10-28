using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Resort.Web.Configuration;

public static class MvcConfiguration
{
    public static IMvcBuilder AddResortMvc(this IServiceCollection services)
    {
        services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 3 * 1024 * 1024);
        return services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
    }
}
