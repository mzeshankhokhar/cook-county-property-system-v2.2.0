using CookCountyApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Cook County Property API",
        Version = "v1",
        Description = "Fast, reliable APIs for accessing Cook County property information. Designed for seamless integration into property detail drawers and modals.",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "API Support"
        }
    });
});

// Add memory caching
builder.Services.AddMemoryCache();

// Configure HttpClient with browser-like headers and cookie handling
builder.Services.AddHttpClient("CookCounty", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseCookies = true,
    CookieContainer = new System.Net.CookieContainer(),
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 10
});

builder.Services.AddHttpClient("GIS", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
    client.Timeout = TimeSpan.FromSeconds(20);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = true
});

// Register services
builder.Services.AddScoped<ICookCountyProxyService, CookCountyProxyService>();
builder.Services.AddScoped<IPropertySummaryService, PropertySummaryService>();
builder.Services.AddScoped<ITaxPortalHtmlParser, TaxPortalHtmlParser>();

// Add CORS for frontend integration
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
// Enable Swagger in all environments for easier debugging
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cook County Property API v1");
    c.RoutePrefix = string.Empty; // Serve Swagger UI at root
});

app.UseCors();
app.MapControllers();

// Get port from environment variable or use default
var port = Environment.GetEnvironmentVariable("DOTNET_PORT") ?? "5001";
app.Urls.Add($"http://0.0.0.0:{port}");

Console.WriteLine($"Cook County Property API (.NET) running on port {port}");
Console.WriteLine($"Swagger UI available at http://localhost:{port}/");

app.Run();
