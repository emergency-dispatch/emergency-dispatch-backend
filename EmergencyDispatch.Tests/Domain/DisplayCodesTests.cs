using EmergencyDispatch.Domain.Common;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Tests.Domain;

public class DisplayCodesTests
{
    [Fact]
    public void IncidentCode_UsesVietnamDate_AndPadsNumber()
    {
        // 17/11 20:30 UTC = 18/11 03:30 giờ VN
        var code = DisplayCodes.IncidentCode(42, new DateTime(2026, 11, 17, 20, 30, 0, DateTimeKind.Utc));

        Assert.Equal("SC-261118-0042", code);
        Assert.Equal("SC-261117-12345", DisplayCodes.IncidentCode(12345, new DateTime(2026, 11, 17, 1, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(UserRole.RescueStaff, "NV-0007")]
    [InlineData(UserRole.Operator, "NV-0007")]
    [InlineData(UserRole.Admin, "NV-0007")]
    [InlineData(UserRole.Citizen, null)]
    public void StaffCode_OnlyForNonCitizens(UserRole role, string? expected)
    {
        Assert.Equal(expected, new User { Role = role, StaffNumber = 7 }.StaffCode);
    }

    [Fact]
    public void StaffCode_IsNull_WhenNumberNotAssigned()
    {
        Assert.Null(new User { Role = UserRole.RescueStaff }.StaffCode);
    }
}
