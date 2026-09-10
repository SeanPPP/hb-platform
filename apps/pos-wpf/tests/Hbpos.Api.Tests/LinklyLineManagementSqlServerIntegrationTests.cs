using BlazorApp.Shared.Security;
using Hbpos.Api.Data;
using Hbpos.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hbpos.Api.Tests;

public sealed class LinklyLineSqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "LINKLY_LINE_SQLSERVER_TEST_CONNECTION";

    public LinklyLineSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
            Skip = "未配置隔离 SQL Server，跳过 Linkly 线路真实事务测试。";
    }
}

// 每个用例创建独立数据库；不允许借用生产表，也不依赖用例执行顺序。
public sealed class LinklyLineManagementSqlServerIntegrationTests : IAsyncLifetime
{
    private string? masterConnection;
    private string connection = string.Empty;
    private string database = string.Empty;
    private readonly DateTime version = new(2026, 9, 10, 6, 0, 0, DateTimeKind.Utc);
    private readonly Guid lineA = Guid.Parse("00000001-0000-0000-0000-000000000001");
    private readonly Guid lineB = Guid.Parse("00000002-0000-0000-0000-000000000002");
    private readonly Guid lineC = Guid.Parse("00000003-0000-0000-0000-000000000003");

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(LinklyLineSqlServerFactAttribute.ConnectionVariable);
        if (string.IsNullOrWhiteSpace(configured)) return;
        database = $"HbLinklyLines_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        masterConnection = builder.ConnectionString;
        await ExecuteAtAsync(masterConnection, $"CREATE DATABASE [{database}]");
        builder.InitialCatalog = database;
        connection = builder.ConnectionString;
        await ExecuteAsync(SqlSugarLinklyCloudBackendAsyncSchemaInitializer.EnsureTableSql);
        await ExecuteAsync("""
            CREATE TABLE [dbo].[POSM_设备注册信息表] (
                [ID] int IDENTITY(1,1) NOT NULL,
                [系统设备编号] nvarchar(64) NOT NULL PRIMARY KEY,
                [分店代码] nvarchar(32) NOT NULL,
                [设备类型] nvarchar(32) NOT NULL,
                [设备系统] nvarchar(32) NULL,
                [设备状态] int NOT NULL,
                [是否允许交易] bit NOT NULL);
            INSERT INTO [dbo].[POSM_设备注册信息表]
                ([系统设备编号],[分店代码],[设备类型],[设备系统],[设备状态],[是否允许交易]) VALUES
                (N'POS-A',N'S001',N'POS',N'Windows',1,1),
                (N'POS-B',N'S001',N'POS',N'iOS',1,1),
                (N'POS-C',N'S001',N'POS',N'iOS',1,1),
                (N'POS-DISABLED',N'S001',N'POS',N'iOS',2,1),
                (N'POS-NOT-ALLOWED',N'S001',N'POS',N'iOS',1,0),
                (N'PDA-A',N'S001',N'PDA',N'Android',1,1),
                (N'OTHER-STORE',N'S002',N'POS',N'Windows',1,1);
            INSERT INTO [dbo].[POSM_LinklyCloudConfigurationMode]
                ([Environment],[StoreCode],[Mode]) VALUES (N'Production',N'S001',N'Active');
            """);
        await SeedLineAsync(lineA, 1);
        await SeedLineAsync(lineB, 2);
        await SeedLineAsync(lineC, 3);
    }

    public async Task DisposeAsync()
    {
        if (masterConnection is null) return;
        // 名称只来自本用例生成的 GUID，销毁范围严格限于本用例数据库。
        await ExecuteAtAsync(masterConnection,
            $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];");
    }

    [LinklyLineSqlServerFact]
    public async Task Caller_can_transfer_another_pos_line_and_atomically_replace_target()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        await SeedSelectionAsync("POS-B", lineB, 201);
        await SeedSelectionAsync("POS-C", lineC, 301);
        await AssignAsync("POS-A", lineB, "POS-B", 201, "POS-C", lineC, 301);
        Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
        Assert.Null(await SelectionAsync("POS-B"));
        var target = (await SelectionAsync("POS-C"))!;
        Assert.Equal(lineB, target.TerminalId);
        Assert.True(target.Revision > 301);
        Assert.Null(await OwnerAsync(lineC));
        var refreshed = await Repository().ListAsync("Production", "S001", default);
        Assert.Equal("Healthy", refreshed.Single(item => item.TerminalId == lineA).LastHealthStatus);
        Assert.All(refreshed.Where(item => item.TerminalId != lineA), item =>
        {
            Assert.Null(item.LastHealthStatus);
            Assert.Null(item.LastHealthAt);
            Assert.True(item.UpdatedAt > version);
        });
        await AssertPairingPreservedAsync();
    }

    [LinklyLineSqlServerFact]
    public async Task Local_switch_to_unassigned_line_releases_previous_line()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        await AssignAsync("POS-A", lineB, null, 0, "POS-A", lineA, 101);
        Assert.Equal(lineB, (await SelectionAsync("POS-A"))!.TerminalId);
        Assert.Null(await OwnerAsync(lineA));
        await AssertPairingPreservedAsync();
    }

    [LinklyLineSqlServerFact]
    public async Task Unbind_only_releases_requested_line_and_can_repeat_from_new_snapshot()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        await SeedSelectionAsync("POS-B", lineB, 201);
        await AssignAsync("POS-A", lineB, "POS-B", 201, null, null, 0);
        Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
        Assert.Null(await SelectionAsync("POS-B"));
        var newVersion = (await Repository().GetAsync("Production", "S001", lineB, default))!.UpdatedAt!.Value;
        await AssignAsync("POS-A", lineB, null, 0, null, null, 0, newVersion);
        Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
        await AssertPairingPreservedAsync();
    }

    [LinklyLineSqlServerFact]
    public async Task Stale_source_or_target_version_leaves_all_bindings_unchanged()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        await SeedSelectionAsync("POS-B", lineB, 201);
        await Assert.ThrowsAnyAsync<Exception>(() => AssignAsync("POS-C", lineA, "POS-A", 100, "POS-B", lineB, 201));
        await Assert.ThrowsAnyAsync<Exception>(() => AssignAsync("POS-C", lineA, "POS-A", 101, "POS-B", lineB, 200));
        await Assert.ThrowsAnyAsync<Exception>(() => AssignAsync("POS-C", lineA, "POS-A", 101, "POS-B", lineB, 201, version.AddTicks(-1)));
        Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
        Assert.Equal(101, (await SelectionAsync("POS-A"))!.Revision);
        Assert.Equal(lineB, (await SelectionAsync("POS-B"))!.TerminalId);
        Assert.Equal(201, (await SelectionAsync("POS-B"))!.Revision);
    }

    [LinklyLineSqlServerFact]
    public async Task Target_must_be_available_pos_in_same_store()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        foreach (var target in new[] { "OTHER-STORE", "POS-DISABLED", "POS-NOT-ALLOWED", "PDA-A", "MISSING" })
        {
            await Assert.ThrowsAnyAsync<Exception>(() => AssignAsync("POS-A", lineA, "POS-A", 101, target, null, 0));
            Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
        }
    }

    [LinklyLineSqlServerFact]
    public async Task Historical_owner_stays_visible_but_cannot_be_a_new_target()
    {
        await SeedSelectionAsync("DELETED-POS", lineA, 101);
        await SeedSelectionAsync("POS-DISABLED", lineB, 201);
        await ExecuteAsync("UPDATE [dbo].[POSM_设备注册信息表] SET [设备状态]=0 WHERE [系统设备编号]=N'POS-DISABLED';");
        var devices = await Repository().ListAssignableDevicesAsync("Production", "S001", default);
        foreach (var owner in new[] { "DELETED-POS", "POS-DISABLED" })
        {
            var historical = Assert.Single(devices, item => item.DeviceCode == owner);
            Assert.False(historical.IsAvailable);
            Assert.NotNull(historical.SelectedTerminalId);
            Assert.True(historical.SelectionRevision > 0);
        }
        await AssignAsync("POS-C", lineA, "DELETED-POS", 101, "POS-A", null, 0);
        Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
        Assert.Null(await SelectionAsync("DELETED-POS"));
    }

    [LinklyLineSqlServerFact]
    public async Task Source_owner_session_without_terminal_id_blocks_transfer()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        await SeedSessionAsync("POS-A", null, "Unknown", true);
        await Assert.ThrowsAnyAsync<Exception>(() => AssignAsync("POS-C", lineA, "POS-A", 101, "POS-B", null, 0));
        Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
        Assert.Null(await SelectionAsync("POS-B"));
    }

    [LinklyLineSqlServerFact]
    public async Task Target_old_line_lease_and_unknown_acknowledged_session_block_replace()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        await SeedSelectionAsync("POS-B", lineB, 201);
        var lease = Guid.NewGuid();
        Assert.True(await Repository().TryAcquireConnectionTestLeaseAsync("Production", "S001", lineB,
            version, lease, DateTime.UtcNow.AddMinutes(2), DateTime.UtcNow, default, "POS-B", 201));
        await Assert.ThrowsAnyAsync<Exception>(() => AssignAsync("POS-A", lineA, "POS-A", 101, "POS-B", lineB, 201));
        await Repository().ReleaseConnectionTestLeaseAsync("Production", "S001", lineB, lease, default);
        await SeedSessionAsync("HISTORICAL-POS", lineB, "Unknown", true);
        await Assert.ThrowsAnyAsync<Exception>(() => AssignAsync("POS-A", lineA, "POS-A", 101, "POS-B", lineB, 201));
        Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
        Assert.Equal(lineB, (await SelectionAsync("POS-B"))!.TerminalId);
    }

    [LinklyLineSqlServerFact]
    public async Task Concurrent_claims_have_one_winner_and_no_partial_release()
    {
        var attempts = await Task.WhenAll(
            CaptureAsync(() => AssignAsync("POS-A", lineA, null, 0, "POS-A", null, 0)),
            CaptureAsync(() => AssignAsync("POS-B", lineA, null, 0, "POS-B", null, 0)));
        Assert.Single(attempts, success => success);
        var owners = await Repository().ListAssignableDevicesAsync("Production", "S001", default);
        Assert.Single(owners, item => item.SelectedTerminalId == lineA);
        await AssertPairingPreservedAsync();
    }

    [LinklyLineSqlServerFact]
    public async Task Opposite_transfers_terminate_and_commit_only_one_complete_result()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        await SeedSelectionAsync("POS-B", lineB, 201);
        var attempts = await Task.WhenAll(
            CaptureAsync(() => AssignAsync("POS-C", lineA, "POS-A", 101, "POS-B", lineB, 201)),
            CaptureAsync(() => AssignAsync("POS-C", lineB, "POS-B", 201, "POS-A", lineA, 101)));
        Assert.Single(attempts, success => success);
        var a = await SelectionAsync("POS-A");
        var b = await SelectionAsync("POS-B");
        Assert.True((a is null && b?.TerminalId == lineA) || (b is null && a?.TerminalId == lineB));
    }

    [LinklyLineSqlServerFact]
    public async Task Unassigned_line_management_lease_blocks_assignment_without_changing_pairing()
    {
        var lease = Guid.NewGuid();
        Assert.True(await Repository().TryAcquireConnectionTestLeaseAsync("Production", "S001", lineA,
            version, lease, DateTime.UtcNow.AddMinutes(2), DateTime.UtcNow, default));
        await Assert.ThrowsAnyAsync<Exception>(() => AssignAsync("POS-A", lineA, null, 0, "POS-A", null, 0));
        Assert.Null(await SelectionAsync("POS-A"));
        await Repository().ReleaseConnectionTestLeaseAsync("Production", "S001", lineA, lease, default);
        await AssignAsync("POS-A", lineA, null, 0, "POS-A", null, 0);
        Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
        await AssertPairingPreservedAsync();
    }

    [LinklyLineSqlServerFact]
    public async Task Legacy_local_selection_cannot_bypass_old_or_new_line_management_lease()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        foreach (var terminal in new[] { lineA, lineB })
        {
            var lease = Guid.NewGuid();
            Assert.True(await Repository().TryAcquireConnectionTestLeaseAsync("Production", "S001", terminal,
                version, lease, DateTime.UtcNow.AddMinutes(2), DateTime.UtcNow, default,
                terminal == lineA ? "POS-A" : null, terminal == lineA ? 101 : 0));
            await Assert.ThrowsAnyAsync<Exception>(() => Repository().UpsertSelectionAsync(
                "Production", "S001", "POS-A", lineB, 101, DateTime.UtcNow, "TEST-OPERATOR", default));
            Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
            Assert.Equal(101, (await SelectionAsync("POS-A"))!.Revision);
            await Repository().ReleaseConnectionTestLeaseAsync("Production", "S001", terminal, lease, default);
        }
        await Repository().UpsertSelectionAsync("Production", "S001", "POS-A", lineB, 101,
            DateTime.UtcNow, "TEST-OPERATOR", default);
        Assert.Equal(lineB, (await SelectionAsync("POS-A"))!.TerminalId);
    }

    [LinklyLineSqlServerFact]
    public async Task Opposite_legacy_switches_return_conflicts_without_deadlock_or_partial_changes()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        await SeedSelectionAsync("POS-B", lineB, 201);
        var attempts = await Task.WhenAll(
            CaptureAsync(() => Repository().UpsertSelectionAsync("Production", "S001", "POS-A",
                lineB, 101, DateTime.UtcNow, "TEST", default)),
            CaptureAsync(() => Repository().UpsertSelectionAsync("Production", "S001", "POS-B",
                lineA, 201, DateTime.UtcNow, "TEST", default)));
        Assert.All(attempts, success => Assert.False(success));
        Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
        Assert.Equal(101, (await SelectionAsync("POS-A"))!.Revision);
        Assert.Equal(lineB, (await SelectionAsync("POS-B"))!.TerminalId);
        Assert.Equal(201, (await SelectionAsync("POS-B"))!.Revision);
    }

    [LinklyLineSqlServerFact]
    public async Task Connection_test_lease_rechecks_owner_after_target_line_is_displaced()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        await SeedSelectionAsync("POS-B", lineB, 201);
        await AssignAsync("POS-C", lineA, "POS-A", 101, "POS-B", lineB, 201);
        var lease = Guid.NewGuid();
        var currentVersion = (await Repository().GetAsync("Production", "S001", lineB, default))!.UpdatedAt!.Value;
        Assert.False(await Repository().TryAcquireConnectionTestLeaseAsync("Production", "S001", lineB,
            currentVersion, lease, DateTime.UtcNow.AddMinutes(2), DateTime.UtcNow, default, "POS-B", 201));
        Assert.Null((await Repository().GetAsync("Production", "S001", lineB, default))!.PairingAttemptId);
        Assert.Null(await OwnerAsync(lineB));
    }

    [LinklyLineSqlServerFact]
    public async Task Not_ready_line_rejects_binding_but_still_allows_unbinding()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        await ExecuteAsync("UPDATE [dbo].[POSM_LinklyCloudTerminal] SET [PairingState]=N'NeedsRepair' WHERE [TerminalId]=@Line;", new SqlParameter("@Line", lineA));
        await Assert.ThrowsAsync<LinklyCloudTerminalNotReadyException>(() =>
            AssignAsync("POS-C", lineA, "POS-A", 101, "POS-B", null, 0));
        Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
        await AssignAsync("POS-C", lineA, "POS-A", 101, null, null, 0);
        Assert.Null(await SelectionAsync("POS-A"));
    }

    [LinklyLineSqlServerFact]
    public async Task Connection_test_rejects_owner_unknown_session_without_terminal_id()
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        await SeedSessionAsync("POS-A", null, "Unknown", true);
        Assert.False(await Repository().TryAcquireConnectionTestLeaseAsync("Production", "S001", lineA,
            version, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(9), DateTime.UtcNow, default, "POS-A", 101));
        Assert.Null((await Repository().GetAsync("Production", "S001", lineA, default))!.PairingAttemptId);
    }

    [LinklyLineSqlServerFact]
    public Task Assignment_racing_session_creation_has_only_one_winner() => RaceWithSessionCreationAsync(false);

    [LinklyLineSqlServerFact]
    public Task Legacy_switch_racing_session_creation_has_only_one_winner() => RaceWithSessionCreationAsync(true);

    private async Task RaceWithSessionCreationAsync(bool legacy)
    {
        await SeedSelectionAsync("POS-A", lineA, 101);
        var sessionRepository = new SqlSugarLinklyCloudBackendAsyncRepository(CreateContext());
        var createSession = sessionRepository.TryCreateSessionAsync(new LinklyCloudBackendSessionRecord
        {
            Environment = "Production", StoreCode = "S001", DeviceCode = "POS-A",
            TerminalId = lineA, TerminalUpdatedAt = version, SelectionRevision = 101,
            SessionId = Guid.NewGuid().ToString("N"), Status = "Created", IsActive = true,
            UpdatedAt = DateTime.UtcNow,
        }, default);
        var assignment = CaptureAsync(() => legacy
            ? Repository().UpsertSelectionAsync("Production", "S001", "POS-A", lineB,
                101, DateTime.UtcNow, "TEST", default)
            : AssignAsync("POS-C", lineA, "POS-A", 101, "POS-B", null, 0));
        var results = await Task.WhenAll(createSession, assignment);
        Assert.Single(results, success => success);
        if (results[0])
        {
            Assert.Equal(lineA, (await SelectionAsync("POS-A"))!.TerminalId);
            Assert.Equal(101, (await SelectionAsync("POS-A"))!.Revision);
        }
        else
        {
            Assert.Equal(legacy ? lineB : lineA, (await SelectionAsync(legacy ? "POS-A" : "POS-B"))!.TerminalId);
        }
    }

    private Task AssignAsync(string caller, Guid line, string? owner, long sourceRevision,
        string? target, Guid? targetLine, long targetRevision, DateTime? expectedVersion = null) =>
        Repository().AssignTerminalAsync("Production", "S001", caller, line, expectedVersion ?? version,
            owner, sourceRevision, target, targetLine, targetRevision, DateTime.UtcNow, "TEST-OPERATOR", default);

    private Task<LinklyCloudDeviceSelectionRecord?> SelectionAsync(string device) =>
        Repository().GetSelectionAsync("Production", "S001", device, default);

    private async Task<string?> OwnerAsync(Guid line) =>
        (await Repository().ListAssignableDevicesAsync("Production", "S001", default))
            .FirstOrDefault(item => item.SelectedTerminalId == line)?.DeviceCode;

    private async Task AssertPairingPreservedAsync()
    {
        var lines = await Repository().ListAsync("Production", "S001", default);
        Assert.Equal(3, lines.Count);
        foreach (var line in lines)
        {
            var actual = (await Repository().GetAsync("Production", "S001", line.TerminalId, default))!;
            Assert.Equal("Ready", actual.PairingState);
            Assert.Equal("test-secret", actual.Secret);
            Assert.Equal("test-pos-id", actual.PosId);
        }
    }

    private SqlSugarLinklyCloudTerminalRepository Repository()
        => new(CreateContext(), new TestProtector());

    private HbposSqlSugarContext CreateContext()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MainConnection"] = connection,
            ["ConnectionStrings:PosmConnection"] = connection,
            ["Database:CommandTimeoutSeconds"] = "20",
        }).Build();
        return new HbposSqlSugarContext(config, NullLogger<HbposSqlSugarContext>.Instance);
    }

    private Task SeedLineAsync(Guid id, int lane) => ExecuteAsync("""
        INSERT INTO [dbo].[POSM_LinklyCloudTerminal]
            ([TerminalId],[Environment],[StoreCode],[LaneNo],[DisplayName],[Username],[Password],
             [Secret],[PosId],[PairingState],[CreatedAt],[UpdatedAt],[LastHealthStatus],[LastHealthAt])
        VALUES (@Id,N'Production',N'S001',@Lane,@Name,@Name,N'test-password',N'test-secret',
                N'test-pos-id',N'Ready',@At,@At,N'Healthy',@At);
        """, new("@Id", id), new("@Lane", lane), new("@Name", $"Test line {lane}"), At("@At", version));

    private Task SeedSelectionAsync(string device, Guid line, long revision) => ExecuteAsync("""
        INSERT INTO [dbo].[POSM_LinklyCloudDeviceSelection]
            ([Environment],[StoreCode],[DeviceCode],[TerminalId],[Revision])
        VALUES (N'Production',N'S001',@Device,@Line,@Revision);
        """, new("@Device", device), new("@Line", line), new("@Revision", revision));

    private Task SeedSessionAsync(string device, Guid? line, string status, bool acknowledged) => ExecuteAsync("""
        INSERT INTO [dbo].[POSM_LinklyCloudBackendSession]
            ([Environment],[StoreCode],[DeviceCode],[TerminalId],[SessionId],[Status],[IsActive],[ClientAcknowledgedAt])
        VALUES (N'Production',N'S001',@Device,@Line,@Session,@Status,0,@Ack);
        """, new("@Device", device), new("@Line", (object?)line ?? DBNull.Value),
        new("@Session", Guid.NewGuid().ToString()), new("@Status", status),
        new("@Ack", acknowledged ? DateTime.UtcNow : DBNull.Value));

    private Task ExecuteAsync(string sql, params SqlParameter[] parameters) => ExecuteAtAsync(connection, sql, parameters);

    private static async Task ExecuteAtAsync(string connectionString, string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static SqlParameter At(string name, DateTime value) => new(name, System.Data.SqlDbType.DateTime2) { Value = value };

    private static async Task<bool> CaptureAsync(Func<Task> action)
    {
        try { await action(); return true; }
        // 竞争失败必须是可恢复的绑定冲突，死锁、超时和 SQL 错误不能被误算作通过。
        catch (LinklyCloudTerminalSelectionConflictException) { return false; }
        catch (LinklyCloudTerminalAssignedException) { return false; }
    }

    private sealed class TestProtector : ILinklyCloudTerminalCredentialProtector
    {
        public string ProtectPassword(string value) => value;
        public string UnprotectPassword(string value) => value;
        public string ProtectSecret(string value) => value;
        public string UnprotectSecret(string value) => value;
    }
}
