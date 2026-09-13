using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Workhaven.Api.Features.Identity.EmailConfirmation;

namespace Workhaven.Api.Features.Identity;

internal sealed class WorkhavenIdentityDbContext(DbContextOptions<WorkhavenIdentityDbContext> options)
    : IdentityUserContext<IdentityUser>(options)
{
    public DbSet<PendingConfirmationEmail> PendingConfirmationEmails => Set<PendingConfirmationEmail>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.Entity<IdentityUser>().HasIndex(user => user.NormalizedEmail).IsUnique();
        builder.Entity<PendingConfirmationEmail>(email =>
        {
            email.HasKey(pending => pending.UserId);
            email.HasIndex(pending => pending.NextAttemptAt).HasFilter("\"FailedAt\" IS NULL");
            email.HasOne<IdentityUser>().WithMany().HasForeignKey(pending => pending.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
