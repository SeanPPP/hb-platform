using BlazorApp.Shared.Constants;
using Hbpos.Api.Services;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Tests;

public sealed class InstallmentRepaymentClaimServiceTests
{
    private static readonly DateTimeOffset InitialNow = DateTimeOffset.Parse("2026-08-04T00:00:00Z");

    [Fact]
    public void Capabilities_default_to_required_repayment_claims_and_fail_closed_lifecycle_switches()
    {
        var harness = CreateHarness(crossDeviceEnabled: true);

        var capabilities = harness.Service.GetCapabilities();

        Assert.True(capabilities.RepaymentClaimsSupported);
        Assert.True(capabilities.RepaymentClaimsRequired);
        Assert.True(capabilities.CrossDeviceRepaymentEnabled);
        Assert.False(capabilities.CrossDeviceCancelRefundEnabled);
        Assert.False(capabilities.CrossDeviceVoidEnabled);
        Assert.False(capabilities.CrossDevicePickupEnabled);
        Assert.False(capabilities.CardRepaymentSupported);
        Assert.Equal(120, capabilities.PreparedClaimTtlSeconds);
    }

    [Fact]
    public async Task Create_is_idempotent_only_for_the_same_identity_and_immutable_fingerprint()
    {
        var harness = CreateHarness();
        var request = CreateRequest();

        var first = await harness.Service.CreateAsync(
            harness.InstallmentGuid,
            request,
            Identity(),
            CancellationToken.None);
        var replay = await harness.Service.CreateAsync(
            harness.InstallmentGuid,
            request,
            Identity(),
            CancellationToken.None);

        Assert.Equal(InstallmentRepaymentClaimStatus.Prepared, first.Status);
        Assert.Equal(InitialNow.AddSeconds(120), first.ExpiresAtUtc);
        Assert.True(replay.AlreadyExists);

        var mismatch = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.CreateAsync(
                harness.InstallmentGuid,
                request with { Amount = request.Amount + 1m },
                Identity(),
                CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Mismatch, mismatch.Code);
    }

    [Fact]
    public async Task Card_create_fails_closed_before_claim_order_or_payment_state_changes()
    {
        var harness = CreateHarness();
        var before = await harness.Installments.GetDetailsAsync(
            harness.InstallmentGuid,
            CancellationToken.None);
        var request = CreateRequest(amount: 10m, method: PaymentMethodKind.Card);

        var exception = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.CreateAsync(
                harness.InstallmentGuid,
                request,
                Identity(),
                CancellationToken.None));

        Assert.Equal(
            InstallmentRepaymentClaimErrorCodes.PaymentMethodUnsupported,
            exception.Code);
        Assert.Null(await harness.Repository.GetAsync(request.OperationGuid, CancellationToken.None));
        Assert.Null(await harness.Repository.GetBlockingAsync(harness.InstallmentGuid, CancellationToken.None));
        Assert.Equal(0, harness.Installments.AppendCount);
        var after = await harness.Installments.GetDetailsAsync(
            harness.InstallmentGuid,
            CancellationToken.None);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Prepared_expires_after_120_seconds_but_provider_pending_and_unknown_never_auto_expire()
    {
        var harness = CreateHarness();
        var first = CreateRequest();
        await harness.Service.CreateAsync(harness.InstallmentGuid, first, Identity(), CancellationToken.None);

        harness.Time.UtcNow = InitialNow.AddSeconds(121);
        var second = CreateRequest();
        await harness.Service.CreateAsync(harness.InstallmentGuid, second, Identity(), CancellationToken.None);
        var released = await harness.Repository.GetAsync(first.OperationGuid, CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.Released, released?.Status);

        await harness.Service.BeginProviderAsync(
            harness.InstallmentGuid,
            second.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest("cash", "attempt-1"),
            Identity(),
            CancellationToken.None);
        harness.Time.UtcNow = InitialNow.AddHours(12);

        var pendingBusy = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.CreateAsync(harness.InstallmentGuid, CreateRequest(), Identity(), CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Busy, pendingBusy.Code);

        await harness.Service.ResolveAsync(
            harness.InstallmentGuid,
            second.OperationGuid,
            new InstallmentRepaymentClaimResolveRequest(InstallmentRepaymentClaimResolveOutcome.Unknown),
            Identity(),
            CancellationToken.None);
        harness.Time.UtcNow = InitialNow.AddDays(30);

        var unknownBusy = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.CreateAsync(harness.InstallmentGuid, CreateRequest(), Identity(), CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Busy, unknownBusy.Code);
    }

    [Fact]
    public async Task Resolve_only_releases_prepared_and_only_declines_or_marks_unknown_after_provider_begin()
    {
        var harness = CreateHarness();
        var prepared = CreateRequest();
        await harness.Service.CreateAsync(harness.InstallmentGuid, prepared, Identity(), CancellationToken.None);

        var released = await harness.Service.ResolveAsync(
            harness.InstallmentGuid,
            prepared.OperationGuid,
            new InstallmentRepaymentClaimResolveRequest(InstallmentRepaymentClaimResolveOutcome.Released),
            Identity(),
            CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.Released, released.Status);

        var pending = CreateRequest(method: PaymentMethodKind.Cash);
        await harness.Service.CreateAsync(harness.InstallmentGuid, pending, Identity(), CancellationToken.None);
        await harness.Service.BeginProviderAsync(
            harness.InstallmentGuid,
            pending.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest("cash", "cash-attempt-1"),
            Identity(),
            CancellationToken.None);

        var invalidRelease = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.ResolveAsync(
                harness.InstallmentGuid,
                pending.OperationGuid,
                new InstallmentRepaymentClaimResolveRequest(InstallmentRepaymentClaimResolveOutcome.Released),
                Identity(),
                CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Mismatch, invalidRelease.Code);

        var declined = await harness.Service.ResolveAsync(
            harness.InstallmentGuid,
            pending.OperationGuid,
            new InstallmentRepaymentClaimResolveRequest(InstallmentRepaymentClaimResolveOutcome.Declined),
            Identity(),
            CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.Declined, declined.Status);
    }

    [Fact]
    public async Task Commit_is_idempotent_and_allows_only_configured_same_store_cross_device_recovery()
    {
        var denied = CreateHarness(crossDeviceEnabled: false, installmentDeviceCode: "POS-01");
        var deniedException = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            denied.Service.CreateAsync(
                denied.InstallmentGuid,
                CreateRequest(method: PaymentMethodKind.Cash),
                Identity(deviceCode: "POS-02"),
                CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Mismatch, deniedException.Code);

        var harness = CreateHarness(crossDeviceEnabled: true, installmentDeviceCode: "POS-01");
        var identity = Identity(deviceCode: "POS-02");
        var request = CreateRequest(amount: 10m, method: PaymentMethodKind.Cash);
        await harness.Service.CreateAsync(harness.InstallmentGuid, request, identity, CancellationToken.None);
        await harness.Service.BeginProviderAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest("cash", "attempt-cross-device"),
            identity,
            CancellationToken.None);

        var first = await harness.Service.CommitAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimCommitRequest(),
            identity,
            CancellationToken.None);
        var replay = await harness.Service.CommitAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimCommitRequest(),
            identity,
            CancellationToken.None);

        Assert.Equal(InstallmentRepaymentClaimStatus.Committed, first.Status);
        Assert.Equal(30m, first.Commit?.PaidAmount);
        Assert.Equal(InstallmentRepaymentClaimStatus.Committed, replay.Status);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(first.Commit),
            System.Text.Json.JsonSerializer.Serialize(replay.Commit));
        Assert.False(replay.Commit?.AlreadyRecorded);
        Assert.Equal(1, harness.Installments.AppendCount);

        var crossStore = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.GetAsync(
                harness.InstallmentGuid,
                request.OperationGuid,
                Identity(storeCode: "S02", deviceCode: "POS-02"),
                CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Mismatch, crossStore.Code);
    }

    [Fact]
    public async Task Existing_cross_device_claim_stays_bound_to_the_original_claimant_when_config_or_display_name_changes()
    {
        var harness = CreateHarness(crossDeviceEnabled: true, installmentDeviceCode: "POS-01");
        var claimant = Identity(deviceCode: "POS-02", cashierName: "Original Display Name");
        var request = CreateRequest();
        await harness.Service.CreateAsync(harness.InstallmentGuid, request, claimant, CancellationToken.None);

        // 配置只控制新建 claim；已建立的恢复链必须继续由原设备和稳定 CashierId 完成。
        var claimsNowOptional = new InstallmentRepaymentClaimService(
            harness.Repository,
            harness.Installments,
            harness.CommitRepository,
            Options.Create(new InstallmentRepaymentClaimOptions { CrossDeviceEnabled = false }),
            harness.Time);
        var renamedClaimant = claimant with { CashierName = "Renamed Cashier" };

        var recovered = await claimsNowOptional.GetAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            renamedClaimant,
            CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.Prepared, recovered.Status);

        var otherDevice = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            claimsNowOptional.GetAsync(
                harness.InstallmentGuid,
                request.OperationGuid,
                renamedClaimant with { DeviceCode = "POS-03" },
                CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Mismatch, otherDevice.Code);

        var otherCashier = await claimsNowOptional.GetAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            renamedClaimant with { CashierId = "C02", CashierUserGuid = "U02" },
            CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.Prepared, otherCashier.Status);

        await claimsNowOptional.BeginProviderAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest("cash", "attempt-config-toggle"),
            renamedClaimant,
            CancellationToken.None);
    }

    [Fact]
    public async Task Unknown_requires_the_same_provider_attempt_to_resume_before_commit_or_decline()
    {
        var harness = CreateHarness();
        var identity = Identity();
        var request = CreateRequest();
        await harness.Service.CreateAsync(harness.InstallmentGuid, request, identity, CancellationToken.None);
        await harness.Service.BeginProviderAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest("cash", "attempt-unknown"),
            identity,
            CancellationToken.None);
        await harness.Service.ResolveAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimResolveRequest(InstallmentRepaymentClaimResolveOutcome.Unknown),
            identity,
            CancellationToken.None);

        var directCommit = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.CommitAsync(
                harness.InstallmentGuid,
                request.OperationGuid,
                new InstallmentRepaymentClaimCommitRequest(Reference: "APPROVED"),
                identity,
                CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Mismatch, directCommit.Code);

        var directDecline = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.ResolveAsync(
                harness.InstallmentGuid,
                request.OperationGuid,
                new InstallmentRepaymentClaimResolveRequest(InstallmentRepaymentClaimResolveOutcome.Declined),
                identity,
                CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Mismatch, directDecline.Code);

        var wrongAttempt = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.BeginProviderAsync(
                harness.InstallmentGuid,
                request.OperationGuid,
                new InstallmentRepaymentClaimBeginProviderRequest("cash", "different-attempt"),
                identity,
                CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Mismatch, wrongAttempt.Code);

        var resumed = await harness.Service.BeginProviderAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest("cash", "attempt-unknown"),
            identity,
            CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.ProviderPending, resumed.Status);

        var committed = await harness.Service.CommitAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimCommitRequest(),
            identity,
            CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.Committed, committed.Status);
    }

    [Fact]
    public async Task Cash_claim_amount_cannot_exceed_balance_and_is_not_treated_as_tendered_amount()
    {
        var harness = CreateHarness();

        var exception = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.CreateAsync(
                harness.InstallmentGuid,
                CreateRequest(amount: 80.01m, method: PaymentMethodKind.Cash),
                Identity(),
                CancellationToken.None));

        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Mismatch, exception.Code);
        Assert.Null(await harness.Repository.GetBlockingAsync(harness.InstallmentGuid, CancellationToken.None));
    }

    [Theory]
    [InlineData(PaymentMethodKind.Cash, "card")]
    [InlineData(PaymentMethodKind.Cash, "voucher")]
    [InlineData(PaymentMethodKind.Voucher, "cash")]
    public async Task Begin_rejects_provider_that_does_not_match_the_claim_method_before_provider_side_effects(
        PaymentMethodKind method,
        string provider)
    {
        var harness = CreateHarness();
        var request = CreateRequest(method: method);
        await harness.Service.CreateAsync(harness.InstallmentGuid, request, Identity(), CancellationToken.None);
        var exception = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.BeginProviderAsync(
                harness.InstallmentGuid,
                request.OperationGuid,
                new InstallmentRepaymentClaimBeginProviderRequest(provider, "attempt-1"),
                Identity(),
                CancellationToken.None));

        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Invalid, exception.Code);
        Assert.Equal(0, harness.Installments.AppendCount);
        var stored = await harness.Repository.GetAsync(request.OperationGuid, CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.Prepared, stored?.Status);
        Assert.Null(stored?.Provider);
    }

    [Fact]
    public async Task Legacy_card_claim_cannot_begin_a_provider_attempt()
    {
        var harness = CreateHarness();
        var claim = await SeedLegacyCardClaimAsync(harness, InstallmentRepaymentClaimStatus.Prepared);

        var exception = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.BeginProviderAsync(
                harness.InstallmentGuid,
                claim.OperationGuid,
                new InstallmentRepaymentClaimBeginProviderRequest("linkly", "attempt-card"),
                Identity(),
                CancellationToken.None));

        Assert.Equal(InstallmentRepaymentClaimErrorCodes.PaymentMethodUnsupported, exception.Code);
        var stored = await harness.Repository.GetAsync(claim.OperationGuid, CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.Prepared, stored?.Status);
        Assert.Null(stored?.Provider);
        Assert.Equal(0, harness.Installments.AppendCount);
    }

    [Fact]
    public async Task Legacy_provider_pending_card_claim_rejects_forged_complete_evidence_without_state_changes()
    {
        var harness = CreateHarness();
        var claim = await SeedLegacyCardClaimAsync(harness, InstallmentRepaymentClaimStatus.ProviderPending);
        var before = await harness.Installments.GetDetailsAsync(harness.InstallmentGuid, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.CommitAsync(
                harness.InstallmentGuid,
                claim.OperationGuid,
                new InstallmentRepaymentClaimCommitRequest(
                    Reference: "APPROVED",
                    CardTransactions: [CardTransaction("ANZ", "TXN-FORGED", claim.Amount)]),
                Identity(),
                CancellationToken.None));

        Assert.Equal(InstallmentRepaymentClaimErrorCodes.PaymentMethodUnsupported, exception.Code);
        Assert.Equal(0, harness.Installments.AppendCount);
        Assert.Equal(before, await harness.Installments.GetDetailsAsync(harness.InstallmentGuid, CancellationToken.None));
        var stored = await harness.Repository.GetAsync(claim.OperationGuid, CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.ProviderPending, stored?.Status);
        Assert.Null(stored?.CommittedAtUtc);
        Assert.Null(stored?.CommitResponseJson);
    }

    [Theory]
    [InlineData(PaymentMethodKind.Cash)]
    [InlineData(PaymentMethodKind.Voucher)]
    public async Task Non_card_commit_rejects_card_transaction_evidence(PaymentMethodKind method)
    {
        var harness = CreateHarness();
        var request = CreateRequest(method: method);
        await harness.Service.CreateAsync(harness.InstallmentGuid, request, Identity(), CancellationToken.None);
        var provider = method == PaymentMethodKind.Cash ? "cash" : "voucher";
        await harness.Service.BeginProviderAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest(provider, "attempt-non-card"),
            Identity(),
            CancellationToken.None);

        var invalid = ValidCommitRequest(method) with
        {
            CardTransactions = [CardTransaction("Square", "TXN-1", request.Amount)]
        };
        var exception = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.CommitAsync(
                harness.InstallmentGuid,
                request.OperationGuid,
                invalid,
                Identity(),
                CancellationToken.None));

        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Invalid, exception.Code);
        Assert.Equal(0, harness.Installments.AppendCount);
    }

    [Fact]
    public async Task Create_and_begin_enforce_schema_lengths_at_the_boundary()
    {
        var maxStoreCode = new string('s', 50);
        var maxDeviceCode = new string('d', 50);
        var maxHarness = CreateHarness(
            installmentDeviceCode: maxDeviceCode,
            installmentStoreCode: maxStoreCode);
        var maxRequest = CreateRequest() with { IdempotencyKey = new string('i', 100) };
        await maxHarness.Service.CreateAsync(maxHarness.InstallmentGuid, maxRequest, Identity(
            storeCode: maxStoreCode,
            deviceCode: maxDeviceCode,
            cashierId: new string('c', 50),
            cashierName: new string('n', 100),
            cashierUserGuid: new string('u', 50)), CancellationToken.None);
        await maxHarness.Service.BeginProviderAsync(
            maxHarness.InstallmentGuid,
            maxRequest.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest("cash", new string('a', 128)),
            Identity(
                storeCode: maxStoreCode,
                deviceCode: maxDeviceCode,
                cashierId: new string('c', 50),
                cashierName: new string('n', 100),
                cashierUserGuid: new string('u', 50)),
            CancellationToken.None);

        var cases = new (InstallmentRepaymentClaimCreateRequest Request, InstallmentRepaymentClaimIdentity Identity)[]
        {
            (CreateRequest() with { IdempotencyKey = new string('i', 101) }, Identity()),
            (CreateRequest(), Identity(storeCode: new string('s', 51))),
            (CreateRequest(), Identity(deviceCode: new string('d', 51))),
            (CreateRequest(), Identity(cashierId: new string('c', 51))),
            (CreateRequest(), Identity(cashierName: new string('n', 101))),
            (CreateRequest(), Identity(cashierUserGuid: new string('u', 51))),
        };
        foreach (var item in cases)
        {
            var harness = CreateHarness();
            var exception = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
                harness.Service.CreateAsync(
                    harness.InstallmentGuid,
                    item.Request,
                    item.Identity,
                    CancellationToken.None));
            Assert.Equal(InstallmentRepaymentClaimErrorCodes.Invalid, exception.Code);
            Assert.Null(await harness.Repository.GetBlockingAsync(harness.InstallmentGuid, CancellationToken.None));
        }

        foreach (var begin in new[]
        {
            new InstallmentRepaymentClaimBeginProviderRequest(new string('p', 32), "attempt"),
            new InstallmentRepaymentClaimBeginProviderRequest(new string('p', 33), "attempt"),
            new InstallmentRepaymentClaimBeginProviderRequest("cash", new string('a', 129)),
        })
        {
            var harness = CreateHarness();
            var request = CreateRequest(method: PaymentMethodKind.Cash);
            await harness.Service.CreateAsync(harness.InstallmentGuid, request, Identity(), CancellationToken.None);
            var exception = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
                harness.Service.BeginProviderAsync(
                    harness.InstallmentGuid,
                    request.OperationGuid,
                    begin,
                    Identity(),
                    CancellationToken.None));
            Assert.Equal(InstallmentRepaymentClaimErrorCodes.Invalid, exception.Code);
        }
    }

    [Fact]
    public async Task Every_mutation_requires_confirm_and_the_method_specific_permission()
    {
        foreach (var method in new[] { PaymentMethodKind.Cash, PaymentMethodKind.Voucher })
        {
            var requiredMethodPermission = method switch
            {
                PaymentMethodKind.Cash => Permissions.PosTerminal.Payment.TakeCash,
                PaymentMethodKind.Card => Permissions.PosTerminal.Payment.TakeCard,
                PaymentMethodKind.Voucher => Permissions.PosTerminal.Payment.TakeVoucher,
                _ => throw new ArgumentOutOfRangeException(nameof(method))
            };
            foreach (var missing in new[]
            {
                requiredMethodPermission,
                Permissions.PosTerminal.Payment.Confirm,
                Permissions.PosTerminal.Installments.AddRepayment,
            })
            {
                var permissions = DefaultPaymentPermissions().Where(code => code != missing).ToArray();
                var harness = CreateHarness();
                var request = CreateRequest(method: method);
                var exception = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
                    harness.Service.CreateAsync(
                        harness.InstallmentGuid,
                        request,
                        Identity(permissionCodes: permissions),
                        CancellationToken.None));
                Assert.Equal(InstallmentRepaymentClaimErrorCodes.PermissionDenied, exception.Code);
                Assert.Null(await harness.Repository.GetBlockingAsync(harness.InstallmentGuid, CancellationToken.None));
            }
        }
    }

    [Fact]
    public async Task Original_device_current_authorized_cashier_can_resume_while_payment_audit_stays_with_creator()
    {
        var harness = CreateHarness();
        var creator = Identity(cashierId: "CREATOR", cashierName: "Creator");
        var recoveryCashier = Identity(
            cashierId: "RECOVERY",
            cashierName: "Recovery Cashier",
            cashierUserGuid: "USER-RECOVERY");
        var request = CreateRequest(method: PaymentMethodKind.Cash);
        await harness.Service.CreateAsync(harness.InstallmentGuid, request, creator, CancellationToken.None);
        await harness.Service.BeginProviderAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest("cash", "cash-attempt"),
            creator,
            CancellationToken.None);
        await harness.Service.BeginProviderAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest("cash", "cash-attempt"),
            recoveryCashier,
            CancellationToken.None);

        var committed = await harness.Service.CommitAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimCommitRequest(),
            recoveryCashier,
            CancellationToken.None);

        Assert.Equal(InstallmentRepaymentClaimStatus.Committed, committed.Status);
        var payment = Assert.Single(committed.Commit!.Details.Payments);
        Assert.Equal("CREATOR", payment.CashierId);
        Assert.Equal("Creator", payment.CashierName);
        var storedClaim = await harness.Repository.GetAsync(request.OperationGuid, CancellationToken.None);
        Assert.Equal("RECOVERY", storedClaim?.LastRecoveryCashierId);
        Assert.Equal("Recovery Cashier", storedClaim?.LastRecoveryCashierName);
        Assert.Equal("USER-RECOVERY", storedClaim?.LastRecoveryCashierUserGuid);
        Assert.Equal(InitialNow, storedClaim?.RecoveredAtUtc);
    }

    [Fact]
    public async Task Different_cashier_cannot_begin_a_prepared_claim_but_can_release_it()
    {
        var harness = CreateHarness();
        var creator = Identity(cashierId: "CREATOR", cashierName: "Creator");
        var other = Identity(cashierId: "OTHER", cashierName: "Other", cashierUserGuid: "USER-OTHER");
        var request = CreateRequest(method: PaymentMethodKind.Cash);
        await harness.Service.CreateAsync(harness.InstallmentGuid, request, creator, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.BeginProviderAsync(
                harness.InstallmentGuid,
                request.OperationGuid,
                new InstallmentRepaymentClaimBeginProviderRequest("cash", "attempt-other"),
                other,
                CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.Mismatch, exception.Code);

        var released = await harness.Service.ResolveAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimResolveRequest(InstallmentRepaymentClaimResolveOutcome.Released),
            other,
            CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.Released, released.Status);
    }

    [Theory]
    [InlineData(PaymentMethodKind.Cash)]
    [InlineData(PaymentMethodKind.Voucher)]
    public async Task Begin_and_commit_recheck_the_current_cashier_permissions(PaymentMethodKind method)
    {
        var requiredMethodPermission = method switch
        {
            PaymentMethodKind.Cash => Permissions.PosTerminal.Payment.TakeCash,
            PaymentMethodKind.Card => Permissions.PosTerminal.Payment.TakeCard,
            PaymentMethodKind.Voucher => Permissions.PosTerminal.Payment.TakeVoucher,
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
        var provider = method switch
        {
            PaymentMethodKind.Cash => "cash",
            PaymentMethodKind.Card => "square",
            PaymentMethodKind.Voucher => "voucher",
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
        var harness = CreateHarness();
        var request = CreateRequest(method: method);
        await harness.Service.CreateAsync(harness.InstallmentGuid, request, Identity(), CancellationToken.None);

        var beginDenied = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.BeginProviderAsync(
                harness.InstallmentGuid,
                request.OperationGuid,
                new InstallmentRepaymentClaimBeginProviderRequest(provider, "attempt-permission"),
                Identity(permissionCodes: DefaultPaymentPermissions().Where(code => code != requiredMethodPermission).ToArray()),
                CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.PermissionDenied, beginDenied.Code);

        await harness.Service.BeginProviderAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest(provider, "attempt-permission"),
            Identity(),
            CancellationToken.None);
        var commitDenied = await Assert.ThrowsAsync<InstallmentRepaymentClaimException>(() =>
            harness.Service.CommitAsync(
                harness.InstallmentGuid,
                request.OperationGuid,
                ValidCommitRequest(method),
                Identity(permissionCodes: DefaultPaymentPermissions()
                    .Where(code => code != Permissions.PosTerminal.Payment.Confirm)
                    .ToArray()),
                CancellationToken.None));
        Assert.Equal(InstallmentRepaymentClaimErrorCodes.PermissionDenied, commitDenied.Code);
        Assert.Equal(0, harness.Installments.AppendCount);

        var committed = await harness.Service.CommitAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            ValidCommitRequest(method),
            Identity(),
            CancellationToken.None);
        Assert.Equal(InstallmentRepaymentClaimStatus.Committed, committed.Status);
    }

    [Fact]
    public async Task Committed_service_replay_keeps_the_original_response_after_pickup()
    {
        var harness = CreateHarness();
        var request = CreateRequest(method: PaymentMethodKind.Cash);
        await harness.Service.CreateAsync(harness.InstallmentGuid, request, Identity(), CancellationToken.None);
        await harness.Service.BeginProviderAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimBeginProviderRequest("cash", "cash-replay"),
            Identity(),
            CancellationToken.None);
        var committed = await harness.Service.CommitAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimCommitRequest(),
            Identity(),
            CancellationToken.None);
        harness.Installments.ChangeStatus(InstallmentStatus.PickedUp);
        harness.Time.UtcNow = InitialNow.AddMinutes(10);
        var recoveryIdentity = Identity(
            cashierId: "RECOVERY",
            cashierName: "Recovery Cashier",
            cashierUserGuid: "USER-RECOVERY");

        var replay = await harness.Service.CommitAsync(
            harness.InstallmentGuid,
            request.OperationGuid,
            new InstallmentRepaymentClaimCommitRequest(),
            recoveryIdentity,
            CancellationToken.None);

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(committed.Commit),
            System.Text.Json.JsonSerializer.Serialize(replay.Commit));
        Assert.Equal(InstallmentStatus.Active, replay.Commit?.Status);
        var storedClaim = await harness.Repository.GetAsync(request.OperationGuid, CancellationToken.None);
        Assert.Equal("RECOVERY", storedClaim?.LastRecoveryCashierId);
        Assert.Equal("USER-RECOVERY", storedClaim?.LastRecoveryCashierUserGuid);
        Assert.Equal(InitialNow.AddMinutes(10), storedClaim?.RecoveredAtUtc);
    }

    private static Harness CreateHarness(
        bool crossDeviceEnabled = false,
        string installmentDeviceCode = "POS-01",
        string installmentStoreCode = "S01")
    {
        var installmentGuid = Guid.NewGuid();
        var repository = new InMemoryClaimRepository();
        var installments = new FakeInstallmentRepository(CreateDetails(
            installmentGuid,
            installmentDeviceCode,
            installmentStoreCode));
        var time = new MutableFakeTimeProvider(InitialNow);
        var commitRepository = new InMemoryCommitRepository(repository, installments);
        var service = new InstallmentRepaymentClaimService(
            repository,
            installments,
            commitRepository,
            Options.Create(new InstallmentRepaymentClaimOptions
            {
                CrossDeviceEnabled = crossDeviceEnabled
            }),
            time);
        return new Harness(installmentGuid, service, repository, installments, commitRepository, time);
    }

    private static InstallmentRepaymentClaimCreateRequest CreateRequest(
        decimal amount = 10m,
        PaymentMethodKind method = PaymentMethodKind.Cash)
    {
        return new InstallmentRepaymentClaimCreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            amount,
            method,
            $"action-{Guid.NewGuid():N}");
    }

    private static async Task<InstallmentRepaymentClaimRecord> SeedLegacyCardClaimAsync(
        Harness harness,
        InstallmentRepaymentClaimStatus status)
    {
        var providerPending = status == InstallmentRepaymentClaimStatus.ProviderPending;
        var claim = new InstallmentRepaymentClaimRecord(
            harness.InstallmentGuid,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "S01",
            "POS-01",
            "C01",
            "Cashier One",
            10m,
            PaymentMethodKind.Card,
            $"legacy-card-{Guid.NewGuid():N}",
            new string('a', 64),
            status,
            providerPending ? "linkly" : null,
            providerPending ? "legacy-attempt" : null,
            InitialNow.AddMinutes(-1),
            InitialNow.AddMinutes(-1),
            providerPending ? null : InitialNow.AddMinutes(1),
            null,
            providerPending ? 2 : 1);
        var inserted = await harness.Repository.TryInsertAsync(
            claim,
            new InstallmentRepaymentClaimInsertSnapshot(
                InstallmentStatus.Active,
                PaidAmount: 20m,
                BalanceAmount: 80m),
            CancellationToken.None);
        Assert.True(inserted);
        return claim;
    }

    private static InstallmentRepaymentClaimIdentity Identity(
        string storeCode = "S01",
        string deviceCode = "POS-01",
        string cashierId = "C01",
        string cashierName = "Cashier One",
        IReadOnlyCollection<string>? permissionCodes = null,
        string cashierUserGuid = "U01") => new(
            storeCode,
            deviceCode,
            cashierId,
            cashierName,
            permissionCodes ?? DefaultPaymentPermissions(),
            cashierUserGuid);

    private static string[] DefaultPaymentPermissions() =>
    [
        Permissions.PosTerminal.Payment.Confirm,
        Permissions.PosTerminal.Installments.AddRepayment,
        Permissions.PosTerminal.Payment.TakeCash,
        Permissions.PosTerminal.Payment.TakeCard,
        Permissions.PosTerminal.Payment.TakeVoucher,
    ];

    private static InstallmentRepaymentClaimCommitRequest ValidCommitRequest(
        PaymentMethodKind method) => method switch
        {
            PaymentMethodKind.Cash => new InstallmentRepaymentClaimCommitRequest(),
            PaymentMethodKind.Voucher => new InstallmentRepaymentClaimCommitRequest(
                Reference: "VOUCHER-1",
                ReservationToken: "reservation-1"),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };

    private static CardTransactionDto CardTransaction(string processor, string? txnRef, decimal amount)
    {
        var square = string.Equals(processor, "square", StringComparison.OrdinalIgnoreCase);
        return CardTransaction(
            processor,
            txnRef,
            amount,
            square ? null : "00",
            square ? "COMPLETED" : "APPROVED");
    }

    private static CardTransactionDto CardTransaction(
        string processor,
        string? txnRef,
        decimal amount,
        string? responseCode,
        string? responseText) => new(
        processor,
        txnRef,
        AuthCode: "AUTH",
        CardType: "VISA",
        CardBin: 412345,
        MaskedCardNumber: "****1234",
        MerchantId: "MERCHANT",
        ResponseCode: responseCode,
        ResponseText: responseText,
        Stan: "123456",
        BankDateTime: InitialNow,
        amount,
        ReceiptText: "approved");

    private static InstallmentDetailsDto CreateDetails(
        Guid installmentGuid,
        string deviceCode,
        string storeCode)
    {
        return new InstallmentDetailsDto(
            installmentGuid,
            "INS-001",
            storeCode,
            deviceCode,
            "C01",
            "Cashier One",
            "Customer One",
            "0400000000",
            InitialNow.AddDays(-1),
            100m,
            20m,
            20m,
            20m,
            80m,
            InstallmentStatus.Active,
            [],
            [],
            null);
    }

    private sealed record Harness(
        Guid InstallmentGuid,
        InstallmentRepaymentClaimService Service,
        InMemoryClaimRepository Repository,
        FakeInstallmentRepository Installments,
        InMemoryCommitRepository CommitRepository,
        MutableFakeTimeProvider Time);

    private sealed class MutableFakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class InMemoryClaimRepository : IInstallmentRepaymentClaimRepository
    {
        private readonly Dictionary<Guid, InstallmentRepaymentClaimRecord> claims = [];

        public Task<InstallmentRepaymentClaimRecord?> GetAsync(Guid operationGuid, CancellationToken cancellationToken)
        {
            claims.TryGetValue(operationGuid, out var claim);
            return Task.FromResult(claim);
        }

        public Task<InstallmentRepaymentClaimRecord?> GetBlockingAsync(
            Guid installmentGuid,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(claims.Values.FirstOrDefault(claim =>
                claim.InstallmentGuid == installmentGuid &&
                claim.Status is InstallmentRepaymentClaimStatus.Prepared
                    or InstallmentRepaymentClaimStatus.ProviderPending
                    or InstallmentRepaymentClaimStatus.Unknown));
        }

        public Task<bool> TryInsertAsync(
            InstallmentRepaymentClaimRecord claim,
            InstallmentRepaymentClaimInsertSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (claims.ContainsKey(claim.OperationGuid) ||
                claims.Values.Any(existing =>
                    existing.InstallmentGuid == claim.InstallmentGuid &&
                    existing.Status is InstallmentRepaymentClaimStatus.Prepared
                        or InstallmentRepaymentClaimStatus.ProviderPending
                        or InstallmentRepaymentClaimStatus.Unknown))
            {
                return Task.FromResult(false);
            }

            claims[claim.OperationGuid] = claim;
            return Task.FromResult(true);
        }

        public Task<bool> TryUpdateAsync(
            InstallmentRepaymentClaimRecord claim,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            if (!claims.TryGetValue(claim.OperationGuid, out var current) || current.Revision != expectedRevision)
            {
                return Task.FromResult(false);
            }

            claims[claim.OperationGuid] = claim;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeInstallmentRepository(InstallmentDetailsDto initial) : IInstallmentRepository
    {
        private InstallmentDetailsDto details = initial;

        public int AppendCount { get; private set; }

        public void ChangeStatus(InstallmentStatus status)
        {
            details = details with { Status = status };
        }

        public Task CreateAsync(InstallmentDetailsDto value, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstallmentDetailsDto> AppendPaymentAsync(
            Guid installmentGuid,
            InstallmentPaymentDto payment,
            CancellationToken cancellationToken)
        {
            if (details.Payments.All(existing => existing.PaymentGuid != payment.PaymentGuid))
            {
                AppendCount++;
                var paid = details.PaidAmount + payment.Amount;
                details = details with
                {
                    PaidAmount = paid,
                    BalanceAmount = Math.Max(0m, details.TotalAmount - paid),
                    Payments = details.Payments.Concat([payment]).ToList()
                };
            }

            return Task.FromResult(details);
        }

        public Task<InstallmentDetailsDto> ConfirmPickupAsync(Guid installmentGuid, DateTimeOffset pickedUpAt, string pickedUpBy, string? note, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstallmentDetailsDto> CancelWithRefundAsync(Guid installmentGuid, IReadOnlyList<InstallmentPaymentDto> refunds, InstallmentCancellationInfoDto cancellationInfo, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstallmentDetailsDto> VoidAsync(Guid installmentGuid, InstallmentCancellationInfoDto cancellationInfo, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstallmentPaymentLookup?> FindPaymentAsync(Guid paymentGuid, CancellationToken cancellationToken)
        {
            var payment = details.Payments.FirstOrDefault(existing => existing.PaymentGuid == paymentGuid);
            return Task.FromResult(payment is null ? null : new InstallmentPaymentLookup(details.InstallmentGuid, payment));
        }

        public Task<InstallmentPaymentLookup?> FindPaymentByIdempotencyKeyAsync(Guid installmentGuid, string idempotencyKey, CancellationToken cancellationToken)
        {
            var payment = details.Payments.FirstOrDefault(existing => existing.IdempotencyKey == idempotencyKey);
            return Task.FromResult(payment is null ? null : new InstallmentPaymentLookup(details.InstallmentGuid, payment));
        }

        public Task<InstallmentHistoryQueryResponse> QueryAsync(InstallmentHistoryQueryRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstallmentDetailsDto?> GetDetailsAsync(Guid installmentGuid, CancellationToken cancellationToken)
        {
            return Task.FromResult<InstallmentDetailsDto?>(
                details.InstallmentGuid == installmentGuid ? details : null);
        }
    }

    private sealed class InMemoryCommitRepository(
        InMemoryClaimRepository claims,
        FakeInstallmentRepository installments) : IInstallmentRepaymentClaimCommitRepository
    {
        public async Task<InstallmentRepaymentClaimCommitResult> CommitAsync(
            InstallmentRepaymentClaimRecord expectedClaim,
            InstallmentRepaymentClaimCommitRequest request,
            InstallmentRepaymentClaimIdentity recoveryIdentity,
            DateTimeOffset committedAtUtc,
            CancellationToken cancellationToken)
        {
            var current = await claims.GetAsync(expectedClaim.OperationGuid, cancellationToken)
                ?? throw new InvalidOperationException();
            var existing = await installments.FindPaymentAsync(current.PaymentGuid, cancellationToken);
            var alreadyRecorded = existing is not null;
            if (!alreadyRecorded)
            {
                await installments.AppendPaymentAsync(
                    current.InstallmentGuid,
                    new InstallmentPaymentDto(
                        current.PaymentGuid,
                        current.Method,
                        current.Amount,
                        request.Reference,
                        InstallmentPaymentStatus.Recorded,
                        committedAtUtc,
                        current.CashierId,
                        current.ClaimantDeviceCode,
                        request.CardTransactions,
                        current.IdempotencyKey,
                        request.ReservationToken,
                        current.CashierName),
                    cancellationToken);
            }

            var details = await installments.GetDetailsAsync(current.InstallmentGuid, cancellationToken)
                ?? throw new InvalidOperationException();
            var response = new InstallmentAppendPaymentResponse(
                current.InstallmentGuid,
                current.PaymentGuid,
                details.PaidAmount,
                details.BalanceAmount,
                details.Status,
                details,
                AlreadyRecorded: alreadyRecorded,
                Message: alreadyRecorded ? "AlreadyRecorded" : null);
            var responseJson = System.Text.Json.JsonSerializer.Serialize(
                response,
                InstallmentRepaymentClaimCommitRepositoryJson.Options);
            var committed = current with
            {
                Status = InstallmentRepaymentClaimStatus.Committed,
                UpdatedAtUtc = committedAtUtc,
                ExpiresAtUtc = null,
                CommittedAtUtc = committedAtUtc,
                CommitResponseJson = responseJson,
                LastRecoveryCashierId = recoveryIdentity.CashierId,
                LastRecoveryCashierName = recoveryIdentity.CashierName,
                LastRecoveryCashierUserGuid = recoveryIdentity.CashierUserGuid,
                RecoveredAtUtc = committedAtUtc,
                Revision = current.Revision + 1
            };
            await claims.TryUpdateAsync(committed, current.Revision, cancellationToken);
            return new InstallmentRepaymentClaimCommitResult(committed, response, alreadyRecorded);
        }
    }
}
