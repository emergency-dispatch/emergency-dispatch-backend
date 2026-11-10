using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Type_Catalogs_Auto_Dispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Loại xe: enum int -> mã danh mục (1 Ambulance ... 6 PolicePatrol)
            migrationBuilder.Sql(@"ALTER TABLE ""RescueUnits"" ALTER COLUMN ""UnitType"" TYPE character varying(50) USING (CASE ""UnitType"" WHEN 1 THEN 'Ambulance' WHEN 2 THEN 'FireTruck' WHEN 3 THEN 'LadderTruck' WHEN 4 THEN 'RescueBoat' WHEN 5 THEN 'HeavyRescueVehicle' WHEN 6 THEN 'PolicePatrol' END);");

            migrationBuilder.AddColumn<string>(
                name: "AutoDispatchNote",
                table: "Incidents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AutoDispatchState",
                table: "Incidents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IncidentTypeCode",
                table: "Incidents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedIncidentTypeCode",
                table: "Incidents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SuspectedDuplicateOfIncidentId",
                table: "Incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"ALTER TABLE ""FieldRequests"" ALTER COLUMN ""RequestedUnitType"" TYPE character varying(50) USING (CASE ""RequestedUnitType"" WHEN 1 THEN 'Ambulance' WHEN 2 THEN 'FireTruck' WHEN 3 THEN 'LadderTruck' WHEN 4 THEN 'RescueBoat' WHEN 5 THEN 'HeavyRescueVehicle' WHEN 6 THEN 'PolicePatrol' END);");

            migrationBuilder.AddColumn<string>(
                name: "ConfidenceSource",
                table: "AiClassifications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IncidentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameVi = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentTypes", x => x.Id);
                    table.UniqueConstraint("AK_IncidentTypes_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "RescueUnitTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameVi = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RescueUnitTypes", x => x.Id);
                    table.UniqueConstraint("AK_RescueUnitTypes_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "IncidentTypeUnitRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitTypeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MinSeverity = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentTypeUnitRules", x => x.Id);
                    table.CheckConstraint("CK_IncidentTypeUnitRules_Quantity", "\"Quantity\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_IncidentTypeUnitRules_IncidentTypes_IncidentTypeId",
                        column: x => x.IncidentTypeId,
                        principalTable: "IncidentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IncidentTypeUnitRules_RescueUnitTypes_UnitTypeCode",
                        column: x => x.UnitTypeCode,
                        principalTable: "RescueUnitTypes",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            // Seed danh mục hệ thống (trước khi tạo khóa ngoại) + loại mẫu "Đuối nước / ngập" và quy tắc loại xe theo luật BR-13 cũ
            migrationBuilder.Sql(@"INSERT INTO ""RescueUnitTypes"" (""Id"",""Code"",""NameVi"",""NameEn"",""Description"",""Icon"",""Color"",""IsSystem"",""IsActive"",""DisplayOrder"",""CreatedAt"",""IsDeleted"") VALUES
('8f1d0b7a-0001-4c2e-9a10-000000000001','Ambulance','Xe cấp cứu','Ambulance','Xe cấp cứu y tế 115','ambulance','#e53935',true,true,1,now(),false),
('8f1d0b7a-0001-4c2e-9a10-000000000002','FireTruck','Xe chữa cháy','Fire truck','Xe chữa cháy tiêu chuẩn','fire-truck','#d84315',true,true,2,now(),false),
('8f1d0b7a-0001-4c2e-9a10-000000000003','LadderTruck','Xe thang','Ladder truck','Xe thang chữa cháy cứu nạn nhà cao tầng','ladder','#ef6c00',true,true,3,now(),false),
('8f1d0b7a-0001-4c2e-9a10-000000000004','RescueBoat','Xuồng cứu nạn','Rescue boat','Cano / thuyền cứu nạn đường thủy','boat','#1e88e5',true,true,4,now(),false),
('8f1d0b7a-0001-4c2e-9a10-000000000005','HeavyRescueVehicle','Xe cứu hộ hạng nặng','Heavy rescue vehicle','Xe cứu nạn đặc chủng (cắt kim loại, cẩu nâng)','crane','#6d4c41',true,true,5,now(),false),
('8f1d0b7a-0001-4c2e-9a10-000000000006','PolicePatrol','Xe tuần tra','Patrol vehicle','Xe tuần tra phản ứng nhanh','patrol','#3949ab',true,true,6,now(),false);");
            migrationBuilder.Sql(@"INSERT INTO ""IncidentTypes"" (""Id"",""Code"",""NameVi"",""NameEn"",""Description"",""Icon"",""Color"",""Category"",""IsSystem"",""IsActive"",""DisplayOrder"",""CreatedAt"",""IsDeleted"") VALUES
('8f1d0b7a-0002-4c2e-9a10-000000000001','FireExplosion','Cháy nổ','Fire / explosion','AI nhận diện được','fire','#d84315',1,true,true,1,now(),false),
('8f1d0b7a-0002-4c2e-9a10-000000000002','TrafficAccident','Tai nạn giao thông','Traffic accident','AI nhận diện được','car-crash','#f9a825',2,true,true,2,now(),false),
('8f1d0b7a-0002-4c2e-9a10-000000000003','Other','Sự cố khác','Other emergency','Điều phối viên thẩm định thủ công','alert','#757575',3,true,true,99,now(),false),
('8f1d0b7a-0002-4c2e-9a10-000000000004','FloodDrowning','Đuối nước / ngập','Drowning / flooding','Loại mẫu thuộc nhóm Khác','water','#1e88e5',3,false,true,3,now(),false);");
            migrationBuilder.Sql(@"INSERT INTO ""IncidentTypeUnitRules"" (""Id"",""IncidentTypeId"",""UnitTypeCode"",""MinSeverity"",""Quantity"",""Priority"",""CreatedAt"",""IsDeleted"") VALUES
('8f1d0b7a-0003-4c2e-9a10-000000000001','8f1d0b7a-0002-4c2e-9a10-000000000001','FireTruck',1,2,1,now(),false),
('8f1d0b7a-0003-4c2e-9a10-000000000002','8f1d0b7a-0002-4c2e-9a10-000000000001','LadderTruck',1,1,2,now(),false),
('8f1d0b7a-0003-4c2e-9a10-000000000003','8f1d0b7a-0002-4c2e-9a10-000000000001','HeavyRescueVehicle',1,1,3,now(),false),
('8f1d0b7a-0003-4c2e-9a10-000000000004','8f1d0b7a-0002-4c2e-9a10-000000000001','Ambulance',3,1,4,now(),false),
('8f1d0b7a-0003-4c2e-9a10-000000000005','8f1d0b7a-0002-4c2e-9a10-000000000002','Ambulance',1,2,1,now(),false),
('8f1d0b7a-0003-4c2e-9a10-000000000006','8f1d0b7a-0002-4c2e-9a10-000000000002','HeavyRescueVehicle',1,1,2,now(),false),
('8f1d0b7a-0003-4c2e-9a10-000000000007','8f1d0b7a-0002-4c2e-9a10-000000000002','FireTruck',3,1,3,now(),false),
('8f1d0b7a-0003-4c2e-9a10-000000000008','8f1d0b7a-0002-4c2e-9a10-000000000004','RescueBoat',1,1,1,now(),false),
('8f1d0b7a-0003-4c2e-9a10-000000000009','8f1d0b7a-0002-4c2e-9a10-000000000004','Ambulance',2,1,2,now(),false);");

            // Sự cố cũ: loại chi tiết = nhóm đã biết (EventType / người báo chọn)
            migrationBuilder.Sql(@"UPDATE ""Incidents"" SET ""IncidentTypeCode"" = CASE ""EventType"" WHEN 1 THEN 'FireExplosion' WHEN 2 THEN 'TrafficAccident' ELSE NULL END;");
            migrationBuilder.Sql(@"UPDATE ""Incidents"" SET ""ReportedIncidentTypeCode"" = CASE ""ReportedEventType"" WHEN 1 THEN 'FireExplosion' WHEN 2 THEN 'TrafficAccident' ELSE NULL END;");

            migrationBuilder.CreateIndex(
                name: "IX_RescueUnits_UnitType",
                table: "RescueUnits",
                column: "UnitType");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_IncidentTypeCode",
                table: "Incidents",
                column: "IncidentTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ReportedIncidentTypeCode",
                table: "Incidents",
                column: "ReportedIncidentTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_FieldRequests_RequestedUnitType",
                table: "FieldRequests",
                column: "RequestedUnitType");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentTypeUnitRules_UnitTypeCode",
                table: "IncidentTypeUnitRules",
                column: "UnitTypeCode");

            migrationBuilder.CreateIndex(
                name: "UQ_IncidentTypeUnitRules_Type_Unit_Severity",
                table: "IncidentTypeUnitRules",
                columns: new[] { "IncidentTypeId", "UnitTypeCode", "MinSeverity" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FieldRequests_RescueUnitTypes_RequestedUnitType",
                table: "FieldRequests",
                column: "RequestedUnitType",
                principalTable: "RescueUnitTypes",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Incidents_IncidentTypes_IncidentTypeCode",
                table: "Incidents",
                column: "IncidentTypeCode",
                principalTable: "IncidentTypes",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Incidents_IncidentTypes_ReportedIncidentTypeCode",
                table: "Incidents",
                column: "ReportedIncidentTypeCode",
                principalTable: "IncidentTypes",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RescueUnits_RescueUnitTypes_UnitType",
                table: "RescueUnits",
                column: "UnitType",
                principalTable: "RescueUnitTypes",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FieldRequests_RescueUnitTypes_RequestedUnitType",
                table: "FieldRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Incidents_IncidentTypes_IncidentTypeCode",
                table: "Incidents");

            migrationBuilder.DropForeignKey(
                name: "FK_Incidents_IncidentTypes_ReportedIncidentTypeCode",
                table: "Incidents");

            migrationBuilder.DropForeignKey(
                name: "FK_RescueUnits_RescueUnitTypes_UnitType",
                table: "RescueUnits");

            migrationBuilder.DropTable(
                name: "IncidentTypeUnitRules");

            migrationBuilder.DropTable(
                name: "IncidentTypes");

            migrationBuilder.DropTable(
                name: "RescueUnitTypes");

            migrationBuilder.DropIndex(
                name: "IX_RescueUnits_UnitType",
                table: "RescueUnits");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_IncidentTypeCode",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_ReportedIncidentTypeCode",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_FieldRequests_RequestedUnitType",
                table: "FieldRequests");

            migrationBuilder.DropColumn(
                name: "AutoDispatchNote",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "AutoDispatchState",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "IncidentTypeCode",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ReportedIncidentTypeCode",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "SuspectedDuplicateOfIncidentId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ConfidenceSource",
                table: "AiClassifications");

            migrationBuilder.Sql(@"ALTER TABLE ""RescueUnits"" ALTER COLUMN ""UnitType"" TYPE integer USING (CASE ""UnitType"" WHEN 'Ambulance' THEN 1 WHEN 'FireTruck' THEN 2 WHEN 'LadderTruck' THEN 3 WHEN 'RescueBoat' THEN 4 WHEN 'HeavyRescueVehicle' THEN 5 WHEN 'PolicePatrol' THEN 6 ELSE 1 END);");

            migrationBuilder.Sql(@"ALTER TABLE ""FieldRequests"" ALTER COLUMN ""RequestedUnitType"" TYPE integer USING (CASE WHEN ""RequestedUnitType"" IS NULL THEN NULL ELSE CASE ""RequestedUnitType"" WHEN 'Ambulance' THEN 1 WHEN 'FireTruck' THEN 2 WHEN 'LadderTruck' THEN 3 WHEN 'RescueBoat' THEN 4 WHEN 'HeavyRescueVehicle' THEN 5 WHEN 'PolicePatrol' THEN 6 ELSE 1 END END);");
        }
    }
}
