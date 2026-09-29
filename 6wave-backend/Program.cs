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
        policy.WithOrigins("http://localhost:3000").AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");

app.MapHealthChecks("/health");

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

app.Run();

// Needed so WebApplicationFactory<Program> can find this entry point from the test project.
public partial class Program;
