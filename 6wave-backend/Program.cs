using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SixWaveBackend.Data;
using SixWaveBackend.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
            .AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "sixwave_admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
    options.OnRejected = (ctx, _) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return ValueTask.CompletedTask;
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapPost("/api/admin/auth/login", async (AdminLoginRequest request, IConfiguration config, HttpContext http) =>
{
    var adminEmail = config["Admin:Email"];
    var adminPasswordHash = config["Admin:PasswordHash"];
    if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPasswordHash))
        return Results.Problem("Admin account not configured", statusCode: 500);

    var hasher = new PasswordHasher<object>();
    var passwordResult = hasher.VerifyHashedPassword(null!, adminPasswordHash, request.Password);
    var emailMatches = string.Equals(request.Email.Trim(), adminEmail, StringComparison.OrdinalIgnoreCase);
    if (!emailMatches || passwordResult == PasswordVerificationResult.Failed)
        return Results.Unauthorized();

    Claim[] claims =
    [
        new(ClaimTypes.NameIdentifier, "admin"),
        new(ClaimTypes.Name, "Admin"),
        new(ClaimTypes.Email, adminEmail),
    ];
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    return Results.Ok(new AdminUserResponse("admin", "Admin", adminEmail));
})
.RequireRateLimiting("login")
.WithName("AdminLogin");

app.MapGet("/api/admin/auth/me", (ClaimsPrincipal user) =>
{
    var response = new AdminUserResponse(
        user.FindFirstValue(ClaimTypes.NameIdentifier)!,
        user.FindFirstValue(ClaimTypes.Name)!,
        user.FindFirstValue(ClaimTypes.Email)!);
    return Results.Ok(response);
})
.RequireAuthorization()
.WithName("AdminMe");

app.MapPost("/api/admin/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
})
.WithName("AdminLogout");

app.MapPost("/api/registrations", async (CreateRegistrationRequest request, AppDbContext db) =>
{
    var option = Catalog.Options.FirstOrDefault(o => o.Id == request.OptionId);
    if (option is null)
        return Results.BadRequest(new { error = "Unknown optionId" });

    if (string.IsNullOrWhiteSpace(request.FullName) ||
        string.IsNullOrWhiteSpace(request.Phone) ||
        string.IsNullOrWhiteSpace(request.Email))
        return Results.BadRequest(new { error = "fullName, phone and email are required" });

    var registration = new Registration
    {
        Reference = Codes.GenerateReference(),
        FullName = request.FullName.Trim(),
        Email = request.Email.Trim(),
        PhoneNumber = request.Phone.Trim(),
        OptionId = option.Id,
    };

    for (var i = 0; i < option.Admits; i++)
        registration.Tickets.Add(new Ticket { RegistrationId = registration.Id, BackupCode = Codes.GenerateBackupCode() });

    db.Registrations.Add(registration);
    await db.SaveChangesAsync();

    return Results.Created($"/api/registrations/{registration.Id}", registration.ToResponse());
})
.WithName("CreateRegistration");

app.MapGet("/api/registrations/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var registration = await db.Registrations.FindAsync(id);
    return registration is null ? Results.NotFound() : Results.Ok(registration.ToResponse());
})
.WithName("GetRegistration");

app.MapGet("/api/registrations/{id:guid}/qr", async (Guid id, AppDbContext db) =>
{
    var exists = await db.Registrations.AnyAsync(r => r.Id == id);
    if (!exists) return Results.NotFound();

    var tokens = await db.Tickets.Where(t => t.RegistrationId == id).Select(t => t.Id.ToString()).ToListAsync();
    return Results.Ok(new RegistrationQrResponse(id, tokens));
})
.WithName("GetRegistrationQr");

app.MapGet("/api/registrations/lookup", async (string reference, string phone, AppDbContext db) =>
{
    var normalizedReference = reference.Trim().ToUpperInvariant();
    var registration = await db.Registrations.FirstOrDefaultAsync(r =>
        r.Reference == normalizedReference && r.PhoneNumber == phone.Trim());
    return registration is null ? Results.NotFound() : Results.Ok(registration.ToResponse());
})
.WithName("LookupRegistration");

app.Run();

// Needed so WebApplicationFactory<Program> can find this entry point from the test project.
public partial class Program;
