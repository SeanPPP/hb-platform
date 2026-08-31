using BlazorApp.Shared.Models.HBweb;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class AppOtaReleaseSqlParameterTests
{
    [Fact]
    public void PublishedAtUtc_插入SQLServer时必须使用DateTime2参数()
    {
        using var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString =
                "Server=127.0.0.1;Database=hb_platform_sql_generation;"
                + "User Id=test;Password=test;TrustServerCertificate=True;",
            DbType = SqlSugar.DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });

        var release = new AppOtaRelease
        {
            ReleaseBatchId = Guid.NewGuid(),
            AppKey = "mobile",
            Environment = "production",
            ClientChannel = "production",
            ReleaseChannel = "mobile-production-android-release-test",
            EasBranch = "mobile-production-android-release-test",
            ProjectName = "hbweb-expo",
            Platform = "android",
            RuntimeVersion = "1.0.2",
            UpdateGroupId = Guid.NewGuid().ToString(),
            UpdateId = Guid.NewGuid().ToString(),
            PublishedAtUtc = new DateTime(2026, 8, 30, 8, 59, 58, 436, DateTimeKind.Utc)
                .AddTicks(6_667),
            FactFingerprint = new string('a', 64),
            RegistrationSource = "app-ota-release-api",
        };

        var parameters = db.Insertable(release).ToSql().Value;
        var publishedAtParameter = Assert.Single(
            parameters,
            parameter => parameter.ParameterName.Equals(
                $"@{nameof(AppOtaRelease.PublishedAtUtc)}",
                StringComparison.OrdinalIgnoreCase
            )
        );

        Assert.Equal(System.Data.DbType.DateTime2, publishedAtParameter.DbType);
    }
}
