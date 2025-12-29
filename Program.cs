/*
 * ============================================================================
 * STATIONNEMENT - Smart Parking Management System
 * ============================================================================
 * Developer: dev-harshhh19 (Harshad Nikam)
 * GitHub: https://github.com/dev-harshhh19/
 * LinkedIn: https://www.linkedin.com/in/harshad-nikam06/
 * Email: nikamharshadshivaji@gmail.com
 * ============================================================================
 */

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Stationnement.Web.Services;
using Stationnement.Web.Repositories;
using Dapper;

// Load environment variables from .env file
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
        var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }
    Console.WriteLine("[CONFIG] Loaded environment variables from .env file");
}

var builder = WebApplication.CreateBuilder(args);

// Configure Dapper to map snake_case columns to PascalCase properties
DefaultTypeMap.MatchNamesWithUnderscores = true;

// Add services to the container
builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Database connection string - build from environment variables if available
var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
var dbName = Environment.GetEnvironmentVariable("DB_NAME");
var dbUser = Environment.GetEnvironmentVariable("DB_USERNAME");
var dbPass = Environment.GetEnvironmentVariable("DB_PASSWORD");
var dbSsl = Environment.GetEnvironmentVariable("DB_SSLMODE") ?? "Require";

string connectionString;
if (!string.IsNullOrEmpty(dbHost) && !string.IsNullOrEmpty(dbName) && !string.IsNullOrEmpty(dbUser) && !string.IsNullOrEmpty(dbPass))
{
    connectionString = $"Host={dbHost};Database={dbName};Username={dbUser};Password={dbPass};SslMode={dbSsl}";
    Console.WriteLine($"[CONFIG] Using database connection from environment variables: Host={dbHost}, Database={dbName}");
}
else
{
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
    Console.WriteLine("[CONFIG] Using database connection from appsettings.json");
}

// Register repositories
builder.Services.AddScoped<IUserRepository>(sp => new UserRepository(connectionString!));
builder.Services.AddScoped<IParkingRepository>(sp => new ParkingRepository(connectionString!));
builder.Services.AddScoped<IReservationRepository>(sp => new ReservationRepository(connectionString!));
builder.Services.AddScoped<IPaymentRepository>(sp => new PaymentRepository(connectionString!));
builder.Services.AddScoped<ISubscriptionRepository>(sp => new SubscriptionRepository(connectionString!));
builder.Services.AddScoped<IAdminSessionRepository>(sp => new AdminSessionRepository(connectionString!));

// Register services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IPricingService, PricingService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret!)),
        ClockSkew = TimeSpan.Zero
    };

    // Read token from cookie if not in header
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.Request.Cookies.ContainsKey("accessToken"))
            {
                context.Token = context.Request.Cookies["accessToken"];
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("ParkingManager", policy => policy.RequireRole("Admin", "ParkingManager"));
});

// CORS for API
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

// Log the URLs the app is listening on
app.Lifetime.ApplicationStarted.Register(() =>
{
    var urls = app.Urls;
    Console.WriteLine();
    Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║           STATIONNEMENT - Smart Parking System             ║");
    Console.WriteLine("╠════════════════════════════════════════════════════════════╣");
    foreach (var url in urls)
    {
        Console.WriteLine($"║  🌐 Listening on: {url,-40} ║");
    }
    Console.WriteLine("╠════════════════════════════════════════════════════════════╣");
    Console.WriteLine("║  Press Ctrl+C to shut down                                 ║");
    Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
});

app.Run();
