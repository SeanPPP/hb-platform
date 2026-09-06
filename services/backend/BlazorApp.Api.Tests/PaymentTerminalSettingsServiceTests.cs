using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using LinklyCredentialDataProtection = BlazorApp.Api.Security.LinklyCloudTerminalCredentialDataProtection;
using LinklyCredentialProtector = BlazorApp.Shared.Security.ILinklyCloudTerminalCredentialProtector;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PaymentTerminalSettingsServiceTests : IDisposable
{
    private readonly string _mainDbPath;
    private readonly string _posmDbPath;
    private readonly SqliteConnection _mainConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _mainDb;
    private readonly SqlSugarClient _posmDb;
    private readonly string _linklyCredentialKeysPath;
    private readonly LinklyCredentialProtector _linklyCredentialProtector;

    public PaymentTerminalSettingsServiceTests()
    {
        _mainDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.main.db");
        _posmDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.posm.db");
        _mainConnection = new SqliteConnection($"Data Source={_mainDbPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmDbPath}");
        _mainConnection.Open();
        _posmConnection.Open();

        _mainDb = CreateDb(_mainConnection.ConnectionString);
        _posmDb = CreateDb(_posmConnection.ConnectionString);
        _linklyCredentialKeysPath = Path.Combine(
            Path.GetTempPath(),
            $"hb-linkly-terminal-test-keys-{Guid.NewGuid():N}"
        );
        _linklyCredentialProtector = LinklyCredentialDataProtection.CreateProtector(
            LinklyCredentialDataProtection.CreateProvider(_linklyCredentialKeysPath)
        );

        _mainDb.CodeFirst.InitTables(typeof(Store));
        CreatePaymentTables();
    }

    [Fact]
    public async Task UpdateSquareTokenAsync_WritesEnabledTokenAndReturnsSanitizedStatus()
    {
        var service = CreateService();

        var result = await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto
            {
                Environment = "sandbox",
                AccessToken = "  sandbox-secret  ",
            },
            "admin"
        );

        var rows = await QuerySquareRowsAsync("Sandbox");

        Assert.True(result.Success);
        Assert.True(result.Data!.Square.Single(item => item.Environment == "Sandbox").Configured);
        Assert.Single(rows);
        Assert.True(rows[0].IsEnabled);
        Assert.Equal("sandbox-secret", rows[0].AccessToken);
        Assert.DoesNotContain(
            typeof(PaymentTerminalEnvironmentStatusDto).GetProperties(),
            property => property.Name == "AccessToken"
        );
    }

    [Fact]
    public async Task UpdateSquareTokenAsync_ReplacesEnabledTokenForEnvironment()
    {
        var service = CreateService();
        await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto { Environment = "Production", AccessToken = "first-token" },
            "admin"
        );

        await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto { Environment = "Production", AccessToken = "second-token" },
            "admin"
        );

        var rows = await QuerySquareRowsAsync("Production");

        Assert.Equal(2, rows.Count);
        Assert.Single(rows, row => row.IsEnabled);
        Assert.Equal("second-token", rows.Single(row => row.IsEnabled).AccessToken);
        Assert.False(rows.Single(row => row.AccessToken == "first-token").IsEnabled);
    }

    [Fact]
    public async Task UpdateSquareTokenAsync_ClearDisablesTokenAndBlanksSecret()
    {
        var service = CreateService();
        await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto { Environment = "Production", AccessToken = "secret-token" },
            "admin"
        );

        var result = await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto { Environment = "Production", ClearToken = true },
            "admin"
        );
        var row = (await QuerySquareRowsAsync("Production")).Single();

        Assert.True(result.Success);
        Assert.False(result.Data!.Square.Single(item => item.Environment == "Production").Configured);
        Assert.False(row.IsEnabled);
        Assert.Equal(string.Empty, row.AccessToken);
    }

    [Fact]
    public async Task UpdateSquareTokenAsync_ReturnsCurrentStoreSelectionWhenProvided()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedStore("002", "Beach Store");

        var result = await service.UpdateSquareTokenAsync(
            new UpdateSquareTokenDto { Environment = "Sandbox", AccessToken = "sandbox-token" },
            "admin",
            "002"
        );

        Assert.True(result.Success);
        Assert.Equal("002", result.Data!.SelectedStoreCode);
    }

    [Fact]
    public async Task UpdateLinklyCredentialAsync_KeepsExistingPasswordWhenPasswordIsBlank()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "001",
                Environment = "Production",
                Username = "old-user",
                Password = "old-password",
            },
            "admin"
        );

        var result = await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "001",
                Environment = "Production",
                Username = "new-user",
                Password = "   ",
            },
            "admin"
        );
        var row = (await QueryLinklyRowsAsync("001", "Production")).Single();

        Assert.True(result.Success);
        Assert.True(result.Data!.Linkly.Single(item => item.Environment == "Production").HasPassword);
        Assert.Equal("new-user", row.Username);
        Assert.Equal("old-password", row.Password);
        Assert.DoesNotContain(
            typeof(LinklyCloudCredentialAdminDto).GetProperties(),
            property => property.Name == "Password"
        );
    }

    [Fact]
    public async Task UpdateLinklyCredentialAsync_WhenPasswordBlankWithoutExistingCredential_ReturnsValidationError()
    {
        var service = CreateService();
        SeedStore("001", "City Store");

        var result = await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "001",
                Environment = "Sandbox",
                Username = "sandbox-user",
                Password = " ",
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("LINKLY_PASSWORD_REQUIRED", result.ErrorCode);
        Assert.Empty(await QueryLinklyRowsAsync("001", "Sandbox"));
    }

    [Fact]
    public async Task UpdateLinklyCredentialAsync_ClearDeletesCredential()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "001",
                Environment = "Sandbox",
                Username = "sandbox-user",
                Password = "sandbox-password",
            },
            "admin"
        );

        var result = await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "001",
                Environment = "Sandbox",
                ClearCredential = true,
            },
            "admin"
        );

        Assert.True(result.Success);
        Assert.False(result.Data!.Linkly.Single(item => item.Environment == "Sandbox").HasPassword);
        Assert.Empty(await QueryLinklyRowsAsync("001", "Sandbox"));
    }

    [Fact]
    public async Task GetSettingsAsync_ReturnsStoresAndSelectedStoreStatuses()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedStore("002", "Beach Store");
        await service.UpdateLinklyCredentialAsync(
            new UpdateLinklyCredentialDto
            {
                StoreCode = "002",
                Environment = "Production",
                Username = "linkly-user",
                Password = "linkly-password",
            },
            "admin"
        );

        var result = await service.GetSettingsAsync("002");

        Assert.True(result.Success);
        Assert.Equal("002", result.Data!.SelectedStoreCode);
        Assert.Equal(new[] { "001", "002" }, result.Data.Stores.Select(store => store.StoreCode).ToArray());
        Assert.True(result.Data.Linkly.Single(item => item.Environment == "Production").HasPassword);
        Assert.Equal("linkly-user", result.Data.Linkly.Single(item => item.Environment == "Production").Username);
    }

    [Fact]
    public async Task CreateLinklyTerminalAsync_CreatesDraftTerminalAndReturnsOnlyMaskedCredential()
    {
        var service = CreateService();
        SeedStore("001", "City Store");

        var result = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = " 001 ",
                Environment = "production",
                LaneNo = 1,
                DisplayName = " Front Counter ",
                Username = " test-user-001 ",
                Password = " lane-secret ",
            },
            "admin"
        );

        var row = (await QueryLinklyTerminalRowsAsync("001", "Production")).Single();
        Assert.True(result.Success);
        Assert.Equal("Draft", result.Data!.Mode);
        Assert.Equal("Unpaired", row.PairingState);
        Assert.Equal("test-user-001", row.Username);
        Assert.NotEqual("lane-secret", row.Password);
        Assert.Equal("lane-secret", _linklyCredentialProtector.UnprotectPassword(row.Password));
        Assert.Equal(LinklyCredentialDataProtection.CurrentVersion, row.CredentialProtectionVersion);
        Assert.Equal("test" + new string('\u2022', 6) + "001", result.Data.Terminals.Single().UsernameMasked);
        Assert.True(result.Data.Terminals.Single().HasPassword);
        Assert.DoesNotContain(
            typeof(LinklyTerminalAdminDto).GetProperties(),
            property => new[] { "Username", "Password", "Secret", "PosId", "PairCode" }.Contains(property.Name)
        );
    }

    [Fact]
    public async Task CreateLinklyTerminalAsync_DoesNotRevealShortUsername()
    {
        var service = CreateService();
        SeedStore("001", "City Store");

        var result = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "1234567",
                Password = "lane-secret",
            },
            "admin"
        );

        Assert.True(result.Success);
        Assert.Equal(new string('\u2022', 7), result.Data!.Terminals.Single().UsernameMasked);
    }

    [Fact]
    public async Task UpdateLinklyTerminalAsync_WhenCredentialChanges_ClearsPairingMaterialAndRequiresRepair()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_LinklyCloudTerminal SET PairingState = 'Ready', Secret = 'paired-secret', PosId = 'POS-1' WHERE TerminalId = @TerminalId",
            new SugarParameter("@TerminalId", terminalId)
        );

        var result = await service.UpdateLinklyTerminalAsync(
            terminalId,
            new UpdateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-099",
            },
            "admin"
        );

        var row = (await QueryLinklyTerminalRowsAsync("001", "Production")).Single();
        Assert.True(result.Success);
        Assert.Equal("NeedsRepair", row.PairingState);
        Assert.Null(row.Secret);
        Assert.Null(row.PosId);
        Assert.NotEqual("lane-secret", row.Password);
        Assert.Equal("lane-secret", _linklyCredentialProtector.UnprotectPassword(row.Password));
    }

    [Fact]
    public async Task UpdateLinklyTerminalAsync_WhenCredentialsAreOmitted_PreservesPairingAndSecrets()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_LinklyCloudTerminal SET PairingState = 'Ready', Secret = 'paired-secret', PosId = 'POS-1' WHERE TerminalId = @TerminalId",
            new SugarParameter("@TerminalId", terminalId)
        );

        var passwordBeforeEdit = (await QueryLinklyTerminalRowsAsync("001", "Production")).Single().Password;
        var result = await service.UpdateLinklyTerminalAsync(
            terminalId,
            new UpdateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Main Counter",
                Username = "   ",
                Password = "   ",
            },
            "admin"
        );

        var row = (await QueryLinklyTerminalRowsAsync("001", "Production")).Single();
        Assert.True(result.Success);
        Assert.Equal("Ready", row.PairingState);
        Assert.Equal("test-user-001", row.Username);
        Assert.Equal(passwordBeforeEdit, row.Password);
        Assert.NotEqual("lane-secret", row.Password);
        Assert.Equal("lane-secret", _linklyCredentialProtector.UnprotectPassword(row.Password));
        Assert.Equal("paired-secret", row.Secret);
        Assert.Equal("POS-1", row.PosId);
    }

    [Fact]
    public async Task UpdateLinklyTerminalAsync_SubmittedPasswordRotatesProtectedValueAndClearsPairing()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "old-password-sentinel",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        var previous = (await QueryLinklyTerminalRowsAsync("001", "Production")).Single();
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_LinklyCloudTerminal SET PairingState = 'Ready', Secret = 'paired-secret', PosId = 'POS-1' WHERE TerminalId = @TerminalId",
            new SugarParameter("@TerminalId", terminalId)
        );

        var result = await service.UpdateLinklyTerminalAsync(
            terminalId,
            new UpdateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Password = "new-password-sentinel",
            },
            "admin"
        );

        var row = (await QueryLinklyTerminalRowsAsync("001", "Production")).Single();
        Assert.True(result.Success);
        Assert.NotEqual(previous.Password, row.Password);
        Assert.NotEqual("new-password-sentinel", row.Password);
        Assert.Equal("new-password-sentinel", _linklyCredentialProtector.UnprotectPassword(row.Password));
        Assert.Equal(LinklyCredentialDataProtection.CurrentVersion, row.CredentialProtectionVersion);
        Assert.Null(row.Secret);
        Assert.Null(row.PosId);
        Assert.Equal("NeedsRepair", row.PairingState);
    }

    [Fact]
    public async Task ActivateLinklyConfigurationAsync_RejectsLegacyPlaintextCredentialAndMarksItNotReady()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedPosDevice("POS-01", "001");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-password",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        await _posmDb.Ado.ExecuteCommandAsync(
            """
            UPDATE POSM_LinklyCloudTerminal
            SET CredentialProtectionVersion = 0, PairingState = 'Ready', Secret = 'legacy-secret', PosId = 'POS-1'
            WHERE TerminalId = @TerminalId
            """,
            new SugarParameter("@TerminalId", terminalId)
        );
        await _posmDb.Ado.ExecuteCommandAsync(
            """
            INSERT INTO POSM_LinklyCloudDeviceSelection
                (Environment, StoreCode, DeviceCode, TerminalId, Revision, UpdatedAt)
            VALUES ('Production', '001', 'POS-01', @TerminalId, 1, @UpdatedAt)
            """,
            new SugarParameter("@TerminalId", terminalId),
            new SugarParameter("@UpdatedAt", DateTime.UtcNow)
        );

        var management = await service.GetLinklyTerminalManagementAsync("001", "Production");
        var activation = await service.ActivateLinklyConfigurationAsync(
            new ActivateLinklyConfigurationDto { StoreCode = "001", Environment = "Production" },
            "admin"
        );

        Assert.True(management.Success);
        Assert.False(management.Data!.Terminals.Single().HasPassword);
        Assert.Equal("NeedsRepair", management.Data.Terminals.Single().PairingState);
        Assert.False(activation.Success);
        Assert.Equal("LINKLY_TERMINAL_CREDENTIAL_REENTRY_REQUIRED", activation.Code);
    }

    [Fact]
    public async Task CreateLinklyTerminalAsync_WhenProtectionFails_DoesNotPersistCredential()
    {
        var service = CreateService(new ThrowingLinklyCredentialProtector());
        SeedStore("001", "City Store");

        var result = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "password-sentinel",
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("LINKLY_TERMINAL_CREDENTIAL_PROTECTION_FAILED", result.Code);
        Assert.Empty(await QueryLinklyTerminalRowsAsync("001", "Production"));
    }

    [Theory]
    [InlineData("Pending", true)]
    [InlineData("Completed", false)]
    public async Task UpdateLinklyTerminalAsync_WhenSessionRequiresRecovery_DoesNotRotateCredential(
        string status,
        bool isActive
    )
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        await _posmDb.Ado.ExecuteCommandAsync(
            """
            INSERT INTO POSM_LinklyCloudBackendSession
                (Environment, StoreCode, DeviceCode, TerminalId, SessionId, Status, OperationType, ClientAcknowledgedAt, IsActive, UpdatedAt)
            VALUES
                ('Production', '001', 'POS-01', @TerminalId, 'SESSION-1', @Status, 'Transaction', NULL, @IsActive, @UpdatedAt)
            """,
            new SugarParameter("@TerminalId", terminalId),
            new SugarParameter("@Status", status),
            new SugarParameter("@IsActive", isActive ? 1 : 0),
            new SugarParameter("@UpdatedAt", DateTime.UtcNow)
        );

        var result = await service.UpdateLinklyTerminalAsync(
            terminalId,
            new UpdateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-099",
            },
            "admin"
        );

        var row = (await QueryLinklyTerminalRowsAsync("001", "Production")).Single();
        Assert.False(result.Success);
        Assert.Equal("LINKLY_TERMINAL_SESSION_ACTIVE", result.ErrorCode);
        Assert.Equal("test-user-001", row.Username);
        Assert.NotEqual("lane-secret", row.Password);
        Assert.Equal("lane-secret", _linklyCredentialProtector.UnprotectPassword(row.Password));
    }

    [Fact]
    public async Task SetLinklyDeviceSelectionAsync_RejectsTerminalFromAnotherStore()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedStore("002", "North Store");
        SeedPosDevice("POS-01", "001");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "002",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "North Counter",
                Username = "test-user-002",
                Password = "lane-secret",
            },
            "admin"
        );

        var result = await service.SetLinklyDeviceSelectionAsync(
            "POS-01",
            new UpdateLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                TerminalId = created.Data!.Terminals.Single().TerminalId,
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("LINKLY_TERMINAL_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task SetLinklyDeviceSelectionAsync_BlocksUnacknowledgedSessionInsideMutationBoundary()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedPosDevice("POS-01", "001");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_LinklyCloudTerminal SET PairingState = 'Ready', Secret = 'paired-secret', PosId = 'POS-1' WHERE TerminalId = @TerminalId",
            new SugarParameter("@TerminalId", terminalId)
        );
        await SeedBlockingSessionAsync(terminalId, "POS-01", "Completed", false);

        var result = await service.SetLinklyDeviceSelectionAsync(
            "POS-01",
            new UpdateLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                TerminalId = terminalId,
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("LINKLY_TERMINAL_SESSION_ACTIVE", result.ErrorCode);
        var count = await _posmDb.Ado.GetIntAsync("SELECT COUNT(*) FROM POSM_LinklyCloudDeviceSelection");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SetLinklyDeviceSelectionAsync_RejectsTerminalAssignedToAnotherPos()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedPosDevice("POS-01", "001");
        SeedPosDevice("POS-02", "001");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;

        var first = await service.SetLinklyDeviceSelectionAsync(
            "POS-01",
            new UpdateLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                TerminalId = terminalId,
            },
            "admin"
        );
        var second = await service.SetLinklyDeviceSelectionAsync(
            "POS-02",
            new UpdateLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                TerminalId = terminalId,
            },
            "admin"
        );

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal("LINKLY_TERMINAL_ASSIGNMENT_CONFLICT", second.ErrorCode);
        var management = (await service.GetLinklyTerminalManagementAsync("001", "Production")).Data!;
        Assert.Equal(terminalId, management.Devices.Single(device => device.DeviceCode == "POS-01").TerminalId);
        Assert.Null(management.Devices.Single(device => device.DeviceCode == "POS-02").TerminalId);
    }

    [Fact]
    public async Task DeleteLinklyDeviceSelectionAsync_ExposesAndReleasesDisabledOrMissingDeviceSelection()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedPosDevice("POS-DISABLED", "001");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        await _posmDb.Ado.ExecuteCommandAsync(
            """
            INSERT INTO POSM_LinklyCloudDeviceSelection
                (Environment, StoreCode, DeviceCode, TerminalId, Revision, UpdatedAt, UpdatedBy)
            VALUES ('Production', '001', 'POS-DISABLED', @TerminalId, 7, @UpdatedAt, 'admin')
            """,
            new SugarParameter("@TerminalId", terminalId),
            new SugarParameter("@UpdatedAt", DateTime.UtcNow)
        );
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_设备注册信息表 SET 设备状态 = 0, 是否允许交易 = 0 WHERE 系统设备编号 = 'POS-DISABLED'"
        );
        // 与生产一对一唯一索引一致，验证释放后该实体终端立刻可被替代 POS 占用。
        await _posmDb.Ado.ExecuteCommandAsync(
            "CREATE UNIQUE INDEX UX_POSM_LinklyCloudDeviceSelection_Scope_Terminal ON POSM_LinklyCloudDeviceSelection(Environment, StoreCode, TerminalId)"
        );

        var disabledManagement = (await service.GetLinklyTerminalManagementAsync("001", "Production")).Data!;
        var disabled = disabledManagement.Devices.Single(device => device.DeviceCode == "POS-DISABLED");
        Assert.False(disabled.Enabled);
        Assert.False(disabled.DeviceMissing);
        Assert.Equal(terminalId, disabled.TerminalId);
        Assert.Equal(7, disabled.Revision);

        await _posmDb.Ado.ExecuteCommandAsync(
            "DELETE FROM POSM_设备注册信息表 WHERE 系统设备编号 = 'POS-DISABLED'"
        );
        var missingManagement = (await service.GetLinklyTerminalManagementAsync("001", "Production")).Data!;
        var missing = missingManagement.Devices.Single(device => device.DeviceCode == "POS-DISABLED");
        Assert.False(missing.Enabled);
        Assert.True(missing.DeviceMissing);
        Assert.Equal(terminalId, missing.TerminalId);
        Assert.Equal(7, missing.Revision);

        var released = await service.DeleteLinklyDeviceSelectionAsync(
            "POS-DISABLED",
            new DeleteLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                ExpectedRevision = 7,
            },
            "admin"
        );

        Assert.True(released.Success);
        Assert.Empty(released.Data!.Devices);
        Assert.Equal(0, await _posmDb.Ado.GetIntAsync("SELECT COUNT(*) FROM POSM_LinklyCloudDeviceSelection"));

        SeedPosDevice("POS-REPLACEMENT", "001");
        var replacement = await service.SetLinklyDeviceSelectionAsync(
            "POS-REPLACEMENT",
            new UpdateLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                TerminalId = terminalId,
            },
            "admin"
        );
        Assert.True(replacement.Success);
        Assert.Equal(
            terminalId,
            replacement.Data!.Devices.Single(device => device.DeviceCode == "POS-REPLACEMENT").TerminalId
        );
    }

    [Fact]
    public async Task DeleteLinklyDeviceSelectionAsync_UsesRevisionAndBlocksDeviceOrTerminalRecoverySession()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedPosDevice("POS-01", "001");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        Assert.True((await service.SetLinklyDeviceSelectionAsync(
            "POS-01",
            new UpdateLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                TerminalId = terminalId,
            },
            "admin")).Success);

        var stale = await service.DeleteLinklyDeviceSelectionAsync(
            "POS-01",
            new DeleteLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                ExpectedRevision = 2,
            },
            "admin"
        );
        Assert.False(stale.Success);
        Assert.Equal("LINKLY_SELECTION_REVISION_CONFLICT", stale.ErrorCode);

        var enabledDevice = await service.DeleteLinklyDeviceSelectionAsync(
            "POS-01",
            new DeleteLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                ExpectedRevision = 1,
            },
            "admin"
        );
        Assert.False(enabledDevice.Success);
        Assert.Equal("LINKLY_DEVICE_SELECTION_RELEASE_NOT_ALLOWED", enabledDevice.ErrorCode);

        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_设备注册信息表 SET 设备状态 = 0, 是否允许交易 = 0 WHERE 系统设备编号 = 'POS-01'"
        );
        await _posmDb.Ado.ExecuteCommandAsync(
            """
            UPDATE POSM_LinklyCloudTerminal
            SET PairingAttemptId = @AttemptId, PairingLeaseExpiresAt = @ExpiresAt
            WHERE TerminalId = @TerminalId
            """,
            new SugarParameter("@AttemptId", Guid.NewGuid()),
            new SugarParameter("@ExpiresAt", DateTime.UtcNow.AddMinutes(5)),
            new SugarParameter("@TerminalId", terminalId)
        );
        var blockedByLease = await service.DeleteLinklyDeviceSelectionAsync(
            "POS-01",
            new DeleteLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                ExpectedRevision = 1,
            },
            "admin"
        );
        Assert.False(blockedByLease.Success);
        Assert.Equal("LINKLY_TERMINAL_SESSION_ACTIVE", blockedByLease.ErrorCode);
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_LinklyCloudTerminal SET PairingAttemptId = NULL, PairingLeaseExpiresAt = NULL WHERE TerminalId = @TerminalId",
            new SugarParameter("@TerminalId", terminalId)
        );
        var otherTerminal = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 2,
                DisplayName = "Back Counter",
                Username = "test-user-002",
                Password = "lane-secret",
            },
            "admin"
        );
        await SeedBlockingSessionAsync(
            otherTerminal.Data!.Terminals.Single(terminal => terminal.LaneNo == 2).TerminalId,
            "POS-01",
            "Completed",
            false
        );
        var blockedByDeviceSession = await service.DeleteLinklyDeviceSelectionAsync(
            "POS-01",
            new DeleteLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                ExpectedRevision = 1,
            },
            "admin"
        );
        Assert.False(blockedByDeviceSession.Success);
        Assert.Equal("LINKLY_TERMINAL_SESSION_ACTIVE", blockedByDeviceSession.ErrorCode);
        await _posmDb.Ado.ExecuteCommandAsync("DELETE FROM POSM_LinklyCloudBackendSession");
        await SeedBlockingSessionAsync(terminalId, "OTHER-POS", "Completed", false);
        var blocked = await service.DeleteLinklyDeviceSelectionAsync(
            "POS-01",
            new DeleteLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                ExpectedRevision = 1,
            },
            "admin"
        );
        Assert.False(blocked.Success);
        Assert.Equal("LINKLY_TERMINAL_SESSION_ACTIVE", blocked.ErrorCode);
        Assert.Equal(1, await _posmDb.Ado.GetIntAsync("SELECT COUNT(*) FROM POSM_LinklyCloudDeviceSelection"));
    }

    [Fact]
    public async Task SetLinklyDeviceSelectionAsync_ActiveModeRejectsTerminalThatLostPairingReadiness()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedPosDevice("POS-01", "001");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_LinklyCloudTerminal SET PairingState = 'Ready', Secret = 'paired-secret', PosId = 'POS-1' WHERE TerminalId = @TerminalId",
            new SugarParameter("@TerminalId", terminalId)
        );
        var selected = await service.SetLinklyDeviceSelectionAsync(
            "POS-01",
            new UpdateLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                TerminalId = terminalId,
            },
            "admin"
        );
        Assert.True(selected.Success);
        Assert.True((await service.ActivateLinklyConfigurationAsync(
            new ActivateLinklyConfigurationDto { StoreCode = "001", Environment = "Production" },
            "admin")).Success);
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_LinklyCloudTerminal SET PairingState = 'NeedsRepair', Secret = NULL WHERE TerminalId = @TerminalId",
            new SugarParameter("@TerminalId", terminalId)
        );

        var result = await service.SetLinklyDeviceSelectionAsync(
            "POS-01",
            new UpdateLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                TerminalId = terminalId,
                ExpectedRevision = 1,
            },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("LINKLY_TERMINAL_NOT_READY", result.ErrorCode);
    }

    [Fact]
    public async Task ActivateLinklyConfigurationAsync_RequiresReadyTerminalAndEveryEnabledPosSelection()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedPosDevice("POS-01", "001");
        SeedPosDevice("POS-DISABLED", "001", enabled: false);
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_LinklyCloudTerminal SET PairingState = 'Ready', Secret = 'paired-secret', PosId = 'POS-1' WHERE TerminalId = @TerminalId",
            new SugarParameter("@TerminalId", terminalId)
        );

        var blocked = await service.ActivateLinklyConfigurationAsync(
            new ActivateLinklyConfigurationDto { StoreCode = "001", Environment = "Production" },
            "admin"
        );
        Assert.False(blocked.Success);
        Assert.Equal("LINKLY_DEVICE_SELECTION_REQUIRED", blocked.ErrorCode);

        var selected = await service.SetLinklyDeviceSelectionAsync(
            "POS-01",
            new UpdateLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                TerminalId = terminalId,
            },
            "admin"
        );
        var activated = await service.ActivateLinklyConfigurationAsync(
            new ActivateLinklyConfigurationDto { StoreCode = "001", Environment = "Production" },
            "admin"
        );

        Assert.True(selected.Success);
        Assert.Equal(1, selected.Data!.Devices.Single(device => device.DeviceCode == "POS-01").Revision);
        Assert.False(selected.Data.Devices.Single(device => device.DeviceCode == "POS-DISABLED").Enabled);
        Assert.True(activated.Success);
        Assert.Equal("Active", activated.Data!.Mode);
    }

    [Fact]
    public async Task ActivateLinklyConfigurationAsync_BlocksUnexpiredLegacyPairingLease()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedPosDevice("POS-01", "001");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_LinklyCloudTerminal SET PairingState = 'Ready', Secret = 'paired-secret', PosId = 'POS-1' WHERE TerminalId = @TerminalId",
            new SugarParameter("@TerminalId", terminalId)
        );
        Assert.True((await service.SetLinklyDeviceSelectionAsync(
            "POS-01",
            new UpdateLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                TerminalId = terminalId,
            },
            "admin")).Success);
        await _posmDb.Ado.ExecuteCommandAsync(
            """
            UPDATE POSM_LinklyCloudConfigurationMode
            SET LegacyPairingAttemptId = @AttemptId,
                LegacyPairingLeaseExpiresAt = @ExpiresAt
            WHERE Environment = 'Production' AND StoreCode = '001'
            """,
            new SugarParameter("@AttemptId", Guid.NewGuid()),
            new SugarParameter("@ExpiresAt", DateTime.UtcNow.AddMinutes(5))
        );

        var blocked = await service.ActivateLinklyConfigurationAsync(
            new ActivateLinklyConfigurationDto { StoreCode = "001", Environment = "Production" },
            "admin"
        );

        Assert.False(blocked.Success);
        Assert.Equal("LINKLY_CLOUD_LEGACY_PAIRING_IN_PROGRESS", blocked.ErrorCode);
        Assert.Equal(
            "Draft",
            (await service.GetLinklyTerminalManagementAsync("001", "Production")).Data!.Mode);

        await _posmDb.Ado.ExecuteCommandAsync(
            """
            UPDATE POSM_LinklyCloudConfigurationMode
            SET LegacyPairingLeaseExpiresAt = @ExpiresAt
            WHERE Environment = 'Production' AND StoreCode = '001'
            """,
            new SugarParameter("@ExpiresAt", DateTime.UtcNow.AddMinutes(-1))
        );
        var activated = await service.ActivateLinklyConfigurationAsync(
            new ActivateLinklyConfigurationDto { StoreCode = "001", Environment = "Production" },
            "admin"
        );

        Assert.True(activated.Success);
        Assert.Equal("Active", activated.Data!.Mode);
    }

    [Fact]
    public async Task ActivateLinklyConfigurationAsync_BlocksLegacyOrDraftSessionRequiringRecovery()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedPosDevice("POS-01", "001");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_LinklyCloudTerminal SET PairingState = 'Ready', Secret = 'paired-secret', PosId = 'POS-1' WHERE TerminalId = @TerminalId",
            new SugarParameter("@TerminalId", terminalId)
        );
        Assert.True((await service.SetLinklyDeviceSelectionAsync(
            "POS-01",
            new UpdateLinklyDeviceSelectionDto
            {
                StoreCode = "001",
                Environment = "Production",
                TerminalId = terminalId,
            },
            "admin")).Success);
        await SeedBlockingSessionAsync(terminalId, "POS-01", "Pending", true);

        var result = await service.ActivateLinklyConfigurationAsync(
            new ActivateLinklyConfigurationDto { StoreCode = "001", Environment = "Production" },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("LINKLY_TERMINAL_SESSION_ACTIVE", result.ErrorCode);
        Assert.Equal("Draft", (await service.GetLinklyTerminalManagementAsync("001", "Production")).Data!.Mode);
    }

    [Fact]
    public async Task ActivateLinklyConfigurationAsync_RejectsDuplicateTerminalAssignments()
    {
        var service = CreateService();
        SeedStore("001", "City Store");
        SeedPosDevice("POS-01", "001");
        SeedPosDevice("POS-02", "001");
        var created = await service.CreateLinklyTerminalAsync(
            new CreateLinklyTerminalDto
            {
                StoreCode = "001",
                Environment = "Production",
                LaneNo = 1,
                DisplayName = "Front Counter",
                Username = "test-user-001",
                Password = "lane-secret",
            },
            "admin"
        );
        var terminalId = created.Data!.Terminals.Single().TerminalId;
        await _posmDb.Ado.ExecuteCommandAsync(
            "UPDATE POSM_LinklyCloudTerminal SET PairingState = 'Ready', Secret = 'paired-secret', PosId = 'POS-1' WHERE TerminalId = @TerminalId",
            new SugarParameter("@TerminalId", terminalId)
        );
        await _posmDb.Ado.ExecuteCommandAsync(
            "INSERT INTO POSM_LinklyCloudDeviceSelection (Environment, StoreCode, DeviceCode, TerminalId, Revision, UpdatedAt) VALUES ('Production', '001', 'POS-01', @TerminalId, 1, @UpdatedAt), ('Production', '001', 'POS-02', @TerminalId, 1, @UpdatedAt)",
            new SugarParameter("@TerminalId", terminalId),
            new SugarParameter("@UpdatedAt", DateTime.UtcNow)
        );

        var result = await service.ActivateLinklyConfigurationAsync(
            new ActivateLinklyConfigurationDto { StoreCode = "001", Environment = "Production" },
            "admin"
        );

        Assert.False(result.Success);
        Assert.Equal("LINKLY_TERMINAL_ASSIGNMENT_CONFLICT", result.ErrorCode);
        Assert.Equal("Draft", (await service.GetLinklyTerminalManagementAsync("001", "Production")).Data!.Mode);
    }

    [Fact]
    public void Controller_UsesExpectedRouteAndSystemSettingsPolicy()
    {
        var controllerType = typeof(ReactPaymentTerminalSettingsController);
        var route = controllerType.GetCustomAttribute<RouteAttribute>();
        var policies = controllerType.GetMethods()
            .Where(method => method.DeclaringType == controllerType)
            .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>())
            .Select(attribute => attribute.Policy)
            .ToArray();

        Assert.Equal("api/react/v1/payment-terminal-settings", route?.Template);
        Assert.Contains(Permissions.System.ManageSettings, policies);
        Assert.All(policies, policy => Assert.Equal(Permissions.System.ManageSettings, policy));

        var httpRoutes = controllerType.GetMethods()
            .Where(method => method.DeclaringType == controllerType)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .Select(attribute => attribute.Template)
            .ToArray();
        Assert.Contains("linkly-terminals", httpRoutes);
        Assert.Contains("linkly-terminals/{terminalId:guid}", httpRoutes);
        Assert.Contains("linkly-device-selections/{deviceCode}", httpRoutes);
        Assert.Contains("linkly-activation", httpRoutes);
    }

    [Fact]
    public void Controller_maps_legacy_pairing_lease_conflict_to_http_409()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "services/backend/BlazorApp.Api/Controllers/React/ReactPaymentTerminalSettingsController.cs"
        ));

        Assert.Contains("LINKLY_CLOUD_LEGACY_PAIRING_IN_PROGRESS", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_exposes_explicit_linkly_device_selection_release_route()
    {
        var method = typeof(ReactPaymentTerminalSettingsController).GetMethod(
            "DeleteLinklyDeviceSelection"
        );

        Assert.NotNull(method);
        var route = method!.GetCustomAttribute<HttpDeleteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("linkly-device-selections/{deviceCode}", route!.Template);
        Assert.Contains(
            method.GetCustomAttributes<AuthorizeAttribute>(),
            attribute => attribute.Policy == Permissions.System.ManageSettings
        );
    }

    [Fact]
    public void LinklyTerminalMutationLock_UsesSqlServerUpdateLock()
    {
        using var sqlServerDb = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Server=localhost;Database=test;User Id=test;Password=test;TrustServerCertificate=True;",
            DbType = SqlSugar.DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });

        var sql = PaymentTerminalSettingsService.WithLinklyUpdateLock(
                sqlServerDb,
                sqlServerDb.Queryable<LinklyTerminalLockProbe>()
                    .Where(row => row.TerminalId == Guid.Empty)
            )
            .ToSql().Key;

        Assert.Contains("UPDLOCK", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "WITH (UPDLOCK, HOLDLOCK)",
            PaymentTerminalSettingsService.LinklyConfigurationModeUpdateLockSql,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void LinklyDeviceCandidateProjection_UsesSqlServerScalarColumns()
    {
        using var sqlServerDb = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Server=localhost;Database=test;User Id=test;Password=test;TrustServerCertificate=True;",
            DbType = SqlSugar.DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            MoreSettings = new ConnMoreSettings { IsWithNoLockQuery = true },
        });

        var sql = PaymentTerminalSettingsService.SelectLinklyDeviceCandidates(
                sqlServerDb.Queryable<POSM_设备注册信息表>()
                    .Where(row => row.分店代码 == "001" && row.设备类型 == "POS")
            )
            .ToSql().Key;
        var selectClause = sql[..sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase)];

        Assert.DoesNotContain(" AS [Enabled]", selectClause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[设备状态] AS [DeviceStatus]", selectClause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "[是否允许交易] AS [AllowsTransactions]",
            selectClause,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void UpdateLinklyTerminalAsync_LocksCurrentRowBeforeCalculatingAndOnlyUpdatesOwnedFields()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "services/backend/BlazorApp.Api/Services/React/PaymentTerminalSettingsService.cs"
        ));
        var methodStart = source.IndexOf(
            "public async Task<ApiResponse<LinklyTerminalManagementDto>> UpdateLinklyTerminalAsync",
            StringComparison.Ordinal
        );
        var methodEnd = source.IndexOf(
            "public async Task<ApiResponse<LinklyTerminalManagementDto>> SetLinklyDeviceSelectionAsync",
            methodStart,
            StringComparison.Ordinal
        );
        var method = source[methodStart..methodEnd];
        var transactionIndex = method.IndexOf(
            "BeginTranAsync(IsolationLevel.Serializable)",
            StringComparison.Ordinal
        );
        var lockedReadIndex = method.IndexOf("WithLinklyUpdateLock", StringComparison.Ordinal);
        var credentialChangeIndex = method.IndexOf("var credentialChanged", StringComparison.Ordinal);

        Assert.True(transactionIndex >= 0);
        Assert.True(lockedReadIndex > transactionIndex, "必须在事务开始后读取带更新锁的当前行");
        Assert.True(credentialChangeIndex > lockedReadIndex, "凭据变化必须基于加锁后的最新行计算");
        Assert.Contains(".SetColumnsIF(credentialChanged", method, StringComparison.Ordinal);
        Assert.Contains("row.UpdatedAt == existing.UpdatedAt", method, StringComparison.Ordinal);
        Assert.Contains("if (affected != 1)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Updateable(existing)", method, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _mainDb.Dispose();
        _posmDb.Dispose();
        _mainConnection.Dispose();
        _posmConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_mainDbPath);
        SqliteTempFileCleanup.DeleteIfExists(_posmDbPath);
        if (Directory.Exists(_linklyCredentialKeysPath))
        {
            Directory.Delete(_linklyCredentialKeysPath, recursive: true);
        }
    }

    private PaymentTerminalSettingsService CreateService(LinklyCredentialProtector? protector = null)
    {
        return new PaymentTerminalSettingsService(
            CreatePOSMSqlSugarContext(_posmDb),
            CreateSqlSugarContext(_mainDb),
            NullLogger<PaymentTerminalSettingsService>.Instance,
            protector ?? _linklyCredentialProtector
        );
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "services/backend/BlazorApp.Api/Program.cs"
                )))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到仓库根目录");
    }

    private void CreatePaymentTables()
    {
        _posmDb.Ado.ExecuteCommand("""
            CREATE TABLE POSM_SquareToken (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Environment TEXT NOT NULL,
                AccessToken TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UpdatedBy TEXT NULL
            );
            """);
        _posmDb.Ado.ExecuteCommand("""
            CREATE TABLE POSM_LinklyCloudCredential (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StoreCode TEXT NOT NULL,
                Environment TEXT NOT NULL,
                Username TEXT NOT NULL,
                Password TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UpdatedBy TEXT NULL
            );
            """);
        _posmDb.Ado.ExecuteCommand("""
            CREATE TABLE POSM_LinklyCloudTerminal (
                TerminalId TEXT PRIMARY KEY,
                Environment TEXT NOT NULL,
                StoreCode TEXT NOT NULL,
                LaneNo INTEGER NOT NULL,
                DisplayName TEXT NOT NULL,
                Username TEXT NOT NULL,
                Password TEXT NOT NULL,
                Secret TEXT NULL,
                CredentialProtectionVersion INTEGER NOT NULL DEFAULT 1,
                PosId TEXT NULL,
                PairingState TEXT NOT NULL DEFAULT 'Unpaired',
                PairingAttemptId TEXT NULL,
                PairingLeaseExpiresAt TEXT NULL,
                LastHealthStatus TEXT NULL,
                LastHealthAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CreatedBy TEXT NULL,
                UpdatedBy TEXT NULL
            );
            CREATE UNIQUE INDEX UX_LinklyTerminal_Lane
                ON POSM_LinklyCloudTerminal(StoreCode, Environment, LaneNo);
            CREATE UNIQUE INDEX UX_LinklyTerminal_Username
                ON POSM_LinklyCloudTerminal(StoreCode, Environment, Username);
            CREATE UNIQUE INDEX UX_LinklyTerminal_DisplayName
                ON POSM_LinklyCloudTerminal(StoreCode, Environment, DisplayName);
            """);
        _posmDb.Ado.ExecuteCommand("""
            CREATE TABLE POSM_LinklyCloudDeviceSelection (
                Environment TEXT NOT NULL,
                StoreCode TEXT NOT NULL,
                DeviceCode TEXT NOT NULL,
                TerminalId TEXT NOT NULL,
                Revision INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT NOT NULL,
                UpdatedBy TEXT NULL,
                PRIMARY KEY (Environment, StoreCode, DeviceCode)
            );
            """);
        _posmDb.Ado.ExecuteCommand("""
            CREATE TABLE POSM_LinklyCloudConfigurationMode (
                Environment TEXT NOT NULL,
                StoreCode TEXT NOT NULL,
                Mode TEXT NOT NULL DEFAULT 'Legacy',
                LegacyPairingAttemptId TEXT NULL,
                LegacyPairingLeaseExpiresAt TEXT NULL,
                UpdatedAt TEXT NOT NULL,
                UpdatedBy TEXT NULL,
                PRIMARY KEY (Environment, StoreCode)
            );
            """);
        _posmDb.Ado.ExecuteCommand("""
            CREATE TABLE POSM_LinklyCloudBackendSession (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Environment TEXT NOT NULL,
                StoreCode TEXT NOT NULL,
                DeviceCode TEXT NOT NULL,
                TerminalId TEXT NULL,
                SessionId TEXT NOT NULL,
                Status TEXT NOT NULL,
                OperationType TEXT NOT NULL DEFAULT 'Transaction',
                ClientAcknowledgedAt TEXT NULL,
                IsActive INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);
        _posmDb.Ado.ExecuteCommand("""
            CREATE TABLE POSM_设备注册信息表 (
                ID INTEGER PRIMARY KEY AUTOINCREMENT,
                系统设备编号 TEXT NOT NULL,
                分店代码 TEXT NULL,
                设备类型 TEXT NOT NULL,
                设备系统 TEXT NOT NULL DEFAULT 'Windows',
                设备状态 INTEGER NOT NULL,
                是否允许交易 INTEGER NOT NULL
            );
            """);
    }

    private void SeedStore(string storeCode, string storeName)
    {
        _mainDb.Insertable(new Store
        {
            StoreGUID = Guid.NewGuid().ToString("N"),
            StoreCode = storeCode,
            StoreName = storeName,
            IsActive = true,
        }).ExecuteCommand();
    }

    private void SeedPosDevice(string deviceCode, string storeCode, bool enabled = true)
    {
        _posmDb.Ado.ExecuteCommand(
            "INSERT INTO POSM_设备注册信息表 (系统设备编号, 分店代码, 设备类型, 设备状态, 是否允许交易) VALUES (@DeviceCode, @StoreCode, 'POS', @Status, @Allowed)",
            new SugarParameter("@DeviceCode", deviceCode),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@Status", enabled ? 1 : 0),
            new SugarParameter("@Allowed", enabled ? 1 : 0)
        );
    }

    private async Task SeedBlockingSessionAsync(
        Guid terminalId,
        string deviceCode,
        string status,
        bool isActive
    )
    {
        await _posmDb.Ado.ExecuteCommandAsync(
            """
            INSERT INTO POSM_LinklyCloudBackendSession
                (Environment, StoreCode, DeviceCode, TerminalId, SessionId, Status, OperationType, ClientAcknowledgedAt, IsActive, UpdatedAt)
            VALUES
                ('Production', '001', @DeviceCode, @TerminalId, @SessionId, @Status, 'Transaction', NULL, @IsActive, @UpdatedAt)
            """,
            new SugarParameter("@DeviceCode", deviceCode),
            new SugarParameter("@TerminalId", terminalId),
            new SugarParameter("@SessionId", $"SESSION-{Guid.NewGuid():N}"),
            new SugarParameter("@Status", status),
            new SugarParameter("@IsActive", isActive ? 1 : 0),
            new SugarParameter("@UpdatedAt", DateTime.UtcNow)
        );
    }

    private Task<List<SquareRow>> QuerySquareRowsAsync(string environment)
    {
        return _posmDb.Ado.SqlQueryAsync<SquareRow>(
            """
            SELECT Id, Environment, AccessToken, IsEnabled, UpdatedAt, UpdatedBy
            FROM POSM_SquareToken
            WHERE Environment = @Environment
            ORDER BY Id
            """,
            new SugarParameter("@Environment", environment)
        );
    }

    private Task<List<LinklyRow>> QueryLinklyRowsAsync(string storeCode, string environment)
    {
        return _posmDb.Ado.SqlQueryAsync<LinklyRow>(
            """
            SELECT Id, StoreCode, Environment, Username, Password, UpdatedAt, UpdatedBy
            FROM POSM_LinklyCloudCredential
            WHERE StoreCode = @StoreCode AND Environment = @Environment
            ORDER BY Id
            """,
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@Environment", environment)
        );
    }

    private Task<List<LinklyTerminalRow>> QueryLinklyTerminalRowsAsync(string storeCode, string environment)
    {
        return _posmDb.Ado.SqlQueryAsync<LinklyTerminalRow>(
            """
            SELECT TerminalId, Environment, StoreCode, LaneNo, DisplayName, Username, Password,
                   CredentialProtectionVersion,
                   Secret, PosId, PairingState, LastHealthStatus, LastHealthAt, CreatedAt, UpdatedAt,
                   CreatedBy, UpdatedBy
            FROM POSM_LinklyCloudTerminal
            WHERE StoreCode = @StoreCode AND Environment = @Environment
            ORDER BY LaneNo
            """,
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@Environment", environment)
        );
    }

    private static SqlSugarClient CreateDb(string connectionString) =>
        new(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    private static POSMSqlSugarContext CreatePOSMSqlSugarContext(ISqlSugarClient db)
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
        return context;
    }

    private sealed class SquareRow
    {
        public long Id { get; set; }
        public string Environment { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    private sealed class LinklyRow
    {
        public long Id { get; set; }
        public string StoreCode { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    private sealed class LinklyTerminalRow
    {
        public Guid TerminalId { get; set; }
        public string Environment { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public int LaneNo { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public byte CredentialProtectionVersion { get; set; }
        public string? Secret { get; set; }
        public string? PosId { get; set; }
        public string PairingState { get; set; } = string.Empty;
        public string? LastHealthStatus { get; set; }
        public DateTime? LastHealthAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }

    [SugarTable("POSM_LinklyCloudTerminal")]
    private sealed class LinklyTerminalLockProbe
    {
        public Guid TerminalId { get; set; }
    }

    private sealed class ThrowingLinklyCredentialProtector : LinklyCredentialProtector
    {
        public string ProtectPassword(string password) => throw new InvalidOperationException("sentinel");
        public string UnprotectPassword(string protectedPassword) => throw new InvalidOperationException("sentinel");
        public string ProtectSecret(string secret) => throw new InvalidOperationException("sentinel");
        public string UnprotectSecret(string protectedSecret) => throw new InvalidOperationException("sentinel");
    }
}
