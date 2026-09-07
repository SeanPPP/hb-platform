using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class RemoteMaintenancePrepareTests
{
    [Theory]
    [InlineData(" 1042 ", " POS_1042_2007 ", "1042", "POS_1042_2007")]
    [InlineData(null, " \t ", "", "")]
    [InlineData("123456789012345678901234567890123456789012345678901", "POS", "12345678901234567890123456789012345678901234567890", "POS")]
    public async Task Prepare_GeneratesExecutableSqlAndPreservesOperationGuard(
        string? storeCode, string? deviceCode, string expectedStore, string expectedDevice)
    {
        using var fixture = new PrepareFixture(storeCode, deviceCode);

        var result = await fixture.Service.PrepareAsync(fixture.Request);

        Assert.True(result.Success, result.Code);
        Assert.Equal(fixture.Request.OperationId, result.Data!.OperationId);
        var generated = fixture.Update.ToSql();
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(generated.Key, @"\)\s*\("), generated.Key);
        // 执行服务实际生成的参数化 UPDATE；不在测试中复制生产表达式。
        // SQL Server 的 ISNULL 对应 SQLite 的 IFNULL；仅适配函数名，保留原始 WHERE。
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var setup = connection.CreateCommand();
        setup.CommandText = """
            CREATE TABLE HBweb_RemoteMaintenanceDevice (
                Id TEXT PRIMARY KEY, HardwareId TEXT, StoreCode TEXT, DeviceCode TEXT,
                ComputerName TEXT, LastOperationId TEXT, IsDeleted INTEGER, LastAcceptedSequence INTEGER);
            INSERT INTO HBweb_RemoteMaintenanceDevice VALUES (@id, 'old', 'old', 'old', 'old', NULL, 0, 42);
            """;
        setup.Parameters.AddWithValue("@id", fixture.Row.Id);
        setup.ExecuteNonQuery();
        using var command = connection.CreateCommand();
        command.CommandText = System.Text.RegularExpressions.Regex.Replace(generated.Key, @"\bISNULL\s*\(", "IFNULL(");
        foreach (var parameter in generated.Value)
            command.Parameters.AddWithValue(parameter.ParameterName, parameter.Value ?? DBNull.Value);
        Assert.Equal(1, command.ExecuteNonQuery());
        using var readback = connection.CreateCommand();
        readback.CommandText = "SELECT StoreCode, DeviceCode, HardwareId, ComputerName, LastOperationId, LastAcceptedSequence FROM HBweb_RemoteMaintenanceDevice";
        using (var reader = readback.ExecuteReader())
        {
            Assert.True(reader.Read());
            Assert.Equal(expectedStore, reader.GetString(0));
            Assert.Equal(expectedDevice, reader.GetString(1));
            Assert.Equal(fixture.Request.HardwareId, reader.GetString(2));
            Assert.Equal(fixture.Request.ComputerName, reader.GetString(3));
            Assert.Equal(fixture.Request.OperationId, reader.GetGuid(4));
            Assert.Equal(42, reader.GetInt64(5));
        }
        // 同一操作可重试，其他操作和已删除设备必须被数据库条件挡住。
        Assert.Equal(1, command.ExecuteNonQuery());
        using var mutate = connection.CreateCommand();
        mutate.CommandText = "UPDATE HBweb_RemoteMaintenanceDevice SET LastOperationId = @other";
        mutate.Parameters.AddWithValue("@other", Guid.NewGuid());
        mutate.ExecuteNonQuery();
        Assert.Equal(0, command.ExecuteNonQuery());
        mutate.CommandText = "UPDATE HBweb_RemoteMaintenanceDevice SET LastOperationId = NULL, IsDeleted = 1";
        mutate.ExecuteNonQuery();
        Assert.Equal(0, command.ExecuteNonQuery());
    }

    [Fact]
    public async Task Prepare_ReturnsConflictWhenConcurrentUpdateLoses()
    {
        using var fixture = new PrepareFixture("1042", "POS", affectedRows: 0);

        var result = await fixture.Service.PrepareAsync(fixture.Request);

        Assert.False(result.Success);
        Assert.Equal("REMOTE_MAINTENANCE_OPERATION_CONFLICT", result.Code);
    }

    private sealed class PrepareFixture : IDisposable
    {
        private readonly string _artifactPath = Path.GetTempFileName();
        private readonly SqlSugarClient _sql = new(new ConnectionConfig
        {
            ConnectionString = "Server=127.0.0.1;Database=unused;Integrated Security=true;",
            DbType = SqlSugar.DbType.SqlServer,
            InitKeyType = InitKeyType.Attribute,
            IsAutoCloseConnection = true
        });

        public RemoteMaintenanceDevice Row { get; } = new() { Id = Guid.NewGuid(), DeviceRegistrationId = 2007 };
        public RemoteMaintenancePrepareInternalRequest Request { get; } = new(Guid.NewGuid(), "test-hardware", "test-pos");
        public IUpdateable<RemoteMaintenanceDevice> Update { get; }
        public RemoteMaintenanceService Service { get; }

        public PrepareFixture(string? storeCode, string? deviceCode, int affectedRows = 1)
        {
            Update = _sql.Updateable<RemoteMaintenanceDevice>();
            var writer = new Mock<IUpdateable<RemoteMaintenanceDevice>>(MockBehavior.Strict);
            writer.Setup(x => x.SetColumns(It.IsAny<Expression<Func<RemoteMaintenanceDevice, RemoteMaintenanceDevice>>>()))
                .Callback<Expression<Func<RemoteMaintenanceDevice, RemoteMaintenanceDevice>>>(columns => Update.SetColumns(columns))
                .Returns(writer.Object);
            writer.Setup(x => x.Where(It.IsAny<Expression<Func<RemoteMaintenanceDevice, bool>>>()))
                .Callback<Expression<Func<RemoteMaintenanceDevice, bool>>>(predicate => Update.Where(predicate))
                .Returns(writer.Object);
            writer.Setup(x => x.ExecuteCommandAsync()).ReturnsAsync(affectedRows);
            var devices = new Mock<ISugarQueryable<RemoteMaintenanceDevice>>(MockBehavior.Strict);
            devices.Setup(x => x.FirstAsync(It.IsAny<Expression<Func<RemoteMaintenanceDevice, bool>>>())).ReturnsAsync(Row);
            var ado = new Mock<IAdo>(MockBehavior.Strict);
            ado.Setup(x => x.GetIntAsync(It.IsAny<string>(), It.Is<SugarParameter[]>(p => p.Length == 0))).ReturnsAsync(1);
            var main = new Mock<ISqlSugarClient>(MockBehavior.Strict);
            main.SetupGet(x => x.Ado).Returns(ado.Object);
            main.Setup(x => x.Queryable<RemoteMaintenanceDevice>()).Returns(devices.Object);
            main.Setup(x => x.Updateable<RemoteMaintenanceDevice>()).Returns(writer.Object);

            var registrations = new Mock<ISugarQueryable<POSM_设备注册信息表>>(MockBehavior.Strict);
            registrations.Setup(x => x.Where(It.IsAny<Expression<Func<POSM_设备注册信息表, bool>>>())).Returns(registrations.Object);
            registrations.Setup(x => x.OrderByDescending(It.IsAny<Expression<Func<POSM_设备注册信息表, object>>>())).Returns(registrations.Object);
            registrations.Setup(x => x.FirstAsync()).ReturnsAsync(new POSM_设备注册信息表
            {
                ID = Row.DeviceRegistrationId, 分店代码 = storeCode!, 系统设备编号 = deviceCode!,
                设备状态 = 1, 设备类型 = "POS", 设备系统 = "Windows"
            });
            var posm = new Mock<ISqlSugarClient>(MockBehavior.Strict);
            posm.Setup(x => x.Queryable<POSM_设备注册信息表>()).Returns(registrations.Object);
            var context = CreateContext<SqlSugarContext>(main.Object);
            File.WriteAllBytes(_artifactPath, [1, 2, 3]);
            var artifact = new RemoteMaintenanceArtifactOptions
            {
                Version = "test", FileName = "test.exe", Path = _artifactPath,
                SizeBytes = 3, Sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 }))
            };
            Service = new RemoteMaintenanceService(context, CreateContext<POSMSqlSugarContext>(posm.Object), null!,
                Options.Create(new RemoteMaintenanceOptions
                {
                    Enabled = true, PublicKey = "test", RustdeskArtifact = artifact, StatusAgentArtifact = artifact
                }), NullLogger<RemoteMaintenanceService>.Instance,
                new RemoteMaintenanceSchemaReadiness(context, NullLogger<RemoteMaintenanceSchemaReadiness>.Instance));
        }

        private static T CreateContext<T>(ISqlSugarClient db)
        {
            // 仅替换数据库边界，真实业务方法和 SqlServer SQL 生成器照常运行。
            var context = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(T).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
            return context;
        }

        public void Dispose()
        {
            _sql.Dispose();
            File.Delete(_artifactPath);
        }
    }
}
