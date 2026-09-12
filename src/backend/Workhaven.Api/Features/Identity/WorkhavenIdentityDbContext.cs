using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Workhaven.Api.Features.Identity;

internal sealed class WorkhavenIdentityDbContext(DbContextOptions<WorkhavenIdentityDbContext> options)
    : IdentityUserContext<IdentityUser>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.Entity<IdentityUser>().HasIndex(user => user.NormalizedEmail).IsUnique();
    }
}
