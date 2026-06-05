using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Api.Services.Logging;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ApplicationLogServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ISqlSugarClient _db;

    public ApplicationLogServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"application-log-{Guid.NewGuid():N}.db");
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"DataSource={_dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        CreateApplicationLogTable();
    }

    [Fact]
    public async Task AuthenticateProjectAsync_Key正确且项目启用_返回项目配置()
    {
        var service = CreateService(
            new ApplicationLoggingProjectOptions
            {
                ProjectCode = "hbweb_rv",
                DisplayName = "Web 后台",
                ApiKeyHash = Sha256("web-secret"),
                Enabled = true,
            }
        );

        var project = await service.AuthenticateProjectAsync("hbweb_rv", "web-secret");

        Assert.NotNull(project);
        Assert.Equal("hbweb_rv", project.ProjectCode);
        Assert.Equal("Web 后台", project.DisplayName);
    }

    [Theory]
    [InlineData("", "web-secret")]
    [InlineData("hbweb_rv", "")]
    [InlineData("missing", "web-secret")]
    [InlineData("hbweb_rv", "wrong-secret")]
    public async Task AuthenticateProjectAsync_Key缺失或错误_返回空(string projectCode, string apiKey)
    {
        var service = CreateService(
            new ApplicationLoggingProjectOptions
            {
                ProjectCode = "hbweb_rv",
                ApiKeyHash = Sha256("web-secret"),
                Enabled = true,
            }
        );

        var project = await service.AuthenticateProjectAsync(projectCode, apiKey);

        Assert.Null(project);
    }

    [Fact]
    public async Task IngestAsync_批量写入日志_保存项目和异常分析字段()
    {
        var service = CreateService(
            new ApplicationLoggingProjectOptions
            {
                ProjectCode = "HbwebExpo",
                DisplayName = "移动端",
                ApiKeyHash = Sha256("mobile-secret"),
                Enabled = true,
            }
        );
        var request = new ApplicationLogIngestRequestDto
        {
            Logs =
            [
                new ApplicationLogIngestItemDto
                {
                    Level = "Error",
                    Message = "Bind failed",
                    TimestampUtc = new DateTime(2026, 6, 5, 1, 2, 3, DateTimeKind.Utc),
                    ProjectCode = "HbwebExpo",
                    Environment = "preview",
                    SourceType = "Mobile",
                    ServiceName = "PDA",
                    TraceId = "trace-1",
                    RequestPath = "/api/react/warehouse-products/mobile/HB001",
                    RequestMethod = "PATCH",
                    StatusCode = 500,
                    UserId = "u-1",
                    UserName = "sean",
                    ExceptionType = "InvalidOperationException",
                    ExceptionMessage = "货位不存在",
                    StackTrace = "stack",
                    Properties = new Dictionary<string, object?> { ["productCode"] = "HB001" },
                },
            ],
        };

        var result = await service.IngestAsync("HbwebExpo", request);

        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(0, result.RejectedCount);
        var saved = await _db.Queryable<ApplicationLog>().SingleAsync();
        Assert.Equal("HbwebExpo", saved.ProjectCode);
        Assert.Equal("移动端", saved.ProjectName);
        Assert.Equal("Error", saved.Level);
        Assert.Equal("Mobile", saved.SourceType);
        Assert.Equal("trace-1", saved.TraceId);
        Assert.Equal("/api/react/warehouse-products/mobile/HB001", saved.RequestPath);
        Assert.Equal("InvalidOperationException", saved.ExceptionType);
        Assert.Contains("productCode", saved.PropertiesJson);
    }

    [Fact]
    public async Task IngestAsync_超过批量上限_拒绝写入()
    {
        var service = CreateService();
        var request = new ApplicationLogIngestRequestDto
        {
            Logs = Enumerable
                .Range(0, 201)
                .Select(index => new ApplicationLogIngestItemDto
                {
                    Level = "Error",
                    Message = $"错误 {index}",
                    TimestampUtc = DateTime.UtcNow,
                    ProjectCode = "HBBBackend",
                    Environment = "Development",
                    SourceType = "Backend",
                })
                .ToList(),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IngestAsync("HBBBackend", request)
        );
    }

    [Fact]
    public async Task IngestAsync_Payload项目与鉴权项目不一致_按鉴权项目入库()
    {
        var service = CreateService(
            new ApplicationLoggingProjectOptions
            {
                ProjectCode = "hbweb_rv",
                DisplayName = "Web 后台",
                ApiKeyHash = Sha256("web-secret"),
                Enabled = true,
            }
        );

        var result = await service.IngestAsync(
            "hbweb_rv",
            new ApplicationLogIngestRequestDto
            {
                Logs =
                [
                    new ApplicationLogIngestItemDto
                    {
                        Level = "Error",
                        Message = "冒用移动端项目码",
                        TimestampUtc = DateTime.UtcNow,
                        ProjectCode = "HbwebExpo",
                        Environment = "test",
                        SourceType = "Web",
                    },
                ],
            }
        );

        Assert.Equal(1, result.AcceptedCount);
        var saved = await _db.Queryable<ApplicationLog>().SingleAsync();
        Assert.Equal("hbweb_rv", saved.ProjectCode);
        Assert.Equal("Web 后台", saved.ProjectName);
    }

    [Fact]
    public async Task IngestAsync_SourceType不在白名单_拒绝写入()
    {
        var service = CreateService();

        var result = await service.IngestAsync(
            "HBBBackend",
            new ApplicationLogIngestRequestDto
            {
                Logs =
                [
                    new ApplicationLogIngestItemDto
                    {
                        Level = "Error",
                        Message = "细分来源不应写入 sourceType",
                        TimestampUtc = DateTime.UtcNow,
                        ProjectCode = "HBBBackend",
                        Environment = "test",
                        SourceType = "backend.worker",
                    },
                ],
            }
        );

        Assert.Equal(0, result.AcceptedCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.Equal(0, await _db.Queryable<ApplicationLog>().CountAsync());
    }

    [Fact]
    public async Task QueryAsync_按项目等级路径关键词筛选_返回匹配日志()
    {
        await InsertLogAsync("HBBBackend", "Error", "/api/orders", "订单同步失败", "trace-1");
        await InsertLogAsync("HbwebExpo", "Error", "/api/orders", "移动端错误", "trace-2");
        await InsertLogAsync("HBBBackend", "Warning", "/api/products", "商品警告", "trace-3");

        var service = CreateService();
        var result = await service.QueryAsync(
            new ApplicationLogQueryDto
            {
                ProjectCode = "HBBBackend",
                Level = "Error",
                RequestPath = "orders",
                Keyword = "同步",
                PageNumber = 1,
                PageSize = 20,
            }
        );

        Assert.Equal(1, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("trace-1", item.TraceId);
        Assert.Equal("订单同步失败", item.Message);
    }

    [Fact]
    public async Task CleanupExpiredLogsAsync_按项目保留天数删除过期日志()
    {
        var now = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);
        await InsertLogAsync("HBBBackend", "Error", "/api/old", "旧日志", "old", now.AddDays(-31));
        await InsertLogAsync("HBBBackend", "Error", "/api/new", "新日志", "new", now.AddDays(-29));
        await InsertLogAsync("HbwebExpo", "Error", "/api/mobile", "移动端旧日志", "mobile", now.AddDays(-8));

        var service = CreateService(
            new ApplicationLoggingProjectOptions
            {
                ProjectCode = "HBBBackend",
                ApiKeyHash = Sha256("backend-secret"),
                RetentionDays = 30,
                Enabled = true,
            },
            new ApplicationLoggingProjectOptions
            {
                ProjectCode = "HbwebExpo",
                ApiKeyHash = Sha256("mobile-secret"),
                RetentionDays = 7,
                Enabled = true,
            }
        );

        var deleted = await service.CleanupExpiredLogsAsync(now);

        Assert.Equal(2, deleted);
        var remaining = await _db.Queryable<ApplicationLog>().OrderBy(x => x.TraceId).ToListAsync();
        var item = Assert.Single(remaining);
        Assert.Equal("new", item.TraceId);
    }

    private ApplicationLogService CreateService(params ApplicationLoggingProjectOptions[] projects)
    {
        var options = Options.Create(
            new ApplicationLoggingOptions
            {
                DefaultProjectCode = "HBBBackend",
                DefaultRetentionDays = 30,
                MaxBatchSize = 200,
                Projects = projects.Length > 0
                    ? projects.ToList()
                    : new List<ApplicationLoggingProjectOptions>
                    {
                        new()
                        {
                            ProjectCode = "HBBBackend",
                            DisplayName = "后端",
                            ApiKeyHash = Sha256("backend-secret"),
                            Enabled = true,
                            RetentionDays = 30,
                        },
                    },
            }
        );

        return new ApplicationLogService(_db, options, NullLogger<ApplicationLogService>.Instance);
    }

    private async Task InsertLogAsync(
        string projectCode,
        string level,
        string path,
        string message,
        string traceId,
        DateTime? timestampUtc = null
    )
    {
        await _db.Insertable(
                new ApplicationLog
                {
                    ProjectCode = projectCode,
                    ProjectName = projectCode,
                    Environment = "Development",
                    SourceType = "Backend",
                    Level = level,
                    Category = "Test",
                    Message = message,
                    RequestPath = path,
                    TraceId = traceId,
                    TimestampUtc = timestampUtc ?? DateTime.UtcNow,
                }
            )
            .ExecuteCommandAsync();
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private void CreateApplicationLogTable()
    {
        _db.Ado.ExecuteCommand(
            """
            CREATE TABLE ApplicationLog (
                Id TEXT PRIMARY KEY,
                TimestampUtc TEXT NOT NULL,
                ProjectCode TEXT NOT NULL,
                ProjectName TEXT NULL,
                Environment TEXT NOT NULL,
                SourceType TEXT NOT NULL,
                ServiceName TEXT NULL,
                InstanceId TEXT NULL,
                Level TEXT NOT NULL,
                Category TEXT NULL,
                EventId TEXT NULL,
                Message TEXT NOT NULL,
                ExceptionType TEXT NULL,
                ExceptionMessage TEXT NULL,
                StackTrace TEXT NULL,
                RequestPath TEXT NULL,
                RequestMethod TEXT NULL,
                StatusCode INTEGER NULL,
                TraceId TEXT NULL,
                UserId TEXT NULL,
                UserName TEXT NULL,
                ClientIp TEXT NULL,
                PropertiesJson TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CreatedBy TEXT NULL,
                UpdatedAt TEXT NULL,
                UpdatedBy TEXT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            )
            """
        );
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }
}
