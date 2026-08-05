using System.Reflection;
using System.Runtime.CompilerServices;
using Hbpos.Api.Data;
using Hbpos.Api.Services;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;
using SqlSugar;

namespace Hbpos.Api.Tests;

public sealed class InstallmentMutationLockTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-04T02:00:00Z");

    [Fact]
    public async Task Claim_create_and_void_race_allows_exactly_one_mutation()
    {
        await using var fixture = new MutationFixture();
        var claim = fixture.CreateClaim();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var createTask = Task.Run(async () =>
        {
            await start.Task;
            return await fixture.Claims.TryInsertAsync(claim, fixture.Snapshot, CancellationToken.None);
        });
        var voidTask = Task.Run(async () =>
        {
            await start.Task;
            try
            {
                await fixture.Installments.VoidAsync(
                    fixture.InstallmentGuid,
                    new InstallmentCancellationInfoDto(
                        InstallmentCancellationKind.VoidCancel,
                        Now,
                        "Cashier One",
                        IdempotencyKey: "void-race"),
                    CancellationToken.None);
                return true;
            }
            catch (InstallmentRepaymentClaimException ex)
                when (ex.Code == InstallmentRepaymentClaimErrorCodes.Busy)
            {
                return false;
            }
        });

        start.SetResult(true);
        var claimWon = await createTask;
        var voidWon = await voidTask;

        Assert.NotEqual(claimWon, voidWon);
        var state = await fixture.ReadStateAsync(claim.OperationGuid, claim.PaymentGuid);
        Assert.Equal(claimWon, state.Claim is { IsBlocking: true });
        Assert.Equal(voidWon ? InstallmentStatus.Cancelled : InstallmentStatus.Active, (InstallmentStatus)state.Order.Status);
    }

    [Fact]
    public async Task Claim_create_and_legacy_append_race_allows_exactly_one_payment_path()
    {
        await using var fixture = new MutationFixture();
        var claim = fixture.CreateClaim();
        var legacyPaymentGuid = Guid.NewGuid();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var createTask = Task.Run(async () =>
        {
            await start.Task;
            return await fixture.Claims.TryInsertAsync(claim, fixture.Snapshot, CancellationToken.None);
        });
        var appendTask = Task.Run(async () =>
        {
            await start.Task;
            try
            {
                await fixture.Installments.AppendPaymentAsync(
                    fixture.InstallmentGuid,
                    new InstallmentPaymentDto(
                        legacyPaymentGuid,
                        PaymentMethodKind.Card,
                        10m,
                        "LEGACY",
                        InstallmentPaymentStatus.Recorded,
                        Now,
                        "C01",
                        "POS-01",
                        IdempotencyKey: "legacy-race",
                        CashierName: "Cashier One"),
                    CancellationToken.None);
                return true;
            }
            catch (InstallmentRepaymentClaimException ex)
                when (ex.Code == InstallmentRepaymentClaimErrorCodes.Busy)
            {
                return false;
            }
        });

        start.SetResult(true);
        var claimWon = await createTask;
        var appendWon = await appendTask;

        Assert.NotEqual(claimWon, appendWon);
        var state = await fixture.ReadStateAsync(claim.OperationGuid, legacyPaymentGuid);
        Assert.Equal(claimWon, state.Claim is { IsBlocking: true });
        Assert.Equal(appendWon ? 1 : 0, state.PaymentCount);
        Assert.Equal(appendWon ? 30m : 20m, state.Order.PaidAmount);
    }

    [Fact]
    public async Task Expired_prepared_claim_is_released_inside_the_same_lock_before_lifecycle_check()
    {
        await using var fixture = new MutationFixture();
        var claim = fixture.CreateClaim(
            InstallmentRepaymentClaimStatus.Prepared,
            expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        await fixture.InsertClaimDirectAsync(claim);

        await fixture.Installments.VoidAsync(
            fixture.InstallmentGuid,
            new InstallmentCancellationInfoDto(
                InstallmentCancellationKind.VoidCancel,
                Now,
                "Cashier One",
                IdempotencyKey: "void-after-expiry"),
            CancellationToken.None);

        var state = await fixture.ReadStateAsync(claim.OperationGuid, claim.PaymentGuid);
        Assert.Equal(InstallmentStatus.Cancelled, (InstallmentStatus)state.Order.Status);
        Assert.Equal(InstallmentRepaymentClaimStatus.Released.ToString(), state.Claim?.Status);
        Assert.False(state.Claim?.IsBlocking);
    }

    [Theory]
    [InlineData(InstallmentRepaymentClaimStatus.ProviderPending)]
    [InlineData(InstallmentRepaymentClaimStatus.Unknown)]
    public async Task Provider_pending_and_unknown_claims_never_auto_expire_under_lifecycle_lock(
        InstallmentRepaymentClaimStatus status)
    {
        await using var fixture = new MutationFixture();
        var claim = fixture.CreateClaim(status, expiresAtUtc: DateTimeOffset.UtcNow.AddDays(-30));
        await fixture.InsertClaimDirectAsync(claim);

        var exception = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            fixture.Installments.VoidAsync(
                fixture.InstallmentGuid,
                new InstallmentCancellationInfoDto(
                    InstallmentCancellationKind.VoidCancel,
                    Now,
                    "Cashier One",
                    IdempotencyKey: $"blocked-{status}"),
                CancellationToken.None));

        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Busy, exception.Code);
        var state = await fixture.ReadStateAsync(claim.OperationGuid, claim.PaymentGuid);
        Assert.Equal(InstallmentStatus.Active, (InstallmentStatus)state.Order.Status);
        Assert.Equal(status.ToString(), state.Claim?.Status);
        Assert.True(state.Claim?.IsBlocking);
    }

    [Theory]
    [InlineData(InstallmentCancelClaimStatus.RefundPending)]
    [InlineData(InstallmentCancelClaimStatus.Unknown)]
    public async Task Cancel_claim_blocks_repayment_create_across_the_independent_tables(
        InstallmentCancelClaimStatus status)
    {
        await using var fixture = new MutationFixture();
        var cancelOperationGuid = await fixture.InsertCancelClaimAsync(status);

        var inserted = await fixture.Claims.TryInsertAsync(
            fixture.CreateClaim(),
            fixture.Snapshot,
            CancellationToken.None);

        Assert.False(inserted);
        var cancel = await fixture.ReadCancelClaimAsync(cancelOperationGuid);
        Assert.Equal(status.ToString(), cancel.Status);
        Assert.True(cancel.IsBlocking);
    }

    [Fact]
    public async Task Expired_cancel_prepared_is_released_inside_the_shared_lock_before_repayment_create()
    {
        await using var fixture = new MutationFixture();
        var cancelOperationGuid = await fixture.InsertCancelClaimAsync(
            InstallmentCancelClaimStatus.Prepared,
            DateTimeOffset.UtcNow.AddMinutes(-1));

        var inserted = await fixture.Claims.TryInsertAsync(
            fixture.CreateClaim(),
            fixture.Snapshot,
            CancellationToken.None);

        Assert.True(inserted);
        var cancel = await fixture.ReadCancelClaimAsync(cancelOperationGuid);
        Assert.Equal(InstallmentCancelClaimStatus.Released.ToString(), cancel.Status);
        Assert.False(cancel.IsBlocking);
    }

    [Fact]
    public async Task Repayment_and_cancel_claim_create_race_allows_exactly_one_blocking_claim()
    {
        await using var fixture = new MutationFixture();
        var repayment = fixture.CreateClaim();
        var cancellation = await fixture.CreateCancelClaimAsync();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repaymentTask = Task.Run(async () =>
        {
            await start.Task;
            return await fixture.Claims.TryInsertAsync(repayment, fixture.Snapshot, CancellationToken.None);
        });
        var cancelTask = Task.Run(async () =>
        {
            await start.Task;
            return await fixture.CancelClaims.TryInsertAsync(cancellation, CancellationToken.None);
        });

        start.SetResult(true);
        var repaymentWon = await repaymentTask;
        var cancelWon = await cancelTask;

        Assert.NotEqual(repaymentWon, cancelWon);
    }

    private sealed class MutationFixture : IAsyncDisposable
    {
        private readonly string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"hbpos-installment-mutation-lock-{Guid.NewGuid():N}.db");
        private readonly SqlSugarClient client;

        public MutationFixture()
        {
            InstallmentGuid = Guid.NewGuid();
            client = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = $"Data Source={databasePath}",
                DbType = DbType.Sqlite,
                InitKeyType = InitKeyType.Attribute,
                IsAutoCloseConnection = true
            });
            client.CodeFirst.InitTables<
                InstallmentOrderEntity,
                InstallmentOrderLineEntity,
                InstallmentPaymentEntity,
                InstallmentRepaymentClaimEntity,
                InstallmentCancelClaimEntity>();
            var context = CreateDbContext(client);
            Installments = new SqlSugarInstallmentRepository(context);
            Claims = new SqlSugarInstallmentRepaymentClaimRepository(context);
            CancelClaims = new SqlSugarInstallmentCancelClaimRepository(context, Installments);
            Snapshot = new InstallmentRepaymentClaimInsertSnapshot(
                InstallmentStatus.Active,
                PaidAmount: 20m,
                BalanceAmount: 80m);
            SeedOrder();
        }

        public Guid InstallmentGuid { get; }

        public SqlSugarInstallmentRepaymentClaimRepository Claims { get; }

        public SqlSugarInstallmentCancelClaimRepository CancelClaims { get; }

        public SqlSugarInstallmentRepository Installments { get; }

        public InstallmentRepaymentClaimInsertSnapshot Snapshot { get; }

        public InstallmentRepaymentClaimRecord CreateClaim(
            InstallmentRepaymentClaimStatus status = InstallmentRepaymentClaimStatus.Prepared,
            DateTimeOffset? expiresAtUtc = null)
        {
            return new InstallmentRepaymentClaimRecord(
                InstallmentGuid,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "S01",
                "POS-01",
                "C01",
                "Cashier One",
                10m,
                PaymentMethodKind.Card,
                $"claim-{Guid.NewGuid():N}",
                new string('b', 64),
                status,
                status == InstallmentRepaymentClaimStatus.Prepared ? null : "linkly",
                status == InstallmentRepaymentClaimStatus.Prepared ? null : "attempt-lock",
                Now.AddMinutes(-2),
                Now.AddMinutes(-2),
                expiresAtUtc ?? (status == InstallmentRepaymentClaimStatus.Prepared
                    ? DateTimeOffset.UtcNow.AddMinutes(2)
                    : null),
                null,
                1);
        }

        public async Task<InstallmentCancelClaimRecord> CreateCancelClaimAsync()
        {
            var details = await Installments.GetDetailsAsync(InstallmentGuid, CancellationToken.None);
            Assert.NotNull(details);
            return new InstallmentCancelClaimRecord(
                InstallmentGuid,
                Guid.NewGuid(),
                "S01",
                "POS-01",
                "C01",
                "Cashier One",
                $"cancel-{Guid.NewGuid():N}",
                null,
                InstallmentCancelClaimFingerprint.Create(details!),
                InstallmentCancelClaimStatus.Prepared,
                Now,
                Now,
                DateTimeOffset.UtcNow.AddSeconds(120),
                null,
                1);
        }

        public Task<int> InsertClaimDirectAsync(InstallmentRepaymentClaimRecord claim)
        {
            return client.Insertable(new InstallmentRepaymentClaimEntity
            {
                InstallmentGuid = claim.InstallmentGuid,
                OperationGuid = claim.OperationGuid,
                PaymentGuid = claim.PaymentGuid,
                StoreCode = claim.StoreCode,
                ClaimantDeviceCode = claim.ClaimantDeviceCode,
                CashierId = claim.CashierId,
                CashierName = claim.CashierName,
                Amount = claim.Amount,
                Method = (int)claim.Method,
                IdempotencyKey = claim.IdempotencyKey,
                Fingerprint = claim.Fingerprint,
                Status = claim.Status.ToString(),
                IsBlocking = true,
                Provider = claim.Provider,
                ProviderAttemptId = claim.ProviderAttemptId,
                CreatedAtUtc = claim.CreatedAtUtc.UtcDateTime,
                UpdatedAtUtc = claim.UpdatedAtUtc.UtcDateTime,
                ExpiresAtUtc = claim.ExpiresAtUtc?.UtcDateTime,
                Revision = claim.Revision
            }).ExecuteCommandAsync();
        }

        public async Task<Guid> InsertCancelClaimAsync(
            InstallmentCancelClaimStatus status,
            DateTimeOffset? expiresAtUtc = null)
        {
            var operationGuid = Guid.NewGuid();
            await client.Insertable(new InstallmentCancelClaimEntity
            {
                InstallmentGuid = InstallmentGuid,
                OperationGuid = operationGuid,
                StoreCode = "S01",
                ClaimantDeviceCode = "POS-01",
                CashierId = "C01",
                CashierName = "Cashier One",
                IdempotencyKey = $"cancel-{Guid.NewGuid():N}",
                RefundPlanFingerprint = $"sha256:{new string('a', 64)}",
                Status = status.ToString(),
                IsBlocking = InstallmentCancelClaimRecord.IsBlocking(status),
                CreatedAtUtc = Now.AddMinutes(-2).UtcDateTime,
                UpdatedAtUtc = Now.AddMinutes(-2).UtcDateTime,
                ExpiresAtUtc = expiresAtUtc?.UtcDateTime,
                Revision = 1
            }).ExecuteCommandAsync();
            return operationGuid;
        }

        public Task<InstallmentCancelClaimEntity> ReadCancelClaimAsync(Guid operationGuid) =>
            client.Queryable<InstallmentCancelClaimEntity>()
                .FirstAsync(entity => entity.OperationGuid == operationGuid);

        public async Task<MutationState> ReadStateAsync(Guid operationGuid, Guid paymentGuid)
        {
            var installmentGuidText = InstallmentGuid.ToString("D");
            var paymentGuidText = paymentGuid.ToString("D");
            var order = await client.Queryable<InstallmentOrderEntity>()
                .FirstAsync(entity => entity.InstallmentGuid == installmentGuidText);
            var claim = await client.Queryable<InstallmentRepaymentClaimEntity>()
                .FirstAsync(entity => entity.OperationGuid == operationGuid);
            var paymentCount = await client.Queryable<InstallmentPaymentEntity>()
                .Where(entity => entity.PaymentGuid == paymentGuidText)
                .CountAsync();
            return new MutationState(order, claim, paymentCount);
        }

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            if (File.Exists(databasePath))
            {
                try
                {
                    File.Delete(databasePath);
                }
                catch (IOException)
                {
                    // SQLite 可能短暂占用临时数据库，不影响测试断言。
                }
            }

            return ValueTask.CompletedTask;
        }

        private void SeedOrder()
        {
            client.Insertable(new InstallmentOrderEntity
            {
                InstallmentGuid = InstallmentGuid.ToString("D"),
                InstallmentNumber = "INS-LOCK-001",
                StoreCode = "S01",
                DeviceCode = "POS-01",
                CashierId = "C01",
                CashierName = "Cashier One",
                CustomerName = "Lock Customer",
                CustomerPhone = "0400000000",
                TotalAmount = 100m,
                MinimumDownPayment = 20m,
                DownPaymentAmount = 20m,
                PaidAmount = 20m,
                BalanceAmount = 80m,
                Status = (int)InstallmentStatus.Active,
                CreatedAt = Now.AddDays(-1).UtcDateTime,
                UpdatedAt = Now.AddDays(-1).UtcDateTime
            }).ExecuteCommand();
            client.Insertable(new InstallmentPaymentEntity
            {
                PaymentGuid = Guid.NewGuid().ToString("D"),
                InstallmentGuid = InstallmentGuid.ToString("D"),
                Method = (int)PaymentMethodKind.Cash,
                Amount = 20m,
                Status = (int)InstallmentPaymentStatus.Recorded,
                RecordedAt = Now.AddDays(-1).UtcDateTime,
                CashierId = "C01",
                CashierName = "Cashier One",
                DeviceCode = "POS-01",
                IdempotencyKey = "down-payment"
            }).ExecuteCommand();
        }

        private static HbposSqlSugarContext CreateDbContext(ISqlSugarClient posmDb)
        {
            var context = (HbposSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HbposSqlSugarContext));
            SetAutoProperty(context, nameof(HbposSqlSugarContext.MainDb), posmDb);
            SetAutoProperty(context, nameof(HbposSqlSugarContext.PosmDb), posmDb);
            return context;
        }

        private static void SetAutoProperty(HbposSqlSugarContext context, string propertyName, ISqlSugarClient value)
        {
            var backingField = typeof(HbposSqlSugarContext).GetField(
                $"<{propertyName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(backingField);
            backingField!.SetValue(context, value);
        }
    }

    private sealed record MutationState(
        InstallmentOrderEntity Order,
        InstallmentRepaymentClaimEntity? Claim,
        int PaymentCount);
}
