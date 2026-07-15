using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class CashierBarcodeSyncGuardTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SqlSugarClient _db;

    public CashierBarcodeSyncGuardTests()
    {
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(CashRegisterUser),
            typeof(CashierBarcodeReservation),
            typeof(EmployeeCashierBarcode)
        );
    }

    [Fact]
    public async Task ValidateAndReserveHqBatchAsync_预占新条码并拒绝个人条码冲突()
    {
        await _db.Insertable(new EmployeeCashierBarcode
        {
            HGUID = "employee-1",
            UserGUID = "employee-user",
            Barcode = "EMPLOYEE-CODE",
            Status = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        await _db.Insertable(new CashierBarcodeReservation
        {
            Barcode = "EMPLOYEE-CODE",
            OwnerType = "employee",
            OwnerId = "employee-1",
            CreatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        await CashierBarcodeSyncGuard.ValidateAndReserveHqBatchAsync(_db,
        [
            new CashRegisterUser
            {
                HGUID = "legacy-new",
                UserGUID = "legacy-user",
                UserBarcode = "LEGACY-CODE",
                Status = true,
            },
        ]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CashierBarcodeSyncGuard.ValidateAndReserveHqBatchAsync(_db,
            [
                new CashRegisterUser
                {
                    HGUID = "legacy-conflict",
                    UserGUID = "other-user",
                    UserBarcode = "EMPLOYEE-CODE",
                    Status = true,
                },
            ]));

        var reservation = await _db.Queryable<CashierBarcodeReservation>()
            .FirstAsync(item => item.Barcode == "LEGACY-CODE");
        Assert.Equal("legacy", reservation.OwnerType);
        Assert.Equal("legacy-new", reservation.OwnerId);
    }

    [Fact]
    public async Task ValidateAndReserveHqBatchAsync_拒绝为已有个人条码的用户导入有效Legacy条码()
    {
        await _db.Insertable(new EmployeeCashierBarcode
        {
            HGUID = "employee-1",
            UserGUID = "same-user",
            Barcode = "EMPLOYEE-CODE",
            Status = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CashierBarcodeSyncGuard.ValidateAndReserveHqBatchAsync(_db,
            [
                new CashRegisterUser
                {
                    HGUID = "legacy-new",
                    UserGUID = "same-user",
                    UserBarcode = "LEGACY-CODE",
                    Status = true,
                },
            ]));

        Assert.Contains("同一用户", error.Message);
        Assert.False(await _db.Queryable<CashierBarcodeReservation>()
            .AnyAsync(item => item.Barcode == "LEGACY-CODE"));
    }

    [Fact]
    public async Task ValidateAndReserveHqBatchAsync_同Owner重复HQ行只占位一次()
    {
        var duplicate = new CashRegisterUser
        {
            HGUID = "legacy-same",
            UserGUID = "legacy-user",
            UserBarcode = "SAME-CODE",
            Status = false,
        };

        await CashierBarcodeSyncGuard.ValidateAndReserveHqBatchAsync(
            _db,
            [duplicate, new CashRegisterUser
            {
                HGUID = "legacy-same",
                UserGUID = "legacy-user",
                UserBarcode = "SAME-CODE",
                Status = false,
            }]
        );

        Assert.Equal(1, await _db.Queryable<CashierBarcodeReservation>()
            .CountAsync(item => item.Barcode == "SAME-CODE"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
