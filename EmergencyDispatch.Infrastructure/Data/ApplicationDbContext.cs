using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EmergencyDispatch.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException ex) when (TryGetConflictEntity(ex, out var entityName))
        {
            throw new ConcurrencyConflictException(entityName, ex);
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (TryGetConflictEntity(ex, out var entityName))
        {
            throw new ConcurrencyConflictException(entityName, ex);
        }
    }

    /// <summary>
    /// Các lỗi lưu do ghi đồng thời: xmin không khớp, deadlock, hoặc báo cáo nghiệm thu thứ hai cho cùng một lượt
    /// </summary>
    private static bool TryGetConflictEntity(DbUpdateException ex, out string entityName)
    {
        entityName = ex.Entries.FirstOrDefault()?.Metadata.ClrType.Name ?? "Unknown";

        if (ex is DbUpdateConcurrencyException)
        {
            return true;
        }

        if (ex.InnerException is PostgresException pg)
        {
            if (pg.SqlState == PostgresErrorCodes.DeadlockDetected)
            {
                return true;
            }

            if (pg.SqlState == PostgresErrorCodes.UniqueViolation && pg.ConstraintName == "IX_CompletionReports_AssignmentId")
            {
                entityName = nameof(DispatchAssignment);
                return true;
            }
        }

        return false;
    }

    // 1. User & Auth
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // 2. Station & RescueUnit
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<RescueUnit> RescueUnits => Set<RescueUnit>();

    // 3. Incident & AI
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<DispatchAssignment> DispatchAssignments => Set<DispatchAssignment>();
    public DbSet<IncidentMedia> IncidentMedias => Set<IncidentMedia>();
    public DbSet<AiClassification> AiClassifications => Set<AiClassification>();

    // 4. Dispatch & Completion
    public DbSet<CompletionReport> CompletionReports => Set<CompletionReport>();
    public DbSet<CompletionReportMedia> CompletionReportMedias => Set<CompletionReportMedia>();

    // 5. History & Audit
    public DbSet<IncidentStatusHistory> IncidentStatusHistories => Set<IncidentStatusHistory>();
    public DbSet<AssignmentStatusHistory> AssignmentStatusHistories => Set<AssignmentStatusHistory>();
    public DbSet<IncidentAuditTrail> IncidentAuditTrails => Set<IncidentAuditTrail>();

    // 6. Notifications
    public DbSet<Notification> Notifications => Set<Notification>();

    // 7. Escalation & Feedback & Traffic
    public DbSet<EscalationRule> EscalationRules => Set<EscalationRule>();
    public DbSet<EscalationLog> EscalationLogs => Set<EscalationLog>();
    public DbSet<CitizenFeedback> CitizenFeedbacks => Set<CitizenFeedback>();
    public DbSet<TrafficHotspot> TrafficHotspots => Set<TrafficHotspot>();

    // 8. Security
    public DbSet<BlockedPhoneNumber> BlockedPhoneNumbers => Set<BlockedPhoneNumber>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<SystemAuditLog> SystemAuditLogs => Set<SystemAuditLog>();
    public DbSet<StationTeam> StationTeams => Set<StationTeam>();
    public DbSet<StationTeamMember> StationTeamMembers => Set<StationTeamMember>();

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
            entity.Property(u => u.AvatarUrl).HasMaxLength(500);
            entity.Property(u => u.CitizenIdNumber).HasMaxLength(30);
            entity.Property(u => u.Address).HasMaxLength(300);
            entity.Property(u => u.MedicalNotes).HasMaxLength(2000);
            entity.Property(u => u.ChronicConditions).HasDefaultValueSql("'{}'");
            entity.Property(u => u.Allergies).HasDefaultValueSql("'{}'");
            entity.Property(u => u.CurrentMedications).HasDefaultValueSql("'{}'");
            entity.Property(u => u.MobilityLimitations).HasDefaultValueSql("'{}'");
            entity.Property(u => u.PreferredLanguage).HasMaxLength(50);
            entity.Property(u => u.DependentsNote).HasMaxLength(1000);
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

            entity.Property(u => u.LockReason).HasMaxLength(500);

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

            // Operator phụ trách trạm (các Operator ngang cấp, mỗi người 1 trạm); xóa tài khoản thì trạm chỉ mất người phụ trách
            entity.HasOne(s => s.HeadOperator)
                  .WithMany()
                  .HasForeignKey(s => s.HeadOperatorId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Mỗi Operator chỉ phụ trách 1 trạm
            entity.HasIndex(s => s.HeadOperatorId)
                  .IsUnique()
                  .HasFilter("\"HeadOperatorId\" IS NOT NULL AND \"IsDeleted\" = false")
                  .HasDatabaseName("UQ_Stations_HeadOperator");

            entity.HasQueryFilter(s => !s.IsDeleted);
        });

        // ==================== 4. RESCUE UNIT ====================
        modelBuilder.Entity<RescueUnit>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.PlateNumber).HasMaxLength(50).IsRequired();
            entity.HasIndex(r => r.PlateNumber).IsUnique();

            // Quan hệ với Station (Station -> RescueUnits: Cascade)
            entity.HasOne(r => r.Station)
                  .WithMany(s => s.RescueUnits)
                  .HasForeignKey(r => r.StationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(r => !r.IsDeleted);
        });

        // ==================== 7. INCIDENT ====================
        modelBuilder.Entity<Incident>(entity =>
        {
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasKey(i => i.Id);
            entity.ToTable(t => t.HasCheckConstraint("CK_Incidents_Severity", "\"Severity\" BETWEEN 0 AND 3"));
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

            // Quan hệ người đóng hồ sơ sự cố (User -> Incident: SetNull)
            entity.HasOne(i => i.ClosedByUser)
                  .WithMany()
                  .HasForeignKey(i => i.ClosedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Khóa mềm: Operator đang xử lý sự cố (FR-OP-02, BR-15)
            entity.HasOne(i => i.LockedByOperator)
                  .WithMany()
                  .HasForeignKey(i => i.LockedByOperatorId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Người hủy sự cố (tách khỏi VerifiedBy để không làm sai số liệu RQ3)
            entity.HasOne(i => i.CancelledByUser)
                  .WithMany()
                  .HasForeignKey(i => i.CancelledByUserId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.Property(i => i.CancellationNote).HasMaxLength(500);

            // Trạm phụ trách (phân hàng đợi theo trạm)
            entity.HasOne(i => i.ResponsibleStation)
                  .WithMany()
                  .HasForeignKey(i => i.ResponsibleStationId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(i => new { i.ResponsibleStationId, i.Status }).HasDatabaseName("IX_Incidents_ResponsibleStation_Status");

            // Tự tham chiếu DuplicateOfIncident
            entity.HasOne(i => i.DuplicateOfIncident)
                  .WithMany(d => d.DuplicateIncidents)
                  .HasForeignKey(i => i.DuplicateOfIncidentId)
                  .OnDelete(DeleteBehavior.Restrict);

            // Optimistic concurrency (BR-15): Npgsql ánh xạ uint + IsRowVersion vào cột hệ thống xmin, không tạo cột mới
            entity.Property(i => i.Version).IsRowVersion();

            entity.HasQueryFilter(i => !i.IsDeleted);
        });

        // ==================== 8. DISPATCH ASSIGNMENT ====================
        modelBuilder.Entity<DispatchAssignment>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Version).IsRowVersion();
            entity.HasIndex(d => new { d.IncidentId, d.Status }).HasDatabaseName("IX_DispatchAssignments_Incident_Status");
            entity.HasIndex(d => new { d.RescueUnitId, d.Status }).HasDatabaseName("IX_DispatchAssignments_Unit_Status");
            entity.Property(d => d.Notes).HasMaxLength(1000);
            entity.Property(d => d.RejectionNotes).HasMaxLength(1000);
            entity.Property(d => d.DispatchAlgorithm).HasMaxLength(50);

            entity.HasOne(d => d.AssignedByUser)
                  .WithMany()
                  .HasForeignKey(d => d.AssignedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            // Quan hệ với Incident
            entity.HasOne(d => d.Incident)
                  .WithMany(i => i.DispatchAssignments)
                  .HasForeignKey(d => d.IncidentId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Quan hệ với RescueUnit
            entity.HasOne(d => d.RescueUnit)
                  .WithMany(r => r.DispatchAssignments)
                  .HasForeignKey(d => d.RescueUnitId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(d => !d.IsDeleted);
        });

        // ==================== 9. INCIDENT MEDIA ====================
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

        // ==================== 10. AI CLASSIFICATION ====================
        modelBuilder.Entity<AiClassification>(entity =>
        {
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasKey(a => a.Id);
            entity.ToTable(t => t.HasCheckConstraint("CK_AiClassifications_SeverityScore", "\"SeverityScore\" BETWEEN 0 AND 3"));
            entity.HasIndex(a => a.IncidentId).IsUnique().HasDatabaseName("IX_AiClassifications_IncidentId");

            entity.Property(a => a.ModelName).HasMaxLength(200);

            entity.HasOne(a => a.Incident)
                  .WithOne(i => i.AiClassification)
                  .HasForeignKey<AiClassification>(a => a.IncidentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.TrafficJamVerifiedByUser)
                  .WithMany()
                  .HasForeignKey(a => a.TrafficJamVerifiedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(a => !a.IsDeleted);
        });

        // ==================== 12. COMPLETION REPORT ====================
        modelBuilder.Entity<CompletionReport>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.AssignmentId).IsUnique().HasDatabaseName("IX_CompletionReports_AssignmentId");

            entity.Property(c => c.ActionsSummary).HasMaxLength(2000).IsRequired();
            entity.Property(c => c.HandoverEntity).HasMaxLength(300);
            entity.Property(c => c.HandoverContactName).HasMaxLength(200);
            entity.Property(c => c.HandoverContactPhone).HasMaxLength(30);
            entity.Property(c => c.HandoverNotes).HasMaxLength(1000);
            entity.ToTable(t => t.HasCheckConstraint("CK_CompletionReports_Victims", "\"VictimsAssistedCount\" >= 0"));

            entity.HasOne(c => c.Assignment)
                  .WithOne(a => a.CompletionReport)
                  .HasForeignKey<CompletionReport>(c => c.AssignmentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.SubmittedByUser)
                  .WithMany()
                  .HasForeignKey(c => c.SubmittedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(c => !c.IsDeleted);
        });

        // ==================== 13. COMPLETION REPORT MEDIA ====================
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

        // ==================== 14. INCIDENT STATUS HISTORY ====================
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

        // ==================== 15. ASSIGNMENT STATUS HISTORY ====================
        modelBuilder.Entity<AssignmentStatusHistory>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.HasIndex(h => h.AssignmentId).HasDatabaseName("IX_AssignmentStatusHistory_AssignmentId");
            entity.Property(h => h.Notes).HasMaxLength(1000);

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

        // ==================== 16. INCIDENT AUDIT TRAIL ====================
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

        // ==================== 17. NOTIFICATION ====================
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

        // ==================== 18. ESCALATION RULE ====================
        modelBuilder.Entity<EscalationRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable(t => t.HasCheckConstraint("CK_EscalationRules_MinSeverityLevel", "\"MinSeverityLevel\" BETWEEN 1 AND 3"));
            entity.HasIndex(e => e.MinSeverityLevel).HasDatabaseName("IX_EscalationRules_Severity");
            entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_EscalationRules_Active");

            entity.Property(e => e.NotifyRoles).IsRequired();

            entity.HasQueryFilter(e => !e.IsDeleted);
        });

        // ==================== 19. ESCALATION LOG ====================
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

        // ==================== 21. CITIZEN FEEDBACK ====================
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

        // ==================== 22. TRAFFIC HOTSPOT ====================
        modelBuilder.Entity<TrafficHotspot>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Title).HasMaxLength(200).IsRequired();
            entity.Property(t => t.Description).HasMaxLength(1000);
            entity.Property(t => t.LocationAddress).HasMaxLength(300).IsRequired();
            entity.Property(t => t.MediaUrl).HasMaxLength(500);
            entity.Property(t => t.ReporterName).HasMaxLength(100);
            entity.Property(t => t.ReporterPhone).HasMaxLength(30);

            entity.HasIndex(t => t.IsActive).HasDatabaseName("IX_TrafficHotspots_IsActive");
            entity.HasIndex(t => t.ExpiresAt).HasDatabaseName("IX_TrafficHotspots_ExpiresAt");
            entity.HasIndex(t => t.ReportedByUserId).HasDatabaseName("IX_TrafficHotspots_ReportedBy");

            entity.HasOne(t => t.ReportedByUser)
                  .WithMany(u => u.TrafficHotspots)
                  .HasForeignKey(t => t.ReportedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(t => !t.IsDeleted);
        });

        // ==================== 23. BLOCKED PHONE NUMBER ====================
        modelBuilder.Entity<BlockedPhoneNumber>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.HasIndex(b => b.PhoneNumber).HasDatabaseName("IX_BlockedPhoneNumbers_Phone");

            entity.Property(b => b.PhoneNumber).HasMaxLength(20).IsRequired();
            entity.Property(b => b.Reason).HasMaxLength(500).IsRequired();
            entity.Property(b => b.RemoveReason).HasMaxLength(500);

            entity.HasOne(b => b.Incident)
                  .WithMany()
                  .HasForeignKey(b => b.IncidentId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(b => b.BlockedByUser)
                  .WithMany()
                  .HasForeignKey(b => b.BlockedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(b => b.RemovedByUser)
                  .WithMany()
                  .HasForeignKey(b => b.RemovedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(b => !b.IsDeleted);
        });

        // ==================== 25. STATION TEAM ====================
        modelBuilder.Entity<StationTeam>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => new { t.StationId, t.Name }).IsUnique().HasFilter("\"IsDeleted\" = false").HasDatabaseName("UQ_StationTeams_Station_Name");
            entity.HasIndex(t => t.RescueUnitId).IsUnique().HasFilter("\"RescueUnitId\" IS NOT NULL AND \"IsDeleted\" = false").HasDatabaseName("UQ_StationTeams_RescueUnit");
            entity.Property(t => t.Name).HasMaxLength(100).IsRequired();

            entity.HasOne(t => t.Station)
                  .WithMany()
                  .HasForeignKey(t => t.StationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.RescueUnit)
                  .WithMany()
                  .HasForeignKey(t => t.RescueUnitId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(t => !t.IsDeleted);
        });

        modelBuilder.Entity<StationTeamMember>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => m.StaffId).IsUnique().HasFilter("\"IsDeleted\" = false").HasDatabaseName("UQ_StationTeamMembers_Staff");

            entity.HasOne(m => m.Team)
                  .WithMany(t => t.Members)
                  .HasForeignKey(m => m.TeamId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(m => m.Staff)
                  .WithOne(u => u.TeamMembership)
                  .HasForeignKey<StationTeamMember>(m => m.StaffId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(m => !m.IsDeleted);
        });

        // ==================== 24. SYSTEM SETTING ====================
        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.Key).IsUnique().HasDatabaseName("UQ_SystemSettings_Key");
            entity.Property(s => s.Key).HasMaxLength(100).IsRequired();
            entity.Property(s => s.ValueJson).IsRequired();

            entity.HasOne(s => s.UpdatedByUser)
                  .WithMany()
                  .HasForeignKey(s => s.UpdatedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasQueryFilter(s => !s.IsDeleted);
        });

        // ==================== 25. SYSTEM AUDIT LOG (BR-AD-06) ====================
        modelBuilder.Entity<SystemAuditLog>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => l.Timestamp).HasDatabaseName("IX_SystemAuditLogs_Timestamp");
            entity.HasIndex(l => new { l.EntityType, l.EntityId }).HasDatabaseName("IX_SystemAuditLogs_Entity");
            entity.HasIndex(l => l.ActorId).HasDatabaseName("IX_SystemAuditLogs_Actor");

            entity.Property(l => l.ActorName).HasMaxLength(200);
            entity.Property(l => l.ActorRole).HasMaxLength(50);
            entity.Property(l => l.EntityType).HasMaxLength(50).IsRequired();
            entity.Property(l => l.EntityName).HasMaxLength(300);
            entity.Property(l => l.Description).HasMaxLength(1000).IsRequired();
            entity.Property(l => l.IpAddress).HasMaxLength(64);
            entity.Property(l => l.Action).HasConversion<string>().HasMaxLength(50);

            // Không dùng FK tới Users để nhật ký vẫn còn khi tài khoản bị xóa
            entity.HasQueryFilter(l => !l.IsDeleted);
        });

        // BaseEntity tự gán Id = Guid.NewGuid() ở client. Nếu để EF coi khóa là "tự sinh",
        // bản ghi con thêm qua navigation của entity đang được theo dõi (vd. incident.StatusHistories.Add)
        // sẽ bị hiểu nhầm là bản ghi cũ → UPDATE 0 dòng → DbUpdateConcurrencyException.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(BaseEntity).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(BaseEntity.Id))
                .ValueGeneratedNever();
        }
    }
}
