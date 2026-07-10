using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
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
    public void ApplicationLoggingOptions_默认保留天数为7天()
    {
        var options = new ApplicationLoggingOptions();

        Assert.Equal(7, options.DefaultRetentionDays);
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
        Assert.Equal(0, result.DuplicateCount);
        var itemResult = Assert.Single(result.Results);
        Assert.Null(itemResult.ClientEventId);
        Assert.Equal("accepted", itemResult.Status);
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
    public async Task IngestAsync_同一项目重复ClientEventId_仅保存一次并返回逐条状态()
    {
        var service = CreateService();
        var clientEventId = Guid.NewGuid();
        var request = new ApplicationLogIngestRequestDto
        {
            Logs =
            [
                CreateIngestItem("第一次上报", clientEventId),
                CreateIngestItem("重复上报", clientEventId),
            ],
        };

        var result = await service.IngestAsync("HBBBackend", request);

        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(0, result.RejectedCount);
        Assert.Collection(
            result.Results,
            item =>
            {
                Assert.Equal(clientEventId, item.ClientEventId);
                Assert.Equal("accepted", item.Status);
                Assert.Null(item.ErrorCode);
            },
            item =>
            {
                Assert.Equal(clientEventId, item.ClientEventId);
                Assert.Equal("duplicate", item.Status);
                Assert.Null(item.ErrorCode);
            }
        );
        var saved = await _db.Queryable<ApplicationLog>().SingleAsync();
        Assert.Equal(clientEventId, saved.ClientEventId);
        Assert.Equal("第一次上报", saved.Message);
    }

    [Fact]
    public async Task IngestAsync_旧客户端相同EventId仍按原语义逐条写入()
    {
        var service = CreateService();
        var first = CreateIngestItem("旧客户端第一次");
        first.EventId = "legacy-event";
        var second = CreateIngestItem("旧客户端第二次");
        second.EventId = "legacy-event";

        var result = await service.IngestAsync(
            "HBBBackend",
            new ApplicationLogIngestRequestDto { Logs = [first, second] }
        );

        Assert.Equal(2, result.AcceptedCount);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Equal(2, await _db.Queryable<ApplicationLog>().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_并发上报相同ClientEventId_数据库只保存一次()
    {
        var clientEventId = Guid.NewGuid();
        using var secondDb = CreateSqliteClient();
        var firstService = CreateService(_db);
        var secondService = CreateService(secondDb);

        var results = await Task.WhenAll(
            firstService.IngestAsync(
                "HBBBackend",
                new ApplicationLogIngestRequestDto
                {
                    Logs = [CreateIngestItem("并发一", clientEventId)],
                }
            ),
            secondService.IngestAsync(
                "HBBBackend",
                new ApplicationLogIngestRequestDto
                {
                    Logs = [CreateIngestItem("并发二", clientEventId)],
                }
            )
        );

        Assert.Equal(1, results.Sum(result => result.AcceptedCount));
        Assert.Equal(1, results.Sum(result => result.DuplicateCount));
        Assert.Equal(1, await _db.Queryable<ApplicationLog>().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_Wpf字段和可信客户端Ip_入库前完成脱敏()
    {
        var service = CreateService();
        var clientEventId = Guid.NewGuid();
        var result = await service.IngestAsync(
            "HBBBackend",
            new ApplicationLogIngestRequestDto
            {
                Logs =
                [
                    new ApplicationLogIngestItemDto
                    {
                        ClientEventId = clientEventId,
                        Level = "Error",
                        Message = "Authorization: Bearer top-secret-token card 4111111111111111 " +
                            "/api/pay?customer=alice&token=query-secret " +
                            "voucher_code=full-voucher employee-barcode=staff-secret",
                        TimestampUtc = DateTime.UtcNow,
                        ProjectCode = "HBBBackend",
                        Environment = "Production",
                        SourceType = "POS",
                        StoreCode = "S001",
                        DeviceCode = "POS-01",
                        AppVersion = "2.5.0",
                        InstanceId = "instance-1",
                        EventId = "event-7",
                        ClientIp = "198.51.100.99",
                        RequestPath = "/api/orders?authorizationCode=secret-code",
                        Properties = new Dictionary<string, object?>
                        {
                            ["productCode"] = "HB001",
                            ["Authorization"] = "Bearer property-secret",
                            ["customerEmail"] = "private@example.test",
                            ["requestBody"] = "{\"voucherCode\":\"full-voucher\"}",
                            ["nested"] = new Dictionary<string, object?>
                            {
                                ["password"] = "password-secret",
                            },
                        },
                    },
                ],
            },
            "203.0.113.10"
        );

        Assert.Equal(1, result.AcceptedCount);
        var saved = await _db.Queryable<ApplicationLog>().SingleAsync();
        Assert.Equal(clientEventId, saved.ClientEventId);
        Assert.Equal("S001", saved.StoreCode);
        Assert.Equal("POS-01", saved.DeviceCode);
        Assert.Equal("2.5.0", saved.AppVersion);
        Assert.Equal("203.0.113.10", saved.ClientIp);
        Assert.Equal("/api/orders", saved.RequestPath);
        Assert.DoesNotContain("top-secret-token", saved.Message);
        Assert.DoesNotContain("4111111111111111", saved.Message);
        Assert.DoesNotContain("?customer=", saved.Message);
        Assert.DoesNotContain("query-secret", saved.Message);
        Assert.DoesNotContain("full-voucher", saved.Message);
        Assert.DoesNotContain("staff-secret", saved.Message);
        Assert.DoesNotContain("property-secret", saved.PropertiesJson);
        Assert.DoesNotContain("password-secret", saved.PropertiesJson);
        Assert.DoesNotContain("private@example.test", saved.PropertiesJson);
        Assert.DoesNotContain("full-voucher", saved.PropertiesJson);
        Assert.Contains("HB001", saved.PropertiesJson);
        Assert.Contains("[REDACTED]", saved.PropertiesJson);
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
        Assert.Equal(0, result.DuplicateCount);
        var itemResult = Assert.Single(result.Results);
        Assert.Equal("rejected", itemResult.Status);
        Assert.Equal("INVALID_LOG_ITEM", itemResult.ErrorCode);
        Assert.Equal(0, await _db.Queryable<ApplicationLog>().CountAsync());
    }

    [Fact]
    public async Task IngestAsync_复杂属性值不能直接Json序列化_安全转成可查询文本()
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
                        Message = "包含复杂属性的日志",
                        TimestampUtc = DateTime.UtcNow,
                        ProjectCode = "HBBBackend",
                        Environment = "test",
                        SourceType = "Backend",
                        Properties = new Dictionary<string, object?>
                        {
                            ["remoteIp"] = IPAddress.Loopback,
                            ["endpoint"] = new IPEndPoint(IPAddress.Loopback, 5002),
                            ["tags"] = new[] { "backend", "logging" },
                        },
                    },
                ],
            }
        );

        Assert.Equal(1, result.AcceptedCount);
        var saved = await _db.Queryable<ApplicationLog>().SingleAsync();
        Assert.Contains("remoteIp", saved.PropertiesJson);
        Assert.Contains("127.0.0.1", saved.PropertiesJson);
        Assert.Contains("endpoint", saved.PropertiesJson);
        Assert.Contains("backend", saved.PropertiesJson);
    }

    [Fact]
    public async Task IngestAsync_非有限浮点属性值_安全转成可查询文本()
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
                        Message = "包含非有限浮点属性的日志",
                        TimestampUtc = DateTime.UtcNow,
                        ProjectCode = "HBBBackend",
                        Environment = "test",
                        SourceType = "Backend",
                        Properties = new Dictionary<string, object?>
                        {
                            ["ratio"] = double.NaN,
                            ["positiveInfinity"] = double.PositiveInfinity,
                            ["negativeInfinity"] = float.NegativeInfinity,
                        },
                    },
                ],
            }
        );

        Assert.Equal(1, result.AcceptedCount);
        var saved = await _db.Queryable<ApplicationLog>().SingleAsync();
        Assert.Contains("\"ratio\":\"NaN\"", saved.PropertiesJson);
        Assert.Contains("\"positiveInfinity\":\"Infinity\"", saved.PropertiesJson);
        Assert.Contains("\"negativeInfinity\":\"-Infinity\"", saved.PropertiesJson);
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
        var item = Assert.Single(result.Items!);
        Assert.Equal("trace-1", item.TraceId);
        Assert.Equal("订单同步失败", item.Message);
    }

    [Fact]
    public async Task QueryAsync_按多个项目筛选_返回所选项目日志()
    {
        await InsertLogAsync("HBBBackend", "Error", "/api/orders", "后端错误", "trace-backend");
        await InsertLogAsync("HbwebExpo", "Error", "/api/mobile", "移动端错误", "trace-mobile");
        await InsertLogAsync("hbweb_rv", "Error", "/api/web", "前端错误", "trace-web");

        var service = CreateService();
        var result = await service.QueryAsync(
            new ApplicationLogQueryDto
            {
                ProjectCodes = ["HBBBackend", "HbwebExpo"],
                PageNumber = 1,
                PageSize = 20,
                SortBy = "ProjectCode",
                SortDirection = "asc",
            }
        );

        Assert.Equal(2, result.Total);
        Assert.DoesNotContain(result.Items!, item => item.ProjectCode == "hbweb_rv");
        Assert.Contains(result.Items!, item => item.ProjectCode == "HBBBackend");
        Assert.Contains(result.Items!, item => item.ProjectCode == "HbwebExpo");
    }

    [Fact]
    public async Task QueryAsync_按Wpf维度筛选_返回事件标识实例和服务端接收时间()
    {
        var receivedAtUtc = new DateTime(2026, 7, 10, 1, 2, 3, DateTimeKind.Utc);
        await _db.Insertable(
                new ApplicationLog
                {
                    ClientEventId = Guid.NewGuid(),
                    ProjectCode = "hbpos_win",
                    ProjectName = "WPF POS",
                    Environment = "Production",
                    SourceType = "POS",
                    StoreCode = "S001",
                    DeviceCode = "POS-01",
                    AppVersion = "2.5.0",
                    InstanceId = "instance-1",
                    EventId = "event-1",
                    Level = "Error",
                    Category = "Payment",
                    Message = "支付失败",
                    TimestampUtc = receivedAtUtc.AddMinutes(-5),
                    CreatedAt = receivedAtUtc,
                }
            )
            .ExecuteCommandAsync();
        await _db.Insertable(
                new ApplicationLog
                {
                    ProjectCode = "hbpos_win",
                    ProjectName = "WPF POS",
                    Environment = "Production",
                    SourceType = "POS",
                    StoreCode = "S002",
                    DeviceCode = "POS-02",
                    AppVersion = "2.4.0",
                    InstanceId = "instance-2",
                    EventId = "event-2",
                    Level = "Warning",
                    Message = "其他终端",
                    TimestampUtc = receivedAtUtc,
                    CreatedAt = receivedAtUtc,
                }
            )
            .ExecuteCommandAsync();

        var result = await CreateService().QueryAsync(
            new ApplicationLogQueryDto
            {
                StoreCode = "S001",
                DeviceCode = "POS-01",
                AppVersion = "2.5.0",
                InstanceId = "instance-1",
                EventId = "event-1",
            }
        );

        var item = Assert.Single(result.Items!);
        Assert.Equal("S001", item.StoreCode);
        Assert.Equal("POS-01", item.DeviceCode);
        Assert.Equal("2.5.0", item.AppVersion);
        Assert.Equal("instance-1", item.InstanceId);
        Assert.Equal("event-1", item.EventId);
        Assert.NotNull(item.ClientEventId);
        Assert.Equal(receivedAtUtc, item.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, item.TimestampUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, item.CreatedAtUtc.Kind);
    }

    [Fact]
    public async Task GetSummaryAsync_按Brisbane本地日窗口统计_只包含当天UTC范围内的日志()
    {
        await InsertLogAsync(
            "HBBBackend",
            "Error",
            "/api/before",
            "窗口开始前",
            "trace-before",
            new DateTime(2026, 6, 5, 13, 59, 59, DateTimeKind.Utc)
        );
        await InsertLogAsync(
            "HBBBackend",
            "Error",
            "/api/start",
            "窗口开始",
            "trace-start",
            new DateTime(2026, 6, 5, 14, 0, 0, DateTimeKind.Utc)
        );
        await InsertLogAsync(
            "HBBBackend",
            "Critical",
            "/api/inside",
            "窗口内",
            "trace-inside",
            new DateTime(2026, 6, 6, 13, 59, 59, DateTimeKind.Utc)
        );
        await InsertLogAsync(
            "HBBBackend",
            "Error",
            "/api/end",
            "下一本地日开始",
            "trace-end",
            new DateTime(2026, 6, 6, 14, 0, 0, DateTimeKind.Utc)
        );

        var service = CreateService();
        var result = await service.GetSummaryAsync(
            new ApplicationLogQueryDto
            {
                StartUtc = new DateTime(2026, 6, 5, 14, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 6, 6, 14, 0, 0, DateTimeKind.Utc),
            }
        );

        Assert.Equal(2, result.Total);
        Assert.Contains(result.ByRequestPath, item => item.Name == "/api/start" && item.Count == 1);
        Assert.Contains(result.ByRequestPath, item => item.Name == "/api/inside" && item.Count == 1);
        Assert.DoesNotContain(result.ByRequestPath, item => item.Name == "/api/before");
        Assert.DoesNotContain(result.ByRequestPath, item => item.Name == "/api/end");
    }

    [Theory]
    [InlineData("Error", "trace-error")]
    [InlineData("Critical", "trace-critical")]
    public async Task GetSummaryAsync_按等级筛选_返回对应等级统计(
        string level,
        string expectedTraceId
    )
    {
        await InsertLogAsync("HBBBackend", "Error", "/api/error", "错误日志", "trace-error");
        await InsertLogAsync("HBBBackend", "Critical", "/api/critical", "严重日志", "trace-critical");
        await InsertLogAsync("HBBBackend", "Warning", "/api/warning", "警告日志", "trace-warning");

        var service = CreateService();
        var summary = await service.GetSummaryAsync(
            new ApplicationLogQueryDto
            {
                Level = level,
            }
        );
        var query = await service.QueryAsync(
            new ApplicationLogQueryDto
            {
                Level = level,
                PageNumber = 1,
                PageSize = 10,
            }
        );

        Assert.Equal(1, summary.Total);
        Assert.Single(summary.ByLevel);
        Assert.Equal(level, summary.ByLevel[0].Name);
        Assert.Single(query.Items!);
        Assert.Equal(expectedTraceId, query.Items![0].TraceId);
    }

    [Fact]
    public async Task CleanupExpiredLogsAsync_项目保留7天时删除8天前并保留7天内日志()
    {
        var now = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);
        await InsertLogAsync("HBBBackend", "Error", "/api/expired", "8天前日志", "expired", now.AddDays(-8));
        await InsertLogAsync("HBBBackend", "Error", "/api/kept", "7天内日志", "kept", now.AddDays(-6));

        var service = CreateService(
            new ApplicationLoggingProjectOptions
            {
                ProjectCode = "HBBBackend",
                ApiKeyHash = Sha256("backend-secret"),
                RetentionDays = 7,
                Enabled = true,
            }
        );

        var deleted = await service.CleanupExpiredLogsAsync(now);

        Assert.Equal(1, deleted);
        var remaining = await _db.Queryable<ApplicationLog>().OrderBy(x => x.TraceId).ToListAsync();
        var item = Assert.Single(remaining);
        Assert.Equal("kept", item.TraceId);
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

    [Fact]
    public async Task CleanupExpiredLogsAsync_客户端时间很旧但服务端刚接收_不删除日志()
    {
        var now = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        await InsertLogAsync(
            "hbpos_win",
            "Error",
            "/api/wpf",
            "离线后补传",
            "wpf-delayed",
            now.AddDays(-60),
            now.AddDays(-1)
        );
        var service = CreateService(
            new ApplicationLoggingProjectOptions
            {
                ProjectCode = "hbpos_win",
                ApiKeyHash = Sha256("wpf-secret"),
                RetentionDays = 30,
                Enabled = true,
            }
        );

        var deleted = await service.CleanupExpiredLogsAsync(now);

        Assert.Equal(0, deleted);
        Assert.Equal(1, await _db.Queryable<ApplicationLog>().CountAsync());
    }

    [Fact]
    public async Task CleanupExpiredLogsAsync_恰好30天边界保留_更早一刻删除()
    {
        var now = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        await InsertLogAsync(
            "hbpos_win",
            "Information",
            "/api/wpf",
            "边界日志",
            "boundary",
            now.AddDays(-30),
            now.AddDays(-30));
        await InsertLogAsync(
            "hbpos_win",
            "Information",
            "/api/wpf",
            "已过期日志",
            "expired",
            now.AddDays(-30).AddTicks(-1),
            now.AddDays(-30).AddTicks(-1));
        var service = CreateService(
            new ApplicationLoggingProjectOptions
            {
                ProjectCode = "hbpos_win",
                ApiKeyHash = Sha256("wpf-secret"),
                RetentionDays = 30,
                Enabled = true,
            }
        );

        var deleted = await service.CleanupExpiredLogsAsync(now);

        Assert.Equal(1, deleted);
        var remaining = Assert.Single(await _db.Queryable<ApplicationLog>().ToListAsync());
        Assert.Equal("boundary", remaining.TraceId);
    }

    private ApplicationLogService CreateService(params ApplicationLoggingProjectOptions[] projects)
    {
        return CreateService(_db, projects);
    }

    private static ApplicationLogService CreateService(
        ISqlSugarClient db,
        params ApplicationLoggingProjectOptions[] projects
    )
    {
        var options = Options.Create(
            new ApplicationLoggingOptions
            {
                DefaultProjectCode = "HBBBackend",
                DefaultRetentionDays = 7,
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
                            RetentionDays = 7,
                        },
                    },
            }
        );

        return new ApplicationLogService(db, options, NullLogger<ApplicationLogService>.Instance);
    }

    private async Task InsertLogAsync(
        string projectCode,
        string level,
        string path,
        string message,
        string traceId,
        DateTime? timestampUtc = null,
        DateTime? createdAtUtc = null
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
                    CreatedAt = createdAtUtc ?? timestampUtc ?? DateTime.UtcNow,
                }
            )
            .ExecuteCommandAsync();
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static ApplicationLogIngestItemDto CreateIngestItem(
        string message,
        Guid? clientEventId = null
    )
    {
        return new ApplicationLogIngestItemDto
        {
            ClientEventId = clientEventId,
            Level = "Error",
            Message = message,
            TimestampUtc = DateTime.UtcNow,
            ProjectCode = "HBBBackend",
            Environment = "Development",
            SourceType = "Backend",
        };
    }

    private ISqlSugarClient CreateSqliteClient()
    {
        var db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = $"DataSource={_dbPath}",
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        db.Ado.ExecuteCommand("PRAGMA busy_timeout=5000;");
        return db;
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
                ClientEventId TEXT NULL,
                StoreCode TEXT NULL,
                DeviceCode TEXT NULL,
                AppVersion TEXT NULL,
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
        _db.Ado.ExecuteCommand(
            "CREATE UNIQUE INDEX IX_ApplicationLog_ProjectCode_ClientEventId ON ApplicationLog(ProjectCode, ClientEventId) WHERE ClientEventId IS NOT NULL"
        );
        _db.Ado.ExecuteCommand("PRAGMA busy_timeout=5000;");
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }
}
