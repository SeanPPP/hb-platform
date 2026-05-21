using BlazorApp.Shared.Models;
using Hbpos.Api.Data;
using Hbpos.Contracts.Cashiers;

namespace Hbpos.Api.Services;

public interface ICashierService
{
    Task<CashierSessionDto?> BarcodeLoginAsync(
        CashierBarcodeLoginRequest request,
        CancellationToken cancellationToken);
}

public sealed class CashierService(HbposSqlSugarContext dbContext) : ICashierService
{
    public async Task<CashierSessionDto?> BarcodeLoginAsync(
        CashierBarcodeLoginRequest request,
        CancellationToken cancellationToken)
    {
        var cashier = await dbContext.MainDb.Queryable<CashRegisterUser>()
            .FirstAsync(
                x => x.StoreCode == request.StoreCode
                    && x.UserBarcode == request.UserBarcode
                    && x.Status,
                cancellationToken);

        if (cashier is null)
        {
            return null;
        }

        return new CashierSessionDto(
            string.IsNullOrWhiteSpace(cashier.HGUID) ? cashier.Id.ToString() : cashier.HGUID,
            cashier.OperatorUser,
            cashier.StoreCode,
            request.DeviceCode,
            SplitRoles(cashier.LoginRole));
    }

    private static string[] SplitRoles(string? roles)
    {
        if (string.IsNullOrWhiteSpace(roles))
        {
            return [];
        }

        return roles
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
