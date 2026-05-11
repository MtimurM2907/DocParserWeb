using Microsoft.EntityFrameworkCore;
using DocParseLab.Server.Models;

namespace DocParseLab.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<ParsedDocument> ParsedDocuments => Set<ParsedDocument>();

    public DbSet<DocumentShare> DocumentShares => Set<DocumentShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<ParsedDocument>()
            .HasOne(d => d.Owner)
            .WithMany(u => u.Documents)
            .HasForeignKey(d => d.OwnerId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DocumentShare>()
            .HasOne(s => s.Document)
            .WithMany(d => d.Shares)
            .HasForeignKey(s => s.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentShare>()
            .HasOne(s => s.FromUser)
            .WithMany(u => u.SentShares)
            .HasForeignKey(s => s.FromUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentShare>()
            .HasOne(s => s.ToUser)
            .WithMany(u => u.ReceivedShares)
            .HasForeignKey(s => s.ToUserId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}

