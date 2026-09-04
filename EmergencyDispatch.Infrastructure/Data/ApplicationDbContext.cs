using EmergencyDispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentMedia> IncidentMedias => Set<IncidentMedia>();
    public DbSet<AiClassification> AiClassifications => Set<AiClassification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Cấu hình bảng User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.FullName).HasMaxLength(100).IsRequired();
            entity.Property(u => u.Email).HasMaxLength(150).IsRequired();
            entity.Property(u => u.PhoneNumber).HasMaxLength(20);
            entity.Property(u => u.GoogleId).HasMaxLength(100);

            // Quan hệ với Station (Staff thuộc Station)
            entity.HasOne(u => u.Station)
                  .WithMany(s => s.StaffMembers)
                  .HasForeignKey(u => u.StationId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Global Query Filter: Tự động bỏ qua các bản ghi đã xóa mềm
            entity.HasQueryFilter(u => !u.IsDeleted);
        });

        // 2. Cấu hình bảng RefreshToken
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.Token).IsUnique();
            entity.Property(r => r.Token).HasMaxLength(500).IsRequired();

            // Quan hệ với User
            entity.HasOne(r => r.User)
                  .WithMany(u => u.RefreshTokens)
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(r => !r.IsDeleted);
        });

        // 3. Cấu hình bảng Station
        modelBuilder.Entity<Station>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).HasMaxLength(200).IsRequired();
            entity.Property(s => s.Address).HasMaxLength(500).IsRequired();
            entity.Property(s => s.PhoneNumber).HasMaxLength(20);

            entity.HasQueryFilter(s => !s.IsDeleted);
        });

        // 4. Cấu hình bảng Incident
        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Title).HasMaxLength(250).IsRequired();
            entity.Property(i => i.Description).HasMaxLength(2000);
            entity.Property(i => i.LocationAddress).HasMaxLength(500).IsRequired();
            entity.Property(i => i.ReporterName).HasMaxLength(100);
            entity.Property(i => i.ReporterPhone).HasMaxLength(20);

            // Quan hệ người báo cáo (User -> Incident: Restrict)
            entity.HasOne(i => i.ReportedByUser)
                  .WithMany()
                  .HasForeignKey(i => i.ReportedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Quan hệ điều phối viên xác minh (User -> Incident: Restrict)
            entity.HasOne(i => i.VerifiedByUser)
                  .WithMany()
                  .HasForeignKey(i => i.VerifiedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(i => !i.IsDeleted);
        });

        // 5. Cấu hình bảng IncidentMedia
        modelBuilder.Entity<IncidentMedia>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.MediaUrl).HasMaxLength(1000).IsRequired();
            entity.Property(m => m.PublicId).HasMaxLength(200);
            entity.Property(m => m.MimeType).HasMaxLength(100);

            // Quan hệ với Incident (Incident -> IncidentMedia: Cascade)
            entity.HasOne(m => m.Incident)
                  .WithMany(i => i.MediaItems)
                  .HasForeignKey(m => m.IncidentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(m => !m.IsDeleted);
        });

        // 6. Cấu hình bảng AiClassification
        modelBuilder.Entity<AiClassification>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Summary).HasMaxLength(1000);
            entity.Property(a => a.ModelName).HasMaxLength(100);
            entity.Property(a => a.ErrorMessage).HasMaxLength(2000);

            // Quan hệ 1 - 1 với Incident (Incident -> AiClassification: Cascade)
            entity.HasOne(a => a.Incident)
                  .WithOne(i => i.AiClassification)
                  .HasForeignKey<AiClassification>(a => a.IncidentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(a => !a.IsDeleted);
        });
    }
}
