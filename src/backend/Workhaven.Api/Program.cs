using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Workhaven.Api.Features.Identity;
using Workhaven.Api.Features.Identity.Registration;
using Workhaven.Api.Infrastructure.Database;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration("Database")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(serviceProvider =>
{
    var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    var connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = database.Host,
        Port = database.Port,
        Database = database.Name,
        Username = database.Username,
        Password = database.Password,
        // Also bounds the separate connection Npgsql opens to cancel a stalled query.
        Timeout = 2
    };

    return NpgsqlDataSource.Create(connectionString.ConnectionString);
});

builder.Services.AddDbContext<WorkhavenIdentityDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        // Email validation governs usernames because registration uses the email as the username.
        options.User.AllowedUserNameCharacters = string.Empty;
        options.SignIn.RequireConfirmedEmail = true;
        options.Password.RequiredLength = 15;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddUserManager<AspNetUserManager<IdentityUser>>()
    .AddEntityFrameworkStores<WorkhavenIdentityDbContext>();

builder.Services.AddRegistration();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql", timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError
});
app.UseStatusCodePages();
app.UseRateLimiter();

app.MapRegistration();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");

app.Run();
