using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Globalization;
using TEJ0017_FakturacniSystem;
using TEJ0017_FakturacniSystem.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<ApplicationContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("DefaultConnection"),
    sqlServerOptionsAction: sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
        maxRetryCount: 5,
        maxRetryDelay: TimeSpan.FromSeconds(30),
        errorNumbersToAdd: null);
    }
    ));

//Culture info - sync nastaveni s HTML formularem
var cultureInfo = new CultureInfo("en-US");
cultureInfo.DateTimeFormat.FullDateTimePattern = "dd.MM.yyyy HH:mm:ss";
cultureInfo.DateTimeFormat.ShortDatePattern = "dd.MM.yyyy";
cultureInfo.DateTimeFormat.FullDateTimePattern = "HH:mm:ss";
cultureInfo.DateTimeFormat.LongTimePattern = "HH:mm:ss";
CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

//Session (zatim nikde nepouzito)
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true;
    options.MinimumSameSitePolicy = SameSiteMode.None;
});
builder.Services.AddSession(opts =>
{
    opts.Cookie.IsEssential = true;
});

//Authentication
builder.Services.AddAuthentication("Cookies").AddCookie("Cookies", options =>
{
    options.AccessDeniedPath = "/Home/err403";
    options.LoginPath = "/Home/Login";
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "node_modules")),
    RequestPath = new PathString("/vendor")
});

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();


//pokud nebude nacten vstupni soubor "appData.json", spusti se pruvodce nastavenim
if (DataInitializer.getInstance().initConfigFile())
{
    app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

}
else
{
    //run entry guide
    DataInitializer.getInstance().runEntryGuide();
    app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

}

app.Run();
