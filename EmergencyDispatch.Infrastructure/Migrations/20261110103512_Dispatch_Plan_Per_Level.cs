using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyDispatch.Infrastructure.Migrations
{
    /// <summary>
    /// Kế hoạch điều xe theo đúng từng mức độ (Admin cấu hình "Level 1 điều xe gì, bao nhiêu xe")
    /// thay cho quy tắc "từ mức X trở lên". Tổng số xe của một mức = số xe chuẩn BR-14.
    /// </summary>
    public partial class Dispatch_Plan_Per_Level : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MinSeverity",
                table: "IncidentTypeUnitRules",
                newName: "Severity");

            // (a) Loại có quy tắc seed mà Admin chưa sửa (chỉ có các dòng seed 8f1d0b7a-0003-...): bỏ để thay kế hoạch mặc định theo mức
            migrationBuilder.Sql(@"
DELETE FROM ""IncidentTypeUnitRules"" r
WHERE r.""Id""::text LIKE '8f1d0b7a-0003-%'
  AND NOT EXISTS (SELECT 1 FROM ""IncidentTypeUnitRules"" x
                  WHERE x.""IncidentTypeId"" = r.""IncidentTypeId"" AND x.""Id""::text NOT LIKE '8f1d0b7a-0003-%');");

            // (b) Quy tắc Admin tự tạo theo kiểu ""từ mức X trở lên"": nhân ra các mức cao hơn để giữ nguyên ý nghĩa
            migrationBuilder.Sql(@"
INSERT INTO ""IncidentTypeUnitRules"" (""Id"",""IncidentTypeId"",""UnitTypeCode"",""Severity"",""Quantity"",""Priority"",""CreatedAt"",""IsDeleted"")
SELECT gen_random_uuid(), r.""IncidentTypeId"", r.""UnitTypeCode"", lv, r.""Quantity"", r.""Priority"", now(), false
FROM ""IncidentTypeUnitRules"" r
CROSS JOIN generate_series(2, 3) AS lv
WHERE lv > r.""Severity""
  AND NOT EXISTS (SELECT 1 FROM ""IncidentTypeUnitRules"" x
                  WHERE x.""IncidentTypeId"" = r.""IncidentTypeId"" AND x.""UnitTypeCode"" = r.""UnitTypeCode"" AND x.""Severity"" = lv);");

            // (c) Kế hoạch mặc định theo mức cho loại chưa có kế hoạch (tổng mỗi mức ≤ 5 xe)
            migrationBuilder.Sql(@"
INSERT INTO ""IncidentTypeUnitRules"" (""Id"",""IncidentTypeId"",""UnitTypeCode"",""Severity"",""Quantity"",""Priority"",""CreatedAt"",""IsDeleted"")
SELECT v.id::uuid, v.type_id::uuid, v.unit, v.lv, v.qty, v.prio, now(), false
FROM (VALUES
  -- Cháy nổ: L1 1 xe chữa cháy; L2 2 xe chữa cháy; L3 2 chữa cháy + 1 xe thang + 1 cứu hộ hạng nặng + 1 cấp cứu
  ('8f1d0b7a-0004-4c2e-9a10-000000000001','8f1d0b7a-0002-4c2e-9a10-000000000001','FireTruck',1,1,1),
  ('8f1d0b7a-0004-4c2e-9a10-000000000002','8f1d0b7a-0002-4c2e-9a10-000000000001','FireTruck',2,2,1),
  ('8f1d0b7a-0004-4c2e-9a10-000000000003','8f1d0b7a-0002-4c2e-9a10-000000000001','FireTruck',3,2,1),
  ('8f1d0b7a-0004-4c2e-9a10-000000000004','8f1d0b7a-0002-4c2e-9a10-000000000001','LadderTruck',3,1,2),
  ('8f1d0b7a-0004-4c2e-9a10-000000000005','8f1d0b7a-0002-4c2e-9a10-000000000001','HeavyRescueVehicle',3,1,3),
  ('8f1d0b7a-0004-4c2e-9a10-000000000006','8f1d0b7a-0002-4c2e-9a10-000000000001','Ambulance',3,1,4),
  -- Tai nạn: L1 1 cấp cứu; L2 1 cấp cứu + 1 cứu hộ hạng nặng; L3 2 cấp cứu + 1 cứu hộ hạng nặng + 1 chữa cháy
  ('8f1d0b7a-0004-4c2e-9a10-000000000011','8f1d0b7a-0002-4c2e-9a10-000000000002','Ambulance',1,1,1),
  ('8f1d0b7a-0004-4c2e-9a10-000000000012','8f1d0b7a-0002-4c2e-9a10-000000000002','Ambulance',2,1,1),
  ('8f1d0b7a-0004-4c2e-9a10-000000000013','8f1d0b7a-0002-4c2e-9a10-000000000002','HeavyRescueVehicle',2,1,2),
  ('8f1d0b7a-0004-4c2e-9a10-000000000014','8f1d0b7a-0002-4c2e-9a10-000000000002','Ambulance',3,2,1),
  ('8f1d0b7a-0004-4c2e-9a10-000000000015','8f1d0b7a-0002-4c2e-9a10-000000000002','HeavyRescueVehicle',3,1,2),
  ('8f1d0b7a-0004-4c2e-9a10-000000000016','8f1d0b7a-0002-4c2e-9a10-000000000002','FireTruck',3,1,3),
  -- Đuối nước / ngập: L1 1 xuồng; L2 1 xuồng + 1 cấp cứu; L3 2 xuồng + 1 cấp cứu
  ('8f1d0b7a-0004-4c2e-9a10-000000000021','8f1d0b7a-0002-4c2e-9a10-000000000004','RescueBoat',1,1,1),
  ('8f1d0b7a-0004-4c2e-9a10-000000000022','8f1d0b7a-0002-4c2e-9a10-000000000004','RescueBoat',2,1,1),
  ('8f1d0b7a-0004-4c2e-9a10-000000000023','8f1d0b7a-0002-4c2e-9a10-000000000004','Ambulance',2,1,2),
  ('8f1d0b7a-0004-4c2e-9a10-000000000024','8f1d0b7a-0002-4c2e-9a10-000000000004','RescueBoat',3,2,1),
  ('8f1d0b7a-0004-4c2e-9a10-000000000025','8f1d0b7a-0002-4c2e-9a10-000000000004','Ambulance',3,1,2)
) AS v(id, type_id, unit, lv, qty, prio)
WHERE EXISTS (SELECT 1 FROM ""IncidentTypes"" t WHERE t.""Id"" = v.type_id::uuid)
  AND EXISTS (SELECT 1 FROM ""RescueUnitTypes"" u WHERE u.""Code"" = v.unit)
  AND NOT EXISTS (SELECT 1 FROM ""IncidentTypeUnitRules"" x WHERE x.""IncidentTypeId"" = v.type_id::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Về kiểu ""từ mức X trở lên"": giữ dòng ở mức thấp nhất của mỗi (loại sự cố, loại xe)
            migrationBuilder.Sql(@"
DELETE FROM ""IncidentTypeUnitRules"" r
WHERE EXISTS (SELECT 1 FROM ""IncidentTypeUnitRules"" x
              WHERE x.""IncidentTypeId"" = r.""IncidentTypeId"" AND x.""UnitTypeCode"" = r.""UnitTypeCode"" AND x.""Severity"" < r.""Severity"");");

            migrationBuilder.RenameColumn(
                name: "Severity",
                table: "IncidentTypeUnitRules",
                newName: "MinSeverity");
        }
    }
}
