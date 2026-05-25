using BlazorApp.Service.Models.HBPOSM_POSM;
using Hbpos.Api.Services;
using Hbpos.Contracts.Vouchers;

namespace Hbpos.Api.Tests;

public sealed class StoreVoucherServiceTests
{
    [Fact]
    public async Task QueryAsync_ReturnsFoundVoucher()
    {
        var voucher = CreateVoucher(remainingAmount: 12.5m);
        var service = new StoreVoucherService(
            new FakeStoreVoucherRepository(voucher),
            new InMemoryStoreVoucherReservationService(new FakeTimeProvider(DateTimeOffset.Parse("2026-05-26T10:00:00Z"))));

        var response = await service.QueryAsync("S01", "V001", CancellationToken.None);

        Assert.True(response.Found);
        Assert.NotNull(response.Voucher);
        Assert.Equal("V001", response.Voucher.VoucherCode);
        Assert.Equal(12.5m, response.Voucher.RemainingAmount);
    }

    [Fact]
    public async Task LockAsync_ReturnsPartialAmountWhenExistingReservationReducesAvailability()
    {
        var reservationService = new InMemoryStoreVoucherReservationService(new FakeTimeProvider(DateTimeOffset.Parse("2026-05-26T10:00:00Z")));
        var service = new StoreVoucherService(
            new FakeStoreVoucherRepository(CreateVoucher(remainingAmount: 10m)),
            reservationService);

        var first = await service.LockAsync(new StoreVoucherLockRequest("S01", "V001", 6m), CancellationToken.None);
        var second = await service.LockAsync(new StoreVoucherLockRequest("S01", "V001", 6m), CancellationToken.None);

        Assert.Equal(6m, first.LockedAmount);
        Assert.Equal(4m, second.LockedAmount);
        Assert.NotEqual(first.ReservationToken, second.ReservationToken);
    }

    private static StoreVoucher CreateVoucher(decimal remainingAmount)
    {
        return new StoreVoucher
        {
            StoreCode = "S01",
            VoucherCode = "V001",
            VoucherType = 3,
            Amount = 20m,
            RemainingAmount = remainingAmount,
            Status = "1",
            ExpiredDate = DateTime.UtcNow.AddDays(3),
            CustomerCode = "C01",
            DiscountRate = 0m,
            Remark = "cash voucher",
            IsDelete = false
        };
    }

    private sealed class FakeStoreVoucherRepository(StoreVoucher? voucher) : IStoreVoucherRepository
    {
        public Task<StoreVoucher?> FindAvailableAsync(
            string storeCode,
            string voucherCode,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(voucher);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
