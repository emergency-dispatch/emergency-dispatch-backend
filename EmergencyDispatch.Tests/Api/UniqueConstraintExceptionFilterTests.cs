using System.Globalization;
using EmergencyDispatch.API.Filters;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace EmergencyDispatch.Tests.Api;

/// <summary>Unique index violations become 409 instead of 500; unique indexes ignore soft-deleted rows.</summary>
public class UniqueConstraintExceptionFilterTests
{
    private static PostgresException Pg(string sqlState, string? constraint)
        => new("duplicate key value violates unique constraint", "ERROR", "ERROR", sqlState, constraintName: constraint);

    private static ExceptionContext Context(Exception ex)
        => new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>()) { Exception = ex };

    private static readonly UniqueConstraintExceptionFilter Filter = new(NullLogger<UniqueConstraintExceptionFilter>.Instance);

    [Theory]
    [InlineData("IX_RescueUnits_PlateNumber", "Biển số xe đã tồn tại trong hệ thống.")]
    [InlineData("IX_Users_Email", "Email này đã được sử dụng trong hệ thống.")]
    [InlineData("IX_Something_Else", "Dữ liệu trùng với một bản ghi đã có. Vui lòng kiểm tra lại.")]
    public void UniqueViolation_Returns409WithMessage(string constraint, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = new CultureInfo("vi");
        var ctx = Context(new DbUpdateException("save failed", Pg(UniqueConstraintExceptionFilter.UniqueViolation, constraint)));

        Filter.OnException(ctx);

        Assert.True(ctx.ExceptionHandled);
        var result = Assert.IsType<ConflictObjectResult>(ctx.Result);
        var body = Assert.IsType<ApiResponseDto<object>>(result.Value);
        Assert.Equal(ApiErrorCodes.Conflict, body.ErrorCode);
        Assert.Equal(expected, body.Message);
    }

    [Fact]
    public void UniqueViolation_EnglishRequest_GetsEnglishMessage()
    {
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = new CultureInfo("en");
        var ctx = Context(new DbUpdateException("save failed", Pg(UniqueConstraintExceptionFilter.UniqueViolation, "IX_RescueUnits_PlateNumber")));

        Filter.OnException(ctx);

        Assert.Equal("License plate already exists in the system.", Assert.IsType<ApiResponseDto<object>>(Assert.IsType<ConflictObjectResult>(ctx.Result).Value).Message);
    }

    [Fact]
    public void OtherPostgresError_IsNotHandled()
    {
        var ctx = Context(new DbUpdateException("fk", Pg("23503", "FK_X")));
        Filter.OnException(ctx);
        Assert.False(ctx.ExceptionHandled);
        Assert.Null(ctx.Result);
    }

    [Fact]
    public void NonDatabaseException_IsNotHandled()
    {
        var ctx = Context(new InvalidOperationException("x"));
        Filter.OnException(ctx);
        Assert.False(ctx.ExceptionHandled);
    }

    [Theory]
    [InlineData("Users", "IX_Users_Email")]
    [InlineData("RescueUnits", "IX_RescueUnits_PlateNumber")]
    [InlineData("Stations", "IX_Stations_Name")]
    [InlineData("Stations", "IX_Stations_Code")]
    public void UniqueIndexes_IgnoreSoftDeletedRows(string table, string indexName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=unused").Options;
        using var db = new ApplicationDbContext(options);

        var index = db.Model.GetEntityTypes().Where(t => t.GetTableName() == table)
            .SelectMany(t => t.GetIndexes()).Single(i => i.GetDatabaseName() == indexName);

        Assert.True(index.IsUnique);
        Assert.Equal("\"IsDeleted\" = false", index.GetFilter());
    }
}
