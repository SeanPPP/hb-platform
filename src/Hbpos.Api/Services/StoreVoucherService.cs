using System.Collections.Concurrent;
using BlazorApp.Service.Models.HBPOSM_POSM;
using Hbpos.Api.Data;
using Hbpos.Contracts.Vouchers;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface IStoreVoucherService
{
    Task<StoreVoucherQueryResponse> QueryAsync(
        string storeCode,
        string voucherCode,
        CancellationToken cancellationToken);

    Task<StoreVoucherLockResponse> LockAsync(
        StoreVoucherLockRequest request,
        CancellationToken cancellationToken);
}

public sealed class StoreVoucherService(
    IStoreVoucherRepository repository,
    IStoreVoucherReservationService reservationService) : IStoreVoucherService
{
    public async Task<StoreVoucherQueryResponse> QueryAsync(
        string storeCode,
        string voucherCode,
        CancellationToken cancellationToken)
    {
        var normalizedStoreCode = NormalizeRequired(storeCode, nameof(storeCode));
        var normalizedVoucherCode = NormalizeRequired(voucherCode, nameof(voucherCode));
        var voucher = await repository.FindAvailableAsync(normalizedStoreCode, normalizedVoucherCode, cancellationToken);

        return voucher is null
            ? new StoreVoucherQueryResponse(false, null, "VoucherNotFound")
            : new StoreVoucherQueryResponse(true, Map(voucher));
    }

    public async Task<StoreVoucherLockResponse> LockAsync(
        StoreVoucherLockRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedStoreCode = NormalizeRequired(request.StoreCode, nameof(request.StoreCode));
        var normalizedVoucherCode = NormalizeRequired(request.VoucherCode, nameof(request.VoucherCode));
        if (request.RequestedAmount <= 0)
        {
            throw new InvalidOperationException("Requested amount must be greater than zero.");
        }

        var voucher = await repository.FindAvailableAsync(normalizedStoreCode, normalizedVoucherCode, cancellationToken)
            ?? throw new InvalidOperationException("Voucher is unavailable.");
        var reservation = await reservationService.ReserveAsync(
            normalizedStoreCode,
            normalizedVoucherCode,
            request.RequestedAmount,
            voucher.RemainingAmount ?? 0m,
            cancellationToken);

        return new StoreVoucherLockResponse(
            normalizedVoucherCode,
            reservation.LockedAmount,
            reservation.Token,
            reservation.ExpiresAt);
    }

    private static StoreVoucherDto Map(StoreVoucher voucher)
    {
        return new StoreVoucherDto(
            voucher.VoucherCode ?? string.Empty,
            voucher.StoreCode,
            voucher.VoucherType ?? 0,
            voucher.Amount ?? 0m,
            voucher.RemainingAmount ?? 0m,
            voucher.Status ?? string.Empty,
            voucher.ExpiredDate is null
                ? null
                : DateTime.SpecifyKind(voucher.ExpiredDate.Value, DateTimeKind.Utc),
            voucher.CustomerCode,
            voucher.DiscountRate ?? 0m,
            voucher.Remark);
    }

    private static string NormalizeRequired(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{paramName} is required.");
        }

        return value.Trim();
    }
}

public interface IStoreVoucherRepository
{
    Task<StoreVoucher?> FindAvailableAsync(
        string storeCode,
        string voucherCode,
        CancellationToken cancellationToken);
}

public sealed class SqlSugarStoreVoucherRepository(HbposSqlSugarContext dbContext) : IStoreVoucherRepository
{
    public async Task<StoreVoucher?> FindAvailableAsync(
        string storeCode,
        string voucherCode,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var voucher = await dbContext.PosmDb.Queryable<StoreVoucher>()
            .Where(x => x.VoucherCode == voucherCode)
            .Where(x => x.Status == "1")
            .Where(x => x.IsDelete == null || x.IsDelete == false)
            .Where(x => x.RemainingAmount != null && x.RemainingAmount > 0)
            .Where(x => x.ExpiredDate == null || x.ExpiredDate > now)
            .Where(x => x.StoreCode == null || x.StoreCode == string.Empty || x.StoreCode == storeCode)
            .OrderBy(x => x.StoreCode == storeCode, OrderByType.Desc)
            .FirstAsync(cancellationToken);
        return voucher;
    }
}

public interface IStoreVoucherReservationService
{
    Task<StoreVoucherReservation?> GetAsync(string token, CancellationToken cancellationToken);

    Task<StoreVoucherReservation> ReserveAsync(
        string storeCode,
        string voucherCode,
        decimal requestedAmount,
        decimal currentRemainingAmount,
        CancellationToken cancellationToken);

    Task ConsumeAsync(string token, CancellationToken cancellationToken);
}

public sealed record StoreVoucherReservation(
    string Token,
    string StoreCode,
    string VoucherCode,
    decimal LockedAmount,
    DateTimeOffset ExpiresAt);

public sealed class InMemoryStoreVoucherReservationService(TimeProvider timeProvider) : IStoreVoucherReservationService
{
    private static readonly TimeSpan ReservationLifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, StoreVoucherReservation> reservations = new(StringComparer.OrdinalIgnoreCase);

    public Task<StoreVoucherReservation?> GetAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PruneExpiredReservations();

        return Task.FromResult(reservations.TryGetValue(token, out var reservation) ? reservation : null);
    }

    public Task<StoreVoucherReservation> ReserveAsync(
        string storeCode,
        string voucherCode,
        decimal requestedAmount,
        decimal currentRemainingAmount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PruneExpiredReservations();

        var reservedAmount = reservations.Values
            .Where(x =>
                string.Equals(x.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.VoucherCode, voucherCode, StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.LockedAmount);
        var lockableAmount = Math.Min(requestedAmount, Math.Max(0m, currentRemainingAmount - reservedAmount));
        if (lockableAmount <= 0)
        {
            throw new InvalidOperationException("Voucher has no remaining amount available to lock.");
        }

        var reservation = new StoreVoucherReservation(
            Guid.NewGuid().ToString("N"),
            storeCode,
            voucherCode,
            lockableAmount,
            timeProvider.GetUtcNow().Add(ReservationLifetime));
        reservations[reservation.Token] = reservation;
        return Task.FromResult(reservation);
    }

    public Task ConsumeAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        reservations.TryRemove(token, out _);
        return Task.CompletedTask;
    }

    private void PruneExpiredReservations()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var pair in reservations)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                reservations.TryRemove(pair.Key, out _);
            }
        }
    }
}
