using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Workhaven.Api.Features.Identity;
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
builder.Services.AddIdentityCore<IdentityUser>()
    .AddEntityFrameworkStores<WorkhavenIdentityDbContext>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql", timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");

app.Run();
