using EmergencyDispatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    // 1. User & Auth
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // 2. Station, Vehicle & Equipment
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Equipment> Equipments => Set<Equipment>();

    // 3. Incident & AI
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentMedia> IncidentMedias => Set<IncidentMedia>();
    public DbSet<AiClassification> AiClassifications => Set<AiClassification>();

    // 4. Dispatch & Completion
    public DbSet<IncidentAssignment> IncidentAssignments => Set<IncidentAssignment>();
    public DbSet<CompletionReport> CompletionReports => Set<CompletionReport>();
    public DbSet<CompletionReportMedia> CompletionReportMedias => Set<CompletionReportMedia>();

    // 5. History & Audit
    public DbSet<IncidentStatusHistory> IncidentStatusHistories => Set<IncidentStatusHistory>();
    public DbSet<AssignmentStatusHistory> AssignmentStatusHistories => Set<AssignmentStatusHistory>();
    public DbSet<IncidentAuditTrail> IncidentAuditTrails => Set<IncidentAuditTrail>();

    // 6. Notifications & Tracking
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<LocationUpdate> LocationUpdates => Set<LocationUpdate>();

    // 7. Escalation & Feedback
    public DbSet<EscalationRule> EscalationRules => Set<EscalationRule>();
    public DbSet<EscalationLog> EscalationLogs => Set<EscalationLog>();
    public DbSet<CitizenFeedback> CitizenFeedbacks => Set<CitizenFeedback>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ==================== 1. USER ====================
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique().HasDatabaseName("IX_Users_Email");
            entity.HasIndex(u => u.Role).HasDatabaseName("IX_Users_Role");
            entity.HasIndex(u => u.StationId).HasDatabaseName("IX_Users_StationId");
            entity.HasIndex(u => new { u.IsDeleted, u.Status }).HasDatabaseName("IX_Users_Active");

            entity.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            entity.Property(u => u.Email).HasMaxLength(255).IsRequired();
            entity.Property(u => u.PhoneNumber).HasMaxLength(20);
            entity.Property(u => u.PasswordHash).HasMaxLength(500);
            entity.Property(u => u.GoogleId).HasMaxLength(100);
            entity.Property(u => u.AvatarUrl).HasMaxLength(500);
            entity.Property(u => u.CitizenIdNumber).HasMaxLength(20);
            entity.Property(u => u.EmergencyContactName).HasMaxLength(200);
            entity.Property(u => u.EmergencyContactPhone).HasMaxLength(20);
            entity.Property(u => u.EmergencyContactRelationship).HasMaxLength(50);
            entity.Property(u => u.FcmToken).HasMaxLength(500);
            entity.Property(u => u.EmailVerificationToken).HasMaxLength(100);
            entity.Property(u => u.PasswordResetToken).HasMaxLength(100);

            // Quan hệ với Station (Staff thuộc Station, nullable)
            entity.HasOne(u => u.Station)
                  .WithMany(s => s.StaffMembers)
                  .HasForeignKey(u => u.StationId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(u => !u.IsDeleted);
        });

        // ==================== 2. REFRESH TOKEN ====================
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.UserId).HasDatabaseName("IX_RefreshTokens_UserId");
            entity.HasIndex(r => r.Token).HasDatabaseName("IX_RefreshTokens_Token");
            entity.Property(r => r.Token).HasMaxLength(500).IsRequired();
            entity.Property(r => r.ReplacedByToken).HasMaxLength(500);

            entity.HasOne(r => r.User)
                  .WithMany(u => u.RefreshTokens)
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(r => !r.IsDeleted);
        });

        // ==================== 3. STATION ====================
        modelBuilder.Entity<Station>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.Name).IsUnique().HasDatabaseName("IX_Stations_Name");
            entity.HasIndex(s => new { s.Latitude, s.Longitude }).HasDatabaseName("IX_Stations_GeoLocation");
            entity.HasIndex(s => s.IsActive).HasDatabaseName("IX_Stations_IsActive");

            entity.Property(s => s.Name).HasMaxLength(200).IsRequired();
            entity.Property(s => s.Address).IsRequired();
            entity.Property(s => s.PhoneNumber).HasMaxLength(20);
            entity.Property(s => s.Email).HasMaxLength(255);
            entity.Property(s => s.OperatingHours).HasMaxLength(100);

            entity.HasQueryFilter(s => !s.IsDeleted);
        });

        // ==================== 4. VEHICLE ====================
        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.HasKey(v => v.Id);
            entity.HasIndex(v => v.LicensePlate).IsUnique().HasDatabaseName("IX_Vehicles_LicensePlate");
            entity.HasIndex(v => v.StationId).HasDatabaseName("IX_Vehicles_StationId");
            entity.HasIndex(v => v.Status).HasDatabaseName("IX_Vehicles_Status");
            entity.HasIndex(v => new { v.Status, v.StationId }).HasDatabaseName("IX_Vehicles_Available_ByStation");

            entity.Property(v => v.LicensePlate).HasMaxLength(20).IsRequired();
            entity.Property(v => v.Name).HasMaxLength(200).IsRequired();

            entity.HasOne(v => v.Station)
                  .WithMany(s => s.Vehicles)
                  .HasForeignKey(v => v.StationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(v => !v.IsDeleted);
        });

        // ==================== 5. EQUIPMENT ====================
        modelBuilder.Entity<Equipment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.VehicleId).HasDatabaseName("IX_Equipment_VehicleId");

            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Condition).HasMaxLength(50).IsRequired();

            entity.HasOne(e => e.Vehicle)
                  .WithMany(v => v.Equipments)
                  .HasForeignKey(e => e.VehicleId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        // ==================== 6. INCIDENT ====================
        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.HasIndex(i => i.Status).HasDatabaseName("IX_Incidents_Status");
            entity.HasIndex(i => i.Severity).HasDatabaseName("IX_Incidents_Severity");
            entity.HasIndex(i => new { i.Status, i.Severity }).HasDatabaseName("IX_Incidents_Queue");
            entity.HasIndex(i => i.ReportedByUserId).HasDatabaseName("IX_Incidents_ReportedBy");
            entity.HasIndex(i => new { i.Latitude, i.Longitude }).HasDatabaseName("IX_Incidents_GeoLocation");
            entity.HasIndex(i => i.CreatedAt).HasDatabaseName("IX_Incidents_CreatedAt");
            entity.HasIndex(i => new { i.IsDeleted, i.Status }).HasDatabaseName("IX_Incidents_Active");

            entity.Property(i => i.Title).HasMaxLength(300).IsRequired();
            entity.Property(i => i.LocationAddress).HasMaxLength(500);
            entity.Property(i => i.ReporterName).HasMaxLength(200);
            entity.Property(i => i.ReporterPhone).HasMaxLength(20);

            // Quan hệ ReportedByUser (Citizen báo cáo)
            entity.HasOne(i => i.ReportedByUser)
                  .WithMany()
                  .HasForeignKey(i => i.ReportedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Quan hệ VerifiedByUser (Operator xác minh)
            entity.HasOne(i => i.VerifiedByUser)
                  .WithMany()
                  .HasForeignKey(i => i.VerifiedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Tự tham chiếu DuplicateOfIncident
            entity.HasOne(i => i.DuplicateOfIncident)
                  .WithMany(d => d.DuplicateIncidents)
                  .HasForeignKey(i => i.DuplicateOfIncidentId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(i => !i.IsDeleted);
        });

        // ==================== 7. INCIDENT MEDIA ====================
        modelBuilder.Entity<IncidentMedia>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => m.IncidentId).HasDatabaseName("IX_IncidentMedia_IncidentId");

            entity.Property(m => m.MediaUrl).HasMaxLength(1000).IsRequired();
            entity.Property(m => m.PublicId).HasMaxLength(500);
            entity.Property(m => m.MimeType).HasMaxLength(50);

            entity.HasOne(m => m.Incident)
                  .WithMany(i => i.MediaItems)
                  .HasForeignKey(m => m.IncidentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(m => !m.IsDeleted);
        });

        // ==================== 8. AI CLASSIFICATION ====================
        modelBuilder.Entity<AiClassification>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => a.IncidentId).IsUnique().HasDatabaseName("IX_AiClassifications_IncidentId");

            entity.Property(a => a.ModelName).HasMaxLength(200);

            entity.HasOne(a => a.Incident)
                  .WithOne(i => i.AiClassification)
                  .HasForeignKey<AiClassification>(a => a.IncidentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(a => !a.IsDeleted);
        });

        // ==================== 9. INCIDENT ASSIGNMENT ====================
        modelBuilder.Entity<IncidentAssignment>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => a.IncidentId).HasDatabaseName("IX_Assignments_IncidentId");
            entity.HasIndex(a => new { a.StaffId, a.Status }).HasDatabaseName("IX_Assignments_Staff_Status");
            entity.HasIndex(a => new { a.IncidentId, a.StaffId }).IsUnique().HasDatabaseName("UQ_Assignments_Incident_Staff");
            entity.HasIndex(a => a.Status).HasDatabaseName("IX_Assignments_Status");

            entity.Property(a => a.DispatchAlgorithm).HasMaxLength(50);

            entity.HasOne(a => a.Incident)
                  .WithMany(i => i.IncidentAssignments)
                  .HasForeignKey(a => a.IncidentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Staff)
                  .WithMany(u => u.AssignedJobs)
                  .HasForeignKey(a => a.StaffId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.Vehicle)
                  .WithMany(v => v.IncidentAssignments)
                  .HasForeignKey(a => a.VehicleId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.AssignedByUser)
                  .WithMany()
                  .HasForeignKey(a => a.AssignedByUserId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(a => !a.IsDeleted);
        });

        // ==================== 10. COMPLETION REPORT ====================
        modelBuilder.Entity<CompletionReport>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.AssignmentId).IsUnique().HasDatabaseName("IX_CompletionReports_AssignmentId");

            entity.Property(c => c.Notes).IsRequired();
            entity.Property(c => c.QrCode).HasMaxLength(500);

            entity.HasOne(c => c.Assignment)
                  .WithOne(a => a.CompletionReport)
                  .HasForeignKey<CompletionReport>(c => c.AssignmentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.QrScannedByUser)
                  .WithMany()
                  .HasForeignKey(c => c.QrScannedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(c => !c.IsDeleted);
        });

        // ==================== 11. COMPLETION REPORT MEDIA ====================
        modelBuilder.Entity<CompletionReportMedia>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => m.CompletionReportId).HasDatabaseName("IX_CompletionReportMedia_ReportId");

            entity.Property(m => m.MediaUrl).HasMaxLength(1000).IsRequired();
            entity.Property(m => m.PublicId).HasMaxLength(500);

            entity.HasOne(m => m.CompletionReport)
                  .WithMany(c => c.MediaItems)
                  .HasForeignKey(m => m.CompletionReportId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(m => !m.IsDeleted);
        });

        // ==================== 12. INCIDENT STATUS HISTORY ====================
        modelBuilder.Entity<IncidentStatusHistory>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.HasIndex(h => h.IncidentId).HasDatabaseName("IX_IncidentStatusHistory_IncidentId");
            entity.HasIndex(h => h.ChangedAt).HasDatabaseName("IX_IncidentStatusHistory_ChangedAt");

            entity.Property(h => h.ChangedByRole).HasMaxLength(50);

            entity.HasOne(h => h.Incident)
                  .WithMany(i => i.StatusHistories)
                  .HasForeignKey(h => h.IncidentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(h => h.ChangedByUser)
                  .WithMany()
                  .HasForeignKey(h => h.ChangedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(h => !h.IsDeleted);
        });

        // ==================== 13. ASSIGNMENT STATUS HISTORY ====================
        modelBuilder.Entity<AssignmentStatusHistory>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.HasIndex(h => h.AssignmentId).HasDatabaseName("IX_AssignmentStatusHistory_AssignmentId");

            entity.HasOne(h => h.Assignment)
                  .WithMany(a => a.StatusHistories)
                  .HasForeignKey(h => h.AssignmentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(h => h.ChangedByUser)
                  .WithMany()
                  .HasForeignKey(h => h.ChangedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(h => !h.IsDeleted);
        });

        // ==================== 14. INCIDENT AUDIT TRAIL ====================
        modelBuilder.Entity<IncidentAuditTrail>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => a.IncidentId).HasDatabaseName("IX_AuditTrail_IncidentId");
            entity.HasIndex(a => a.ActorId).HasDatabaseName("IX_AuditTrail_ActorId");
            entity.HasIndex(a => a.Action).HasDatabaseName("IX_AuditTrail_Action");
            entity.HasIndex(a => a.Timestamp).HasDatabaseName("IX_AuditTrail_Timestamp");
            entity.HasIndex(a => new { a.IncidentId, a.Timestamp }).HasDatabaseName("IX_AuditTrail_Incident_Time");

            entity.Property(a => a.ActorRole).HasMaxLength(50);
            entity.Property(a => a.IpAddress).HasMaxLength(50);
            entity.Property(a => a.UserAgent).HasMaxLength(500);

            entity.HasOne(a => a.Incident)
                  .WithMany(i => i.AuditTrails)
                  .HasForeignKey(a => a.IncidentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Actor)
                  .WithMany()
                  .HasForeignKey(a => a.ActorId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(a => !a.IsDeleted);
        });

        // ==================== 15. NOTIFICATION ====================
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.HasIndex(n => new { n.UserId, n.IsRead }).HasDatabaseName("IX_Notifications_User_Read");
            entity.HasIndex(n => new { n.UserId, n.CreatedAt }).HasDatabaseName("IX_Notifications_User_Time");
            entity.HasIndex(n => n.Type).HasDatabaseName("IX_Notifications_Type");

            entity.Property(n => n.Title).HasMaxLength(300).IsRequired();
            entity.Property(n => n.Message).IsRequired();
            entity.Property(n => n.FcmMessageId).HasMaxLength(200);

            entity.HasOne(n => n.User)
                  .WithMany(u => u.Notifications)
                  .HasForeignKey(n => n.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(n => n.Incident)
                  .WithMany()
                  .HasForeignKey(n => n.IncidentId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(n => !n.IsDeleted);
        });

        // ==================== 16. ESCALATION RULE ====================
        modelBuilder.Entity<EscalationRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MinSeverityLevel).HasDatabaseName("IX_EscalationRules_Severity");
            entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_EscalationRules_Active");

            entity.Property(e => e.NotifyRoles).IsRequired();

            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        // ==================== 17. ESCALATION LOG ====================
        modelBuilder.Entity<EscalationLog>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => l.IncidentId).HasDatabaseName("IX_EscalationLogs_IncidentId");
            entity.HasIndex(l => l.TriggerTime).HasDatabaseName("IX_EscalationLogs_TriggerTime");

            entity.Property(l => l.NotifiedRoles).HasMaxLength(200);

            entity.HasOne(l => l.Incident)
                  .WithMany()
                  .HasForeignKey(l => l.IncidentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(l => l.EscalationRule)
                  .WithMany(r => r.Logs)
                  .HasForeignKey(l => l.EscalationRuleId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(l => !l.IsDeleted);
        });

        // ==================== 18. LOCATION UPDATE ====================
        modelBuilder.Entity<LocationUpdate>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => new { u.UserId, u.Timestamp }).HasDatabaseName("IX_LocationUpdates_User_Time");
            entity.HasIndex(u => u.Timestamp).HasDatabaseName("IX_LocationUpdates_Timestamp");

            entity.HasOne(u => u.User)
                  .WithMany(u => u.LocationUpdates)
                  .HasForeignKey(u => u.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(u => !u.IsDeleted);
        });

        // ==================== 19. CITIZEN FEEDBACK ====================
        modelBuilder.Entity<CitizenFeedback>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.HasIndex(f => f.IncidentId).IsUnique().HasDatabaseName("IX_CitizenFeedbacks_IncidentId");
            entity.HasIndex(f => f.CitizenUserId).HasDatabaseName("IX_CitizenFeedbacks_CitizenId");

            entity.HasOne(f => f.Incident)
                  .WithOne(i => i.CitizenFeedback)
                  .HasForeignKey<CitizenFeedback>(f => f.IncidentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(f => f.CitizenUser)
                  .WithMany(u => u.CitizenFeedbacks)
                  .HasForeignKey(f => f.CitizenUserId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(f => !f.IsDeleted);
        });
    }
}
