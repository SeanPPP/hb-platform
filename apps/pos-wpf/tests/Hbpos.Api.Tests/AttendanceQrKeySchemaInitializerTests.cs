using Hbpos.Api;
using Hbpos.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hbpos.Api.Tests;

public sealed class AttendanceQrKeySchemaInitializerTests
{
    [Fact]
    public async Task InitializeAsync_executes_idempotent_attendance_qr_key_ddl()
    {
        var executor = new CapturingAttendanceQrKeySchemaSqlExecutor();
        var initializer = new SqlSugarAttendanceQrKeySchemaInitializer(executor);

        await initializer.InitializeAsync();

        var sql = Assert.Single(executor.SqlStatements);
        Assert.Contains("IF OBJECT_ID(N'[dbo].[AttendancePosQrKey]', N'U') IS NULL", sql);
        Assert.Contains("CREATE TABLE [dbo].[AttendancePosQrKey]", sql);
        Assert.Contains("ORDER BY [RegisteredAtUtc] DESC, [Kid] DESC", sql);
        Assert.Contains("SET [Status] = N'Revoked', [RevokedAtUtc] = SYSUTCDATETIME()", sql);
        Assert.Contains("IF NOT EXISTS", sql);
        Assert.Contains("CREATE UNIQUE INDEX [UX_AttendancePosQrKey_ActiveDevice]", sql);
        Assert.Contains("CREATE UNIQUE INDEX [UX_AttendancePosQrKey_ActiveHardware]", sql);
        Assert.Contains("WHERE [Status] = N'Active'", sql);
        Assert.Contains("WITH (TABLOCKX, HOLDLOCK)", sql);
        Assert.DoesNotContain("AttendancePunch", sql);
    }

    [Fact]
    public async Task InitializeAsync_submits_the_same_transactionally_guarded_script_on_repeated_calls()
    {
        var executor = new CapturingAttendanceQrKeySchemaSqlExecutor();
        var initializer = new SqlSugarAttendanceQrKeySchemaInitializer(executor);

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        Assert.Equal(2, executor.SqlStatements.Count);
        Assert.Equal(executor.SqlStatements[0], executor.SqlStatements[1]);
        Assert.All(executor.SqlStatements, sql =>
        {
            Assert.Contains("SET XACT_ABORT ON", sql);
            Assert.Contains("BEGIN TRANSACTION", sql);
            Assert.Contains("sys.sp_getapplock", sql);
            Assert.Contains("@Resource = N'AttendancePosQrKey_Schema_Initialization'", sql);
            Assert.Contains("@LockOwner = N'Transaction'", sql);
            Assert.Contains("IF @AttendanceQrSchemaLockResult < 0", sql);
            Assert.Contains("THROW", sql);
            Assert.Contains("COMMIT TRANSACTION", sql);
            Assert.Contains("OBJECT_ID(N'[dbo].[AttendancePosQrKey]', N'U') IS NULL", sql);
            Assert.Contains("WHERE [name] = N'UX_AttendancePosQrKey_ActiveDevice'", sql);
            Assert.Contains("WHERE [name] = N'UX_AttendancePosQrKey_ActiveHardware'", sql);
        });
    }

    [Fact]
    public void AddHbposApiServices_registers_attendance_qr_key_schema_services()
    {
        var services = new ServiceCollection();

        services.AddHbposApiServices();

        var executor = Assert.Single(
            services,
            service => service.ServiceType == typeof(IAttendanceQrKeySchemaSqlExecutor));
        Assert.Equal(typeof(SqlSugarAttendanceQrKeySchemaSqlExecutor), executor.ImplementationType);

        var initializer = Assert.Single(
            services,
            service => service.ServiceType == typeof(IAttendanceQrKeySchemaInitializer));
        Assert.Equal(typeof(SqlSugarAttendanceQrKeySchemaInitializer), initializer.ImplementationType);
    }

    [Fact]
    public void Startup_initializes_attendance_qr_key_schema()
    {
        var initializer = new RecordingAttendanceQrKeySchemaInitializer();
        using var factory = new AttendanceQrApiFactory(initializer);

        using var client = factory.CreateClient();

        Assert.Equal(1, initializer.InitializeCallCount);
    }

    [Fact]
    public void Startup_propagates_attendance_qr_key_schema_failure()
    {
        using var factory = new AttendanceQrApiFactory(new ThrowingAttendanceQrKeySchemaInitializer());

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Equal("attendance-qr-schema-failed", exception.Message);
    }

    private sealed class CapturingAttendanceQrKeySchemaSqlExecutor : IAttendanceQrKeySchemaSqlExecutor
    {
        public List<string> SqlStatements { get; } = [];

        public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
        {
            SqlStatements.Add(sql);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAttendanceQrKeySchemaInitializer : IAttendanceQrKeySchemaInitializer
    {
        public int InitializeCallCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAttendanceQrKeySchemaInitializer : IAttendanceQrKeySchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("attendance-qr-schema-failed");
        }
    }

    private sealed class AttendanceQrApiFactory(
        IAttendanceQrKeySchemaInitializer attendanceQrKeySchemaInitializer)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStoreSchemaInitializer>();
                services.AddSingleton<IStoreSchemaInitializer>(new NoOpStoreSchemaInitializer());

                services.RemoveAll<IAttendanceQrKeySchemaInitializer>();
                services.AddSingleton(attendanceQrKeySchemaInitializer);

                services.RemoveAll<IAdvertisementSchemaInitializer>();
                services.AddSingleton<IAdvertisementSchemaInitializer>(new NoOpAdvertisementSchemaInitializer());

                services.RemoveAll<ILinklyCloudCredentialSchemaInitializer>();
                services.AddSingleton<ILinklyCloudCredentialSchemaInitializer>(new NoOpLinklyCloudCredentialSchemaInitializer());

                services.RemoveAll<ISquareTokenSchemaInitializer>();
                services.AddSingleton<ISquareTokenSchemaInitializer>(new NoOpSquareTokenSchemaInitializer());
            });
        }
    }

    private sealed class NoOpStoreSchemaInitializer : IStoreSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpAdvertisementSchemaInitializer : IAdvertisementSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpLinklyCloudCredentialSchemaInitializer : ILinklyCloudCredentialSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpSquareTokenSchemaInitializer : ISquareTokenSchemaInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
