using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SixWaveBackend.Data;
using SixWaveBackend.Models;

var builder = WebApplication.CreateBuilder(args);

// Render (and most hosts) tell the container which port to listen on via $PORT.
// Locally this is unset, so launchSettings.json's port is used instead.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:3000,http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "sixwave_admin";
        options.Cookie.HttpOnly = true;
        // Locally, frontend and backend are different ports of "localhost" - same site, Lax works.
        // Deployed, they're different domains entirely - that needs None+Secure (HTTPS-only) instead.
        options.Cookie.SameSite = builder.Environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
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

// Apply any pending migrations on startup, so a deploy always leaves the
// database schema in sync with the code - no manual migration step needed.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Only meaningful locally: in production the host (Render) already terminates
    // HTTPS at its edge and only ever forwards plain HTTP to the container.
    app.UseHttpsRedirection();
}

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

app.MapGet("/api/catalog", () =>
{
    var now = DateTimeOffset.UtcNow;
    var wave = Catalog.CurrentWave(now);
    return Results.Ok(new CatalogResponse(
        new CatalogWave(wave.Id, wave.Label, wave.EndsOn?.ToString("yyyy-MM-dd")),
        Catalog.Options.Select(o => new CatalogOption(o.Id, o.Kind.ToString().ToUpperInvariant(), o.Label, o.Admits, wave.Prices[o.Id])).ToList()));
})
.WithName("GetCatalog");

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
        PriceNaira = Catalog.PriceFor(option.Id, DateTimeOffset.UtcNow),
    };

    for (var i = 0; i < option.Admits; i++)
        registration.Tickets.Add(new Ticket { RegistrationId = registration.Id, BackupCode = Codes.GenerateBackupCode(), GuestIndex = i });

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

static AdminUserDetail ToDetail(Registration r) => new(
    r.ToAdminUserRow(),
    r.Tickets.OrderBy(t => t.GuestIndex).Select(t => t.ToAdminPass()).ToList(),
    r.Payments.OrderByDescending(p => p.CreatedAt).Select(p => p.ToAdminTransaction()).ToList());

static string DigitsOnly(string s) => new(s.Where(char.IsDigit).ToArray());

var adminGroup = app.MapGroup("/api/admin").RequireAuthorization();

adminGroup.MapGet("/users", async (string? q, string? status, string? kind, int? page, AppDbContext db) =>
{
    const int pageSize = 10;
    var digitsQuery = string.IsNullOrWhiteSpace(q) ? null : DigitsOnly(q);

    var registrations = await db.Registrations.Include(r => r.Tickets).ToListAsync();
    var rows = registrations
        .Where(r => status switch
        {
            "cancelled" => r.Status == RegistrationStatus.Cancelled,
            "paid" => r.Status != RegistrationStatus.Cancelled && r.PaymentStatus == PaymentStatus.Paid,
            "pending" => r.Status != RegistrationStatus.Cancelled && r.PaymentStatus == PaymentStatus.Pending,
            _ => true,
        })
        .Where(r => kind is null || string.Equals(Catalog.Options.First(o => o.Id == r.OptionId).Kind.ToString(), kind, StringComparison.OrdinalIgnoreCase))
        .Where(r => string.IsNullOrWhiteSpace(q) ||
            r.FullName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            r.Reference.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            r.Email.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (digitsQuery!.Length > 0 && DigitsOnly(r.PhoneNumber).Contains(digitsQuery)))
        .Select(r => r.ToAdminUserRow())
        .ToList();

    var currentPage = Math.Max(1, page ?? 1);
    var items = rows.Skip((currentPage - 1) * pageSize).Take(pageSize).ToList();
    return Results.Ok(new PagedResult<AdminUserRow>(items, rows.Count, currentPage, pageSize));
})
.WithName("AdminListUsers");

adminGroup.MapGet("/users/{id:guid}", async (Guid id, AppDbContext db) =>
{
    var registration = await db.Registrations.Include(r => r.Tickets).Include(r => r.Payments)
        .FirstOrDefaultAsync(r => r.Id == id);
    return registration is null ? Results.NotFound() : Results.Ok(ToDetail(registration));
})
.WithName("AdminGetUser");

adminGroup.MapPost("/users/{id:guid}/payments", async (Guid id, RecordPaymentRequest request, ClaimsPrincipal admin, AppDbContext db) =>
{
    var registration = await db.Registrations.Include(r => r.Tickets).Include(r => r.Payments)
        .FirstOrDefaultAsync(r => r.Id == id);
    if (registration is null) return Results.NotFound();
    if (registration.Status == RegistrationStatus.Cancelled)
        return Results.Conflict(new { error = "This registration is cancelled." });
    if (registration.PaymentStatus == PaymentStatus.Paid)
        return Results.Conflict(new { error = "This registration is already paid." });
    if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method) ||
        method is not (PaymentMethod.Pos or PaymentMethod.Cash or PaymentMethod.Bank_Transfer))
        return Results.BadRequest(new { error = "Choose a payment method." });

    // The price locks now, at the wave on sale when the payment is recorded.
    registration.PriceNaira = Catalog.PriceFor(registration.OptionId, DateTimeOffset.UtcNow);
    db.Payments.Add(new Payment
    {
        Reference = Codes.GeneratePaymentReference(),
        RegistrationId = registration.Id,
        AmountNaira = registration.PriceNaira,
        Method = method,
        RecordedBy = admin.FindFirstValue(ClaimTypes.Name),
    });
    registration.PaymentStatus = PaymentStatus.Paid;
    await db.SaveChangesAsync();

    return Results.Ok(ToDetail(registration));
})
.WithName("AdminRecordPayment");

adminGroup.MapPost("/users/{id:guid}/cancel", async (Guid id, AppDbContext db) =>
{
    var registration = await db.Registrations.Include(r => r.Tickets).Include(r => r.Payments)
        .FirstOrDefaultAsync(r => r.Id == id);
    if (registration is null) return Results.NotFound();
    if (registration.Status == RegistrationStatus.Cancelled)
        return Results.Conflict(new { error = "Already cancelled." });
    if (registration.PaymentStatus == PaymentStatus.Paid)
        return Results.Conflict(new { error = "A paid registration needs a refund, not a cancellation." });

    registration.Status = RegistrationStatus.Cancelled;
    foreach (var ticket in registration.Tickets)
        ticket.Status = TicketStatus.Void;
    await db.SaveChangesAsync();

    return Results.Ok(ToDetail(registration));
})
.WithName("AdminCancelRegistration");

static string CsvCell(string value)
{
    var s = value;
    if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
    if (s.Contains('"') || s.Contains(',') || s.Contains('\n')) s = "\"" + s.Replace("\"", "\"\"") + "\"";
    return s;
}

adminGroup.MapGet("/dashboard", async (AppDbContext db) =>
{
    var registrations = await db.Registrations.Include(r => r.Tickets).Include(r => r.Payments).ToListAsync();
    var live = registrations.Where(r => r.Status != RegistrationStatus.Cancelled).ToList();
    var success = registrations.SelectMany(r => r.Payments).Where(p => p.Status == PaymentTransactionStatus.Success).ToList();
    var activeTickets = registrations.SelectMany(r => r.Tickets).Where(t => t.Status != TicketStatus.Void).ToList();
    var lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");

    PurchaseKind[] kinds = [PurchaseKind.Ticket, PurchaseKind.Group, PurchaseKind.Table];
    var byKind = kinds.Select(kind =>
    {
        var ofKind = live.Where(r => Catalog.Options.First(o => o.Id == r.OptionId).Kind == kind).ToList();
        var revenue = ofKind.Where(r => r.PaymentStatus == PaymentStatus.Paid)
            .Sum(r => r.PriceNaira);
        return new AdminKindStat(kind.ToString().ToUpperInvariant(), ofKind.Count, revenue);
    }).ToList();

    PaymentMethod[] methods = [PaymentMethod.Paystack, PaymentMethod.Pos, PaymentMethod.Cash, PaymentMethod.Bank_Transfer];
    var byMethod = methods.Select(method => new AdminMethodStat(
        method.ToString().ToUpperInvariant(), success.Where(p => p.Method == method).Sum(p => p.AmountNaira))).ToList();

    var perDay = new List<AdminDayStat>();
    for (var i = 13; i >= 0; i--)
    {
        var day = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow.AddDays(-i), lagos).ToString("yyyy-MM-dd");
        var count = registrations.Count(r => TimeZoneInfo.ConvertTime(r.CreatedAt, lagos).ToString("yyyy-MM-dd") == day);
        perDay.Add(new AdminDayStat(day, count));
    }

    var recent = registrations.SelectMany(r => r.Payments.Select(p => p.ToAdminTransactionRow(r)))
        .OrderByDescending(t => t.CreatedAt).Take(6).ToList();

    return Results.Ok(new AdminDashboard(
        new AdminRegistrationStats(
            live.Count,
            live.Count(r => r.PaymentStatus == PaymentStatus.Paid),
            live.Count(r => r.PaymentStatus == PaymentStatus.Pending),
            registrations.Count - live.Count),
        success.Sum(p => p.AmountNaira),
        live.Where(r => r.PaymentStatus == PaymentStatus.Pending).Sum(r => Catalog.AmountDue(r, DateTimeOffset.UtcNow)),
        new AdminCheckedInStats(activeTickets.Count(t => t.Status == TicketStatus.Used), activeTickets.Count),
        byKind, byMethod, perDay, recent));
})
.WithName("AdminDashboard");

adminGroup.MapGet("/transactions", async (string? q, string? status, string? method, int? page, AppDbContext db) =>
{
    const int pageSize = 10;
    var payments = await db.Payments.Include(p => p.Registration).ToListAsync();
    var rows = payments.Select(p => p.ToAdminTransactionRow(p.Registration!))
        .Where(t => status is null || string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase))
        .Where(t => method is null || string.Equals(t.Method, method, StringComparison.OrdinalIgnoreCase))
        .Where(t => string.IsNullOrWhiteSpace(q) ||
            t.Reference.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.UserName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.UserReference.Contains(q, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(t => t.CreatedAt)
        .ToList();

    var success = rows.Where(t => t.Status == "SUCCESS").ToList();
    var currentPage = Math.Max(1, page ?? 1);
    var items = rows.Skip((currentPage - 1) * pageSize).Take(pageSize).ToList();
    return Results.Ok(new AdminTransactionsPage(items, rows.Count, currentPage, pageSize, success.Sum(t => t.Amount), success.Count));
})
.WithName("AdminListTransactions");

adminGroup.MapGet("/transactions/export", async (string? q, string? status, string? method, AppDbContext db) =>
{
    var payments = await db.Payments.Include(p => p.Registration).ToListAsync();
    var rows = payments.Select(p => p.ToAdminTransactionRow(p.Registration!))
        .Where(t => status is null || string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase))
        .Where(t => method is null || string.Equals(t.Method, method, StringComparison.OrdinalIgnoreCase))
        .Where(t => string.IsNullOrWhiteSpace(q) ||
            t.Reference.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.UserName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.UserReference.Contains(q, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(t => t.CreatedAt)
        .ToList();

    string[] header = ["Reference", "Date", "Person", "Registration", "Purchase", "Amount (NGN)", "Method", "Status", "Recorded by"];
    var lines = rows.Select(t => string.Join(',', new[]
    {
        t.Reference, t.CreatedAt.ToString("O"), t.UserName, t.UserReference, t.OptionLabel,
        t.Amount.ToString(), t.Method, t.Status, t.RecordedBy ?? "",
    }.Select(CsvCell)));
    var csv = string.Join('\n', new[] { string.Join(',', header.Select(CsvCell)) }.Concat(lines));
    return Results.Text(csv, "text/csv");
})
.WithName("AdminExportTransactions");

adminGroup.MapGet("/transactions/{reference}", async (string reference, AppDbContext db) =>
{
    var payment = await db.Payments.Include(p => p.Registration).FirstOrDefaultAsync(p => p.Reference == reference);
    return payment is null ? Results.NotFound() : Results.Ok(payment.ToAdminTransactionRow(payment.Registration!));
})
.WithName("AdminGetTransaction");

adminGroup.MapGet("/passes", async (string? q, string? status, int? page, AppDbContext db) =>
{
    const int pageSize = 10;
    var tickets = await db.Tickets.Include(t => t.Registration).ToListAsync();
    var totalsByRegistration = tickets.GroupBy(t => t.RegistrationId).ToDictionary(g => g.Key, g => g.Count());

    var all = tickets.Select(t =>
    {
        var option = Catalog.Options.First(o => o.Id == t.Registration!.OptionId);
        return new AdminPassRow(
            t.Id, t.RegistrationId, t.GuestIndex, t.Status.ToString().ToUpperInvariant(), t.UsedAt, null,
            t.Registration!.FullName, t.Registration!.Reference, option.Label, totalsByRegistration[t.RegistrationId]);
    }).ToList();

    var rows = all
        .Where(p => status is null || string.Equals(p.Status, status, StringComparison.OrdinalIgnoreCase))
        .Where(p => string.IsNullOrWhiteSpace(q) ||
            p.Token.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.UserName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.UserReference.Contains(q, StringComparison.OrdinalIgnoreCase))
        .ToList();

    var currentPage = Math.Max(1, page ?? 1);
    var items = rows.Skip((currentPage - 1) * pageSize).Take(pageSize).ToList();
    var counts = new AdminPassCounts(
        all.Count, all.Count(p => p.Status == "USED"), all.Count(p => p.Status == "UNUSED"), all.Count(p => p.Status == "VOID"));
    return Results.Ok(new AdminPassesPage(items, rows.Count, currentPage, pageSize, counts));
})
.WithName("AdminListPasses");

adminGroup.MapPost("/scan", async (ScanRequest request, AppDbContext db) =>
{
    var code = request.Code.Trim();
    var ticket = Guid.TryParse(code, out var ticketId)
        ? await db.Tickets.Include(t => t.Registration).FirstOrDefaultAsync(t => t.Id == ticketId)
        : await db.Tickets.Include(t => t.Registration).FirstOrDefaultAsync(t => t.BackupCode == code.ToUpperInvariant());

    if (ticket is null)
        return Results.Ok(new ScanResponse("INVALID", null, null, null, null, null));

    var registration = ticket.Registration!;
    var option = Catalog.Options.First(o => o.Id == registration.OptionId);
    var paymentStatus = registration.PaymentStatus.ToString().ToUpperInvariant();

    ScanResponse Respond(string decision) =>
        new(decision, registration.Reference, registration.FullName, registration.OptionId, paymentStatus, Catalog.AmountDue(registration, DateTimeOffset.UtcNow));

    if (registration.Status == RegistrationStatus.Cancelled)
        return Results.Ok(Respond("CANCELLED"));
    if (registration.PaymentStatus != PaymentStatus.Paid)
        return Results.Ok(Respond("PAYMENT_REQUIRED"));
    if (ticket.Status != TicketStatus.Unused)
        return Results.Ok(Respond("ALREADY_USED"));

    // Atomic: only flips Unused -> Used if it's still Unused right now, so two
    // staff scanning the same code at once can't both get GRANTED.
    var rowsAffected = await db.Tickets
        .Where(t => t.Id == ticket.Id && t.Status == TicketStatus.Unused)
        .ExecuteUpdateAsync(s => s
            .SetProperty(t => t.Status, TicketStatus.Used)
            .SetProperty(t => t.UsedAt, DateTimeOffset.UtcNow));

    return Results.Ok(Respond(rowsAffected > 0 ? "GRANTED" : "ALREADY_USED"));
})
.WithName("AdminScan");

app.Run();

// Needed so WebApplicationFactory<Program> can find this entry point from the test project.
public partial class Program;
