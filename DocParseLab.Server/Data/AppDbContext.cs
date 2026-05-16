using Microsoft.EntityFrameworkCore;
using DocParseLab.Server.Models;

namespace DocParseLab.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<ParsedDocument> ParsedDocuments => Set<ParsedDocument>();

    public DbSet<DocumentShare> DocumentShares => Set<DocumentShare>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DbSet<DocumentWorkflowHistory> DocumentWorkflowHistory => Set<DocumentWorkflowHistory>();

    public DbSet<DocumentSignature> DocumentSignatures => Set<DocumentSignature>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<AppUser>()
            .HasOne(u => u.Department)
            .WithMany(d => d.Users)
            .HasForeignKey(u => u.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Department>()
            .HasIndex(d => d.Name)
            .IsUnique();

        modelBuilder.Entity<ParsedDocument>()
            .HasOne(d => d.Owner)
            .WithMany(u => u.Documents)
            .HasForeignKey(d => d.OwnerId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ParsedDocument>()
            .HasOne(d => d.Department)
            .WithMany(dep => dep.Documents)
            .HasForeignKey(d => d.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ParsedDocument>()
            .HasOne(d => d.ResponsibleUser)
            .WithMany()
            .HasForeignKey(d => d.ResponsibleUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ParsedDocument>()
            .HasOne(d => d.CurrentApprover)
            .WithMany()
            .HasForeignKey(d => d.CurrentApproverUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ParsedDocument>()
            .HasIndex(d => d.WorkflowStatus);

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

        modelBuilder.Entity<AuditLogEntry>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AuditLogEntry>()
            .HasIndex(e => e.CreatedAt);

        modelBuilder.Entity<DocumentVersion>()
            .HasOne(v => v.Document)
            .WithMany(d => d.Versions)
            .HasForeignKey(v => v.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentVersion>()
            .HasOne(v => v.CreatedByUser)
            .WithMany()
            .HasForeignKey(v => v.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DocumentVersion>()
            .HasIndex(v => new { v.DocumentId, v.VersionNumber })
            .IsUnique();

        modelBuilder.Entity<DocumentWorkflowHistory>()
            .HasOne(h => h.Document)
            .WithMany(d => d.WorkflowHistory)
            .HasForeignKey(h => h.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentWorkflowHistory>()
            .HasOne(h => h.User)
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DocumentWorkflowHistory>()
            .HasIndex(h => h.CreatedAt);

        modelBuilder.Entity<DocumentSignature>()
            .HasOne(s => s.Document)
            .WithMany(d => d.Signatures)
            .HasForeignKey(s => s.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentSignature>()
            .HasOne(s => s.SignedByUser)
            .WithMany()
            .HasForeignKey(s => s.SignedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentSignature>()
            .HasIndex(s => new { s.DocumentId, s.SignedAt });
    }
}
