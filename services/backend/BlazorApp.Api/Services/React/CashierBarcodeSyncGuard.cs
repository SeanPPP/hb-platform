using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

internal static class CashierBarcodeSyncGuard
{
    internal static async Task ValidateAndReserveHqBatchAsync(
        ISqlSugarClient db,
        IReadOnlyCollection<CashRegisterUser> batch)
    {
        var cashiers = batch
            .Where(item => !string.IsNullOrWhiteSpace(item.UserBarcode))
            .Select(item =>
            {
                item.UserBarcode = item.UserBarcode.Trim();
                return item;
            })
            .ToList();
        if (cashiers.Count == 0)
        {
            return;
        }

        var duplicateOwner = cashiers
            .GroupBy(item => item.UserBarcode, StringComparer.Ordinal)
            .FirstOrDefault(group => group
                .Select(item => item.HGUID?.Trim())
                .Distinct(StringComparer.Ordinal)
                .Count() > 1);
        if (duplicateOwner is not null)
        {
            throw new InvalidOperationException($"HQ 收银条码被多个 legacy 记录占用：{duplicateOwner.Key}");
        }

        var activeUserGuids = cashiers
            .Where(item => item.Status && !string.IsNullOrWhiteSpace(item.UserGUID))
            .Select(item => item.UserGUID!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var userChunk in activeUserGuids.Chunk(500))
        {
            var users = userChunk.ToList();
            var employeeUserConflict = await db.Queryable<EmployeeCashierBarcode>()
                .FirstAsync(item => item.Status && users.Contains(item.UserGUID));
            if (employeeUserConflict is not null)
            {
                throw new InvalidOperationException(
                    $"HQ 有效 legacy 条码与同一用户的个人条码冲突：{employeeUserConflict.UserGUID}"
                );
            }
        }

        foreach (var cashierChunk in cashiers.Chunk(500))
        {
            var items = cashierChunk
                .GroupBy(item => item.UserBarcode, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            var barcodes = items.Select(item => item.UserBarcode)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var employeeBarcodeConflict = await db.Queryable<EmployeeCashierBarcode>()
                .FirstAsync(item => barcodes.Contains(item.Barcode));
            if (employeeBarcodeConflict is not null)
            {
                throw new InvalidOperationException(
                    $"HQ 收银条码与员工个人条码冲突：{employeeBarcodeConflict.Barcode}"
                );
            }

            var reservations = await db.Queryable<CashierBarcodeReservation>()
                .Where(item => barcodes.Contains(item.Barcode))
                .ToListAsync();
            var ownersByBarcode = items.ToDictionary(
                item => item.UserBarcode,
                item => item.HGUID?.Trim() ?? string.Empty,
                StringComparer.Ordinal
            );
            foreach (var reservation in reservations)
            {
                if (!string.Equals(reservation.OwnerType, "legacy", StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(reservation.OwnerId)
                        && !string.Equals(
                            reservation.OwnerId.Trim(),
                            ownersByBarcode[reservation.Barcode],
                            StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        $"HQ 收银条码与已有占用记录冲突：{reservation.Barcode}"
                    );
                }
            }

            var reserved = reservations.Select(item => item.Barcode)
                .ToHashSet(StringComparer.Ordinal);
            var missing = items
                .Where(item => !reserved.Contains(item.UserBarcode))
                .Select(item => new CashierBarcodeReservation
                {
                    Barcode = item.UserBarcode,
                    CreatedAt = DateTime.UtcNow,
                    OwnerType = "legacy",
                    OwnerId = item.HGUID,
                })
                .ToList();
            if (missing.Count > 0)
            {
                await db.Insertable(missing).ExecuteCommandAsync();
            }
        }
    }
}
