using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Shared.DTOs;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Installments;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Wpf.Services;

public sealed record InstallmentOperationResult<T>(
    bool Succeeded,
    T? Response = default,
    LocalInstallmentOrder? LocalOrder = null,
    string? Message = null,
    bool RequiresReview = false);

/// <summary>供创建、补款、取消页面和启动恢复共用的窄服务边界。</summary>
public interface IInstallmentOperationService
{
    Task<InstallmentOperationResult<InstallmentCreateResponse>> ExecuteCreateAsync(
        PosSessionState session,
        InstallmentCreateRequest request,
        bool authorizeCard,
        CancellationToken cancellationToken = default);

    Task<InstallmentOperationResult<InstallmentAppendPaymentResponse>> ExecuteRepaymentAsync(
        PosSessionState session,
        InstallmentAppendPaymentRequest request,
        bool authorizeCard,
        CancellationToken cancellationToken = default);

    Task<InstallmentOperationResult<InstallmentCancelResponse>> ExecuteCancelAsync(
        LocalInstallmentOrder localOrder,
        PosSessionState session,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstallmentOperationRecoveryResult>> RecoverAsync(
        PosSessionState session,
        CancellationToken cancellationToken = default);

    Task<bool> ResolveRefundStepAsync(
        Guid refundStepGuid,
        InstallmentRefundSupervisorResolution resolution,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalInstallmentRefundStep>> GetRefundStepsForReviewAsync(
        Guid installmentGuid,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetLockedInstallmentGuidsAsync(PosSessionState session, CancellationToken cancellationToken = default);

    Task<InstallmentOperationResult<InstallmentCancelResponse>> ResumeCancelAfterSupervisorAsync(
        Guid operationGuid,
        string installmentNumber,
        PosSessionState session,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 分期金融操作协调器。终端调用之外的三个边界均先由仓储开启并提交 SQLite 事务：
/// 提交前、终端批准后，以及 API 成功与快照完成时。
/// </summary>
public sealed class InstallmentOperationService(
    ILocalInstallmentOperationRepository repository,
    IInstallmentApiClient apiClient,
    ICardTerminalClient cardTerminalClient,
    IVoucherTenderClient voucherTenderClient,
    ICardTerminalSettingsProvider? cardTerminalSettingsProvider = null,
    ILocalCardPaymentAttemptRepository? cardPaymentAttemptRepository = null,
    ILinklyPaymentAttemptContextAccessor? linklyPaymentAttemptContextAccessor = null,
    ILocalSquarePaymentAttemptRepository? squarePaymentAttemptRepository = null,
    ISquarePaymentAttemptContextAccessor? squarePaymentAttemptContextAccessor = null,
    FinancialSupervisorAuditReplayService? supervisorAuditReplay = null) : IInstallmentOperationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ApiClaimLease = TimeSpan.FromMinutes(2);
    private readonly string _apiClaimToken = Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _createGate = new(1, 1);

    public Task<InstallmentOperationResult<InstallmentCreateResponse>> ExecuteCreateAsync(
        PosSessionState session,
        InstallmentCreateRequest request,
        bool authorizeCard,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExecuteCreateCoreAsync(session, request, authorizeCard, cancellationToken), CancellationToken.None);

    private async Task<InstallmentOperationResult<InstallmentCreateResponse>> ExecuteCreateCoreAsync(
        PosSessionState session,
        InstallmentCreateRequest request,
        bool authorizeCard,
        CancellationToken cancellationToken = default)
    {
        await _createGate.WaitAsync(cancellationToken);
        try
        {
            var recoverable = await repository.GetRecoverableAsync(session.StoreCode, cancellationToken);
            var unresolvedCreate = recoverable.FirstOrDefault(operation =>
                operation.Kind == LocalInstallmentOperationKind.Create &&
                string.Equals(operation.DeviceCode, session.DeviceCode, StringComparison.OrdinalIgnoreCase) &&
                operation.OperationGuid != request.DownPayment.PaymentGuid);
            if (unresolvedCreate is not null)
            {
                // 同一终端只能推进一个未结创建；新请求必须等待原操作恢复或主管结案。
                return new InstallmentOperationResult<InstallmentCreateResponse>(
                    false,
                    Message: "A previous installment creation is still unresolved. Recover it before collecting payment again.",
                    RequiresReview: true);
            }

            var operation = await repository.CreateOrGetAsync(CreateOperation(
                request.DownPayment.PaymentGuid,
                LocalInstallmentOperationKind.Create,
                request.InstallmentGuid,
                request.DownPayment.PaymentGuid,
                request.StoreCode,
                request.DeviceCode,
                request.CashierId,
                EnsureIdempotencyKey(request.DownPayment.IdempotencyKey, request.InstallmentGuid),
                JsonSerializer.Serialize(request, JsonOptions)), cancellationToken);

            var ready = await EnsureCreateTerminalApprovalAsync(operation, session, authorizeCard, cancellationToken);
            if (!ready.Succeeded)
            {
                return new InstallmentOperationResult<InstallmentCreateResponse>(false, Message: ready.Message, RequiresReview: ready.RequiresReview);
            }

            var approvedOperation = ready.Operation!;
            var approvedRequest = Deserialize<InstallmentCreateRequest>(approvedOperation.RequestJson);
            return await SubmitCreateAsync(approvedOperation, approvedRequest, cancellationToken);
        }
        finally
        {
            _createGate.Release();
        }
    }

    public Task<InstallmentOperationResult<InstallmentAppendPaymentResponse>> ExecuteRepaymentAsync(
        PosSessionState session,
        InstallmentAppendPaymentRequest request,
        bool authorizeCard,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExecuteRepaymentCoreAsync(session, request, authorizeCard, cancellationToken), CancellationToken.None);

    private async Task<InstallmentOperationResult<InstallmentAppendPaymentResponse>> ExecuteRepaymentCoreAsync(
        PosSessionState session,
        InstallmentAppendPaymentRequest request,
        bool authorizeCard,
        CancellationToken cancellationToken = default)
    {
        var operation = await repository.CreateOrGetAsync(CreateOperation(
            request.PaymentGuid,
            LocalInstallmentOperationKind.Repayment,
            request.InstallmentGuid,
            request.PaymentGuid,
            request.StoreCode,
            request.DeviceCode,
            request.CashierId,
            EnsureIdempotencyKey(request.IdempotencyKey, request.PaymentGuid),
            JsonSerializer.Serialize(request, JsonOptions)), cancellationToken);

        var ready = await EnsureRepaymentTerminalApprovalAsync(operation, session, authorizeCard, cancellationToken);
        if (!ready.Succeeded)
        {
            return new InstallmentOperationResult<InstallmentAppendPaymentResponse>(false, Message: ready.Message, RequiresReview: ready.RequiresReview);
        }

        var approvedOperation = ready.Operation!;
        var approvedRequest = Deserialize<InstallmentAppendPaymentRequest>(approvedOperation.RequestJson);
        return await SubmitRepaymentAsync(approvedOperation, approvedRequest, cancellationToken);
    }

    public Task<InstallmentOperationResult<InstallmentCancelResponse>> ExecuteCancelAsync(
        LocalInstallmentOrder localOrder,
        PosSessionState session,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExecuteCancelCoreAsync(localOrder, session, reason, cancellationToken), CancellationToken.None);

    private async Task<InstallmentOperationResult<InstallmentCancelResponse>> ExecuteCancelCoreAsync(
        LocalInstallmentOrder localOrder,
        PosSessionState session,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var operationGuid = DeterministicGuid($"cancel:{localOrder.InstallmentGuid:D}");
        var cancelRequest = new InstallmentCancelRequest(
            localOrder.InstallmentGuid,
            session.StoreCode,
            session.DeviceCode,
            session.CashierId,
            session.CashierName,
            DateTimeOffset.UtcNow,
            [],
            string.IsNullOrWhiteSpace(reason) ? "取消分期并退款" : reason.Trim(),
            $"{localOrder.InstallmentGuid:D}:cancel");
        var now = DateTimeOffset.UtcNow;
        var steps = localOrder.Payments
            .Where(payment => payment.Status == InstallmentPaymentStatus.Recorded && payment.Amount > 0m)
            .Select(payment => new LocalInstallmentRefundStep(
                DeterministicGuid($"refund:{localOrder.InstallmentGuid:D}:{payment.PaymentGuid:D}"),
                operationGuid,
                payment.PaymentGuid,
                payment.Method,
                payment.Amount,
                payment.Reference,
                $"{localOrder.InstallmentGuid:D}:refund:{payment.PaymentGuid:D}",
                LocalInstallmentRefundStepState.Prepared,
                null,
                payment.CardTransactions is null ? null : JsonSerializer.Serialize(payment.CardTransactions, JsonOptions),
                null, null, null, null, null, null, now, now))
            .ToList();
        var operation = await repository.CreateCancelOrGetAsync(CreateOperation(
            operationGuid,
            LocalInstallmentOperationKind.Cancel,
            localOrder.InstallmentGuid,
            null,
            session.StoreCode,
            session.DeviceCode,
            session.CashierId,
            cancelRequest.IdempotencyKey!,
            JsonSerializer.Serialize(cancelRequest, JsonOptions)), steps, cancellationToken);

        return await ContinueCancelAsync(operation, localOrder.InstallmentNumber, session, false, cancellationToken);
    }

    public Task<IReadOnlyList<InstallmentOperationRecoveryResult>> RecoverAsync(PosSessionState session, CancellationToken cancellationToken = default) =>
        Task.Run(() => RecoverCoreAsync(session, cancellationToken), CancellationToken.None);

    private async Task<IReadOnlyList<InstallmentOperationRecoveryResult>> RecoverCoreAsync(PosSessionState session, CancellationToken cancellationToken = default)
    {
        var recoverable = await repository.GetRecoverableAsync(session.StoreCode, cancellationToken);
        var results = new List<InstallmentOperationRecoveryResult>();
        foreach (var operation in recoverable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.State == LocalInstallmentOperationState.TerminalSubmitting)
            {
                if (operation.Kind == LocalInstallmentOperationKind.Cancel)
                {
                    // 退款终端调用在重启前中断时，不能假定没有金融副作用；先把每个未落结果的步骤锁定。
                    var refundSteps = await repository.GetRefundStepsAsync(operation.OperationGuid, CancellationToken.None);
                    foreach (var refundStep in refundSteps.Where(step => step.State == LocalInstallmentRefundStepState.TerminalSubmitting))
                    {
                        await repository.TryTransitionRefundStepAsync(
                            refundStep.RefundStepGuid,
                            [LocalInstallmentRefundStepState.TerminalSubmitting],
                            LocalInstallmentRefundStepState.ResultUnknown,
                            DateTimeOffset.UtcNow,
                            failureMessage: "重启时退款终端结果未知，等待主管结案。",
                            cancellationToken: CancellationToken.None);
                    }

                    await repository.TryTransitionAsync(
                        operation.OperationGuid,
                        [LocalInstallmentOperationState.TerminalSubmitting],
                        LocalInstallmentOperationState.ResultUnknown,
                        DateTimeOffset.UtcNow,
                        failureMessage: "重启时退款终端结果未知，禁止自动重试。",
                        cancellationToken: CancellationToken.None);
                    var resumedCancel = await repository.GetAsync(operation.OperationGuid, CancellationToken.None);
                    var resumedSteps = await repository.GetRefundStepsAsync(operation.OperationGuid, CancellationToken.None);
                    if (resumedCancel is not null && resumedSteps.All(IsRefundApproved))
                    {
                        results.Add(await RecoverCancelAsync(resumedCancel, session, cancellationToken));
                    }
                    else
                    {
                        results.Add(new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, LocalInstallmentOperationState.ResultUnknown, false, "退款结果未知，保持锁定。"));
                    }
                    continue;
                }

                await repository.TryTransitionAsync(
                    operation.OperationGuid,
                    [LocalInstallmentOperationState.TerminalSubmitting],
                    LocalInstallmentOperationState.ResultUnknown,
                    DateTimeOffset.UtcNow,
                    failureMessage: "重启时终端结果未知，禁止自动重试。",
                    cancellationToken: cancellationToken);
                var resumed = await repository.GetAsync(operation.OperationGuid, cancellationToken);
                if (resumed is not null && !string.IsNullOrWhiteSpace(resumed.TerminalAttemptGuid))
                {
                    var resumedResult = resumed.Kind switch
                    {
                        LocalInstallmentOperationKind.Create => await RecoverCreateAsync(resumed, session, cancellationToken),
                        LocalInstallmentOperationKind.Repayment => await RecoverRepaymentAsync(resumed, session, cancellationToken),
                        _ => new InstallmentOperationRecoveryResult(resumed.OperationGuid, resumed.Kind, LocalInstallmentOperationState.ResultUnknown, false, "终端结果未知，保持锁定。")
                    };
                    results.Add(resumedResult);
                    continue;
                }

                results.Add(new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, LocalInstallmentOperationState.ResultUnknown, false, "终端结果未知，保持锁定。"));
                continue;
            }

            if (operation.State == LocalInstallmentOperationState.Prepared)
            {
                results.Add(new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, operation.State, false, "未提交终端的草稿保留给用户继续。"));
                continue;
            }

            try
            {
                var result = operation.Kind switch
                {
                    LocalInstallmentOperationKind.Create => await RecoverCreateAsync(operation, session, cancellationToken),
                    LocalInstallmentOperationKind.Repayment => await RecoverRepaymentAsync(operation, session, cancellationToken),
                    LocalInstallmentOperationKind.Cancel => await RecoverCancelAsync(operation, session, cancellationToken),
                    _ => new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, operation.State, false, "退款步骤需由取消操作处理。")
                };
                results.Add(result);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await repository.TryTransitionAsync(operation.OperationGuid,
                    [LocalInstallmentOperationState.TerminalApproved, LocalInstallmentOperationState.ApiSubmitting, LocalInstallmentOperationState.ResultUnknown],
                    LocalInstallmentOperationState.ResultUnknown,
                    DateTimeOffset.UtcNow,
                    failureMessage: exception.Message,
                    cancellationToken: cancellationToken);
                results.Add(new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, LocalInstallmentOperationState.ResultUnknown, false, "恢复 API 结果未知，保持锁定。"));
            }
        }

        return results;
    }

    public Task<bool> ResolveRefundStepAsync(
        Guid refundStepGuid,
        InstallmentRefundSupervisorResolution resolution,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => ResolveRefundStepCoreAsync(refundStepGuid, resolution, cancellationToken),
            CancellationToken.None);

    private async Task<bool> ResolveRefundStepCoreAsync(
        Guid refundStepGuid,
        InstallmentRefundSupervisorResolution resolution,
        CancellationToken cancellationToken)
    {
        var step = await repository.GetRefundStepAsync(refundStepGuid, cancellationToken);
        var operation = step is null
            ? null
            : await repository.GetAsync(step.OperationGuid, cancellationToken);
        if (step is null || operation is null)
        {
            return false;
        }

        var environment = cardTerminalSettingsProvider is null
            ? "Unknown"
            : (await cardTerminalSettingsProvider.GetSettingsAsync(cancellationToken)).Environment.ToString();
        var resolvedAt = DateTimeOffset.UtcNow;
        var journal = BuildInstallmentRefundSupervisorJournal(
            operation,
            step,
            resolution,
            environment,
            resolvedAt);
        var applied = await repository.ResolveRefundStepWithJournalAsync(
            refundStepGuid,
            resolution,
            journal,
            resolvedAt,
            CancellationToken.None);
        if (applied && supervisorAuditReplay is not null)
        {
            await supervisorAuditReplay.PersistAfterCommitAsync(journal, CancellationToken.None);
        }

        return applied;
    }

    public Task<IReadOnlyList<LocalInstallmentRefundStep>> GetRefundStepsForReviewAsync(Guid installmentGuid, CancellationToken cancellationToken = default) =>
        Task.Run(() => repository.GetRefundStepsForInstallmentAsync(installmentGuid, cancellationToken), CancellationToken.None);

    public Task<IReadOnlySet<Guid>> GetLockedInstallmentGuidsAsync(PosSessionState session, CancellationToken cancellationToken = default) =>
        Task.Run(() => GetLockedInstallmentGuidsCoreAsync(session, cancellationToken), CancellationToken.None);

    private async Task<IReadOnlySet<Guid>> GetLockedInstallmentGuidsCoreAsync(PosSessionState session, CancellationToken cancellationToken = default)
    {
        var operations = await repository.GetRecoverableAsync(session.StoreCode, cancellationToken);
        return operations
            .Where(operation => operation.State is LocalInstallmentOperationState.ResultUnknown or LocalInstallmentOperationState.TerminalSubmitting or LocalInstallmentOperationState.ApiSubmitting)
            .Select(operation => operation.InstallmentGuid)
            .ToHashSet();
    }

    public Task<InstallmentOperationResult<InstallmentCancelResponse>> ResumeCancelAfterSupervisorAsync(
        Guid operationGuid,
        string installmentNumber,
        PosSessionState session,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ResumeCancelAfterSupervisorCoreAsync(operationGuid, installmentNumber, session, cancellationToken), CancellationToken.None);

    private async Task<InstallmentOperationResult<InstallmentCancelResponse>> ResumeCancelAfterSupervisorCoreAsync(
        Guid operationGuid,
        string installmentNumber,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        var operation = await repository.GetAsync(operationGuid, cancellationToken);
        if (operation is null || operation.Kind != LocalInstallmentOperationKind.Cancel)
        {
            return new InstallmentOperationResult<InstallmentCancelResponse>(false, Message: "未找到待主管结案的取消操作。");
        }

        return await ContinueCancelAsync(operation, installmentNumber, session, true, cancellationToken);
    }

    private async Task<TerminalReady> EnsureCreateTerminalApprovalAsync(LocalInstallmentOperation operation, PosSessionState session, bool authorizeCard, CancellationToken cancellationToken)
    {
        var request = Deserialize<InstallmentCreateRequest>(operation.RequestJson);
        if (!authorizeCard || request.DownPayment.Method != PaymentMethodKind.Card || request.DownPayment.CardTransactions is { Count: > 0 })
        {
            return await MarkTerminalApprovedAsync(operation, operation.RequestJson, cancellationToken);
        }

        var claim = await repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.Prepared], LocalInstallmentOperationState.TerminalSubmitting, DateTimeOffset.UtcNow, cancellationToken: cancellationToken);
        if (!claim)
        {
            return await ReadReadyOrUnknownAsync(operation.OperationGuid, cancellationToken);
        }

        return await ApproveCardAsync(
            operation,
            request.DownPayment.Amount,
            session,
            authorization => JsonSerializer.Serialize(request with
            {
                DownPayment = request.DownPayment with
                {
                    Amount = authorization.AuthorizedAmount ?? request.DownPayment.Amount,
                    Reference = authorization.Reference ?? request.DownPayment.Reference,
                    CardTransactions = authorization.CardTransactions
                }
            }, JsonOptions),
            cancellationToken);
    }

    private async Task<TerminalReady> EnsureRepaymentTerminalApprovalAsync(LocalInstallmentOperation operation, PosSessionState session, bool authorizeCard, CancellationToken cancellationToken)
    {
        var request = Deserialize<InstallmentAppendPaymentRequest>(operation.RequestJson);
        if (!authorizeCard || request.Method != PaymentMethodKind.Card || request.CardTransactions is { Count: > 0 })
        {
            return await MarkTerminalApprovedAsync(operation, operation.RequestJson, cancellationToken);
        }

        var claim = await repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.Prepared], LocalInstallmentOperationState.TerminalSubmitting, DateTimeOffset.UtcNow, cancellationToken: cancellationToken);
        if (!claim)
        {
            return await ReadReadyOrUnknownAsync(operation.OperationGuid, cancellationToken);
        }

        return await ApproveCardAsync(
            operation,
            request.Amount,
            session,
            authorization => JsonSerializer.Serialize(request with
            {
                Amount = authorization.AuthorizedAmount ?? request.Amount,
                Reference = authorization.Reference ?? request.Reference,
                CardTransactions = authorization.CardTransactions
            }, JsonOptions),
            cancellationToken);
    }

    private async Task<TerminalReady> ApproveCardAsync(LocalInstallmentOperation operation, decimal amount, PosSessionState session, Func<PaymentAuthorizationResult, string> approvedRequestFactory, CancellationToken cancellationToken)
    {
        TerminalAttemptScope? terminalAttempt = null;
        try
        {
            terminalAttempt = await CreateTerminalAttemptScopeAsync(operation, amount, session, cancellationToken);
            if (terminalAttempt.AttemptGuid is not null && !await repository.TryTransitionAsync(
                    operation.OperationGuid,
                    [LocalInstallmentOperationState.TerminalSubmitting],
                    LocalInstallmentOperationState.TerminalSubmitting,
                    DateTimeOffset.UtcNow,
                    terminalAttemptGuid: terminalAttempt.AttemptGuid.Value.ToString("D"),
                    terminalProcessor: terminalAttempt.Processor,
                    cancellationToken: CancellationToken.None))
            {
                return TerminalReady.Unknown("终端操作正在恢复，请勿重复收款。");
            }
            using var terminalContext = terminalAttempt.BeginContext();
            var authorization = await cardTerminalClient.AuthorizeAsync(amount, session, cancellationToken);
            var terminalAttemptGuid = terminalAttempt.AttemptGuid?.ToString("D");
            if (authorization.ResultUnknown)
            {
                await terminalAttempt.RecordOutcomeAsync(authorization, CancellationToken.None);
                await repository.TryTransitionAsync(
                    operation.OperationGuid,
                    [LocalInstallmentOperationState.TerminalSubmitting],
                    LocalInstallmentOperationState.ResultUnknown,
                    DateTimeOffset.UtcNow,
                    terminalAttemptGuid: terminalAttemptGuid,
                    terminalProcessor: authorization.Processor ?? terminalAttempt.Processor,
                    cancellationToken: CancellationToken.None);
                return TerminalReady.Unknown(authorization.Message ?? "终端结果未知，请勿重复收款。");
            }

            if (!authorization.Approved)
            {
                await terminalAttempt.RecordOutcomeAsync(authorization, CancellationToken.None);
                if (!await repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.TerminalSubmitting], LocalInstallmentOperationState.Failed, DateTimeOffset.UtcNow, terminalProcessor: authorization.Processor, failureMessage: authorization.Message, cancellationToken: CancellationToken.None))
                {
                    return TerminalReady.Unknown("Terminal rejection could not be durably recorded; payment remains locked.");
                }
                return TerminalReady.Failed(authorization.Message ?? "银行卡未获批准。");
            }

            if (!HasExactAuthorizedAmount(authorization, amount) || !terminalAttempt.HasVerifiedApprovalEvidence(authorization))
            {
                // 中文注释：批准证据不完整时禁止把 attempt 标为 Approved，重启只能继续查证，不能据请求金额补造成功。
                await terminalAttempt.RecordOutcomeAsync(authorization with { Approved = false, ResultUnknown = true }, CancellationToken.None);
                await repository.TryTransitionAsync(
                    operation.OperationGuid,
                    [LocalInstallmentOperationState.TerminalSubmitting],
                    LocalInstallmentOperationState.ResultUnknown,
                    DateTimeOffset.UtcNow,
                    terminalAttemptGuid: terminalAttemptGuid,
                    terminalProcessor: authorization.Processor ?? terminalAttempt.Processor,
                    failureMessage: "Terminal approved amount did not match the persisted installment amount.",
                    cancellationToken: CancellationToken.None);
                return TerminalReady.Unknown("终端批准金额与分期金额不一致，保持锁定，请勿重复收款。");
            }

            await terminalAttempt.RecordOutcomeAsync(authorization, CancellationToken.None);
            var requestJson = approvedRequestFactory(authorization);
            if (!await repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.TerminalSubmitting], LocalInstallmentOperationState.TerminalApproved, DateTimeOffset.UtcNow, requestJson: requestJson, terminalAttemptGuid: terminalAttemptGuid ?? authorization.SessionId ?? authorization.TxnRef, terminalProcessor: authorization.Processor ?? terminalAttempt.Processor, cancellationToken: CancellationToken.None))
            {
                return TerminalReady.Unknown("Terminal approval could not be durably recorded; payment remains locked.");
            }
            var approved = await repository.GetAsync(operation.OperationGuid, CancellationToken.None)
                ?? throw new InvalidOperationException("终端批准状态未保存。");
            return TerminalReady.Success(approved);
        }
        catch (OperationCanceledException)
        {
            await repository.TryTransitionAsync(
                operation.OperationGuid,
                [LocalInstallmentOperationState.TerminalSubmitting],
                LocalInstallmentOperationState.ResultUnknown,
                DateTimeOffset.UtcNow,
                terminalAttemptGuid: terminalAttempt?.AttemptGuid?.ToString("D"),
                terminalProcessor: terminalAttempt?.Processor,
                cancellationToken: CancellationToken.None);
            return TerminalReady.Unknown("终端结果未知，请勿重复收款。");
        }
        catch (Exception exception)
        {
            await repository.TryTransitionAsync(
                operation.OperationGuid,
                [LocalInstallmentOperationState.TerminalSubmitting],
                LocalInstallmentOperationState.ResultUnknown,
                DateTimeOffset.UtcNow,
                terminalAttemptGuid: terminalAttempt?.AttemptGuid?.ToString("D"),
                terminalProcessor: terminalAttempt?.Processor,
                failureMessage: exception.Message,
                cancellationToken: CancellationToken.None);
            return TerminalReady.Unknown("终端结果未知，请勿重复收款。");
        }
        finally
        {
            terminalAttempt?.Dispose();
        }
    }

    private async Task<TerminalAttemptScope> CreateTerminalAttemptScopeAsync(
        LocalInstallmentOperation operation,
        decimal amount,
        PosSessionState session,
        CancellationToken cancellationToken)
    {
        if (cardTerminalSettingsProvider is null)
        {
            return TerminalAttemptScope.Empty;
        }

        var settings = await cardTerminalSettingsProvider.GetSettingsAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (settings.Processor == CardProcessorKind.Linkly && cardPaymentAttemptRepository is not null && linklyPaymentAttemptContextAccessor is not null)
        {
            var mode = CardTerminalSettings.NormalizeLinklyConnectionMode(settings.LinklyConnectionMode);
            var attemptGuid = DeterministicGuid($"installment-linkly:{operation.OperationGuid:D}");
            var attempt = await cardPaymentAttemptRepository.GetAttemptAsync(attemptGuid, cancellationToken);
            if (attempt is null)
            {
                attempt = new LocalCardPaymentAttempt(
                    attemptGuid,
                    null,
                    mode == LinklyConnectionMode.LocalIp ? LinklyTerminalClient.BuildTxnRef(session) : null,
                    CardProcessorKind.Linkly.ToString(),
                    settings.Environment.ToString(),
                    CardTerminalSettings.FormatLinklyConnectionMode(mode),
                    "P",
                    amount,
                    LocalCardPaymentAttemptStatus.Pending,
                    operation.RequestJson,
                    session.StoreCode,
                    session.DeviceCode,
                    session.CashierId,
                    null,
                    null,
                    null,
                    now,
                    now,
                    null,
                    null,
                    operation.Kind.ToString(),
                    operation.OperationGuid);
                await cardPaymentAttemptRepository.CreateAsync(attempt, cancellationToken);
            }

            var attemptContext = new LinklyPaymentAttemptContext(
                attempt.AttemptGuid,
                // 中文注释：终端已返回 session 后，UI 取消不能丢失恢复所需的绑定证据。
                (sessionId, txnRef, updatedAt, _) =>
                    cardPaymentAttemptRepository.UpdateSessionAsync(attempt.AttemptGuid, sessionId, txnRef, updatedAt, CancellationToken.None),
                attempt.TxnRef);
            return new TerminalAttemptScope(
                attempt.AttemptGuid,
                attempt.Processor,
                () => linklyPaymentAttemptContextAccessor.Begin(attemptContext),
                authorization => mode != LinklyConnectionMode.LocalIp ||
                    !string.IsNullOrWhiteSpace(attempt.TxnRef) &&
                    string.Equals(authorization.TxnRef ?? authorization.CardTransactions?.FirstOrDefault()?.TxnRef, attempt.TxnRef, StringComparison.Ordinal),
                async (authorization, _) =>
                {
                    if (authorization.ResultUnknown)
                    {
                        await cardPaymentAttemptRepository.MarkRecoveringAsync(attempt.AttemptGuid, DateTimeOffset.UtcNow, CancellationToken.None);
                        return;
                    }

                    await cardPaymentAttemptRepository.UpdateOutcomeAsync(
                        attempt.AttemptGuid,
                        authorization.Approved ? LocalCardPaymentAttemptStatus.Approved : LocalCardPaymentAttemptStatus.Declined,
                        authorization.ResponseCode,
                        authorization.ResponseText ?? authorization.Message,
                        authorization.Reference,
                        DateTimeOffset.UtcNow,
                        CancellationToken.None);
                });
        }

        if (settings.Processor == CardProcessorKind.Square && squarePaymentAttemptRepository is not null && squarePaymentAttemptContextAccessor is not null &&
            !string.IsNullOrWhiteSpace(settings.SquareDeviceId) && !string.IsNullOrWhiteSpace(settings.SquareLocationId))
        {
            var attemptGuid = DeterministicGuid($"installment-square:{operation.OperationGuid:D}");
            var attempt = await squarePaymentAttemptRepository.GetAttemptAsync(attemptGuid, cancellationToken);
            if (attempt is null)
            {
                attempt = new LocalSquarePaymentAttempt(
                    attemptGuid,
                    null,
                    operation.OperationGuid.ToString("N"),
                    SquareDeviceIdNormalizer.NormalizeForTerminalCheckout(settings.SquareDeviceId) ?? settings.SquareDeviceId,
                    settings.SquareLocationId,
                    settings.Environment.ToString(),
                    amount,
                    (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero),
                    "AUD",
                    LocalSquarePaymentAttemptStatus.Pending,
                    null,
                    null,
                    operation.RequestJson,
                    session.StoreCode,
                    session.DeviceCode,
                    session.CashierId,
                    null,
                    null,
                    null,
                    null,
                    now,
                    now,
                    null,
                    null,
                    null,
                    operation.Kind.ToString(),
                    operation.OperationGuid);
                await squarePaymentAttemptRepository.CreateAsync(attempt, cancellationToken);
            }

            var attemptContext = new SquarePaymentAttemptContext(
                attempt.AttemptGuid,
                attempt.IdempotencyKey,
                (checkoutId, checkoutStatus, updatedAt, _) =>
                    squarePaymentAttemptRepository.MarkCheckoutCreatedAsync(
                        attempt.AttemptGuid,
                        checkoutId,
                        checkoutStatus,
                        updatedAt,
                        CancellationToken.None));
            return new TerminalAttemptScope(
                attempt.AttemptGuid,
                CardProcessorKind.Square.ToString(),
                () => squarePaymentAttemptContextAccessor.Begin(attemptContext),
                null,
                async (authorization, _) =>
                {
                    if (authorization.ResultUnknown)
                    {
                        await squarePaymentAttemptRepository.MarkFailedAsync(
                            attempt.AttemptGuid,
                            LocalSquarePaymentAttemptStatus.Unknown,
                            null,
                            null,
                            authorization.ResponseCode,
                            authorization.ResponseText ?? authorization.Message,
                            DateTimeOffset.UtcNow,
                            CancellationToken.None);
                    }
                });
        }

        return TerminalAttemptScope.Empty;
    }

    private async Task<TerminalReady> MarkTerminalApprovedAsync(LocalInstallmentOperation operation, string requestJson, CancellationToken cancellationToken)
    {
        if (operation.State == LocalInstallmentOperationState.TerminalApproved || operation.State == LocalInstallmentOperationState.ApiSubmitting)
        {
            return TerminalReady.Success(operation);
        }

        if (operation.State == LocalInstallmentOperationState.ResultUnknown)
        {
            return TerminalReady.Unknown("终端或 API 结果未知，请先恢复或由主管处理。");
        }

        var transitioned = await repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.Prepared], LocalInstallmentOperationState.TerminalApproved, DateTimeOffset.UtcNow, requestJson: requestJson, cancellationToken: cancellationToken);
        if (!transitioned)
        {
            return await ReadReadyOrUnknownAsync(operation.OperationGuid, cancellationToken);
        }

        var updated = await repository.GetAsync(operation.OperationGuid, cancellationToken)
            ?? throw new InvalidOperationException("分期操作状态未保存。");
        return TerminalReady.Success(updated);
    }

    private async Task<TerminalReady> ReadReadyOrUnknownAsync(Guid operationGuid, CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(operationGuid, cancellationToken);
        return current?.State is LocalInstallmentOperationState.TerminalApproved or LocalInstallmentOperationState.ApiSubmitting
            ? TerminalReady.Success(current)
            : current?.State == LocalInstallmentOperationState.Completed
                ? TerminalReady.Failed("操作已完成，请刷新分期列表。")
                : TerminalReady.Unknown("同一分期操作正在恢复或结果未知，请勿重试。");
    }

    private async Task<InstallmentOperationResult<InstallmentCreateResponse>> SubmitCreateAsync(LocalInstallmentOperation operation, InstallmentCreateRequest request, CancellationToken cancellationToken, bool allowStaleApiSubmittingClaim = false)
    {
        if (!await ClaimApiAsync(operation.OperationGuid, allowStaleApiSubmittingClaim, cancellationToken))
        {
            return new InstallmentOperationResult<InstallmentCreateResponse>(false, Message: "分期操作正在恢复或已完成。", RequiresReview: true);
        }

        try
        {
            var response = await apiClient.CreateAsync(request, cancellationToken);
            var local = ToLocalOrder(response.Details);
            if (!await repository.CompleteWithSnapshotAsync(operation.OperationGuid, [LocalInstallmentOperationState.ApiSubmitting], local, JsonSerializer.Serialize(response, JsonOptions), false, DateTimeOffset.UtcNow, cancellationToken))
            {
                return new InstallmentOperationResult<InstallmentCreateResponse>(false, Message: "创建结果已提交，正在安全对账，请勿重复收款。", RequiresReview: true);
            }
            return new InstallmentOperationResult<InstallmentCreateResponse>(true, response, local, response.Message);
        }
        catch (OperationCanceledException)
        {
            await MarkApiUnknownAsync(operation.OperationGuid, "创建 API 调用已取消，结果未知。", CancellationToken.None);
            return new InstallmentOperationResult<InstallmentCreateResponse>(false, Message: "创建结果未知，请勿再次收款。", RequiresReview: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MarkApiUnknownAsync(operation.OperationGuid, exception.Message, CancellationToken.None);
            return new InstallmentOperationResult<InstallmentCreateResponse>(false, Message: "分期创建结果未知，请勿再次收款。", RequiresReview: true);
        }
    }

    private async Task<InstallmentOperationResult<InstallmentAppendPaymentResponse>> SubmitRepaymentAsync(LocalInstallmentOperation operation, InstallmentAppendPaymentRequest request, CancellationToken cancellationToken, bool allowStaleApiSubmittingClaim = false)
    {
        if (!await ClaimApiAsync(operation.OperationGuid, allowStaleApiSubmittingClaim, cancellationToken))
        {
            return new InstallmentOperationResult<InstallmentAppendPaymentResponse>(false, Message: "补款操作正在恢复或已完成。", RequiresReview: true);
        }

        try
        {
            var response = await apiClient.AppendPaymentAsync(request, cancellationToken);
            var local = ToLocalOrder(response.Details);
            if (!await repository.CompleteWithSnapshotAsync(operation.OperationGuid, [LocalInstallmentOperationState.ApiSubmitting], local, JsonSerializer.Serialize(response, JsonOptions), false, DateTimeOffset.UtcNow, cancellationToken))
            {
                return new InstallmentOperationResult<InstallmentAppendPaymentResponse>(false, Message: "补款结果已提交，正在安全对账，请勿重复收款。", RequiresReview: true);
            }
            return new InstallmentOperationResult<InstallmentAppendPaymentResponse>(true, response, local, response.Message);
        }
        catch (OperationCanceledException)
        {
            await MarkApiUnknownAsync(operation.OperationGuid, "补款 API 调用已取消，结果未知。", CancellationToken.None);
            return new InstallmentOperationResult<InstallmentAppendPaymentResponse>(false, Message: "补款结果未知，请勿再次收款。", RequiresReview: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MarkApiUnknownAsync(operation.OperationGuid, exception.Message, CancellationToken.None);
            return new InstallmentOperationResult<InstallmentAppendPaymentResponse>(false, Message: "补款结果未知，请勿再次收款。", RequiresReview: true);
        }
    }

    private async Task<InstallmentOperationResult<InstallmentCancelResponse>> ContinueCancelAsync(LocalInstallmentOperation operation, string installmentNumber, PosSessionState session, bool supervisorResolved, CancellationToken cancellationToken)
    {
        var steps = await repository.GetRefundStepsAsync(operation.OperationGuid, cancellationToken);
        if ((operation.State is LocalInstallmentOperationState.ApiSubmitting or LocalInstallmentOperationState.ResultUnknown) && steps.All(IsRefundApproved))
        {
            return await PersistApprovedRefundSnapshotAndSubmitAsync(operation, steps, cancellationToken);
        }

        if ((operation.State == LocalInstallmentOperationState.ResultUnknown && !supervisorResolved) || steps.Any(step => step.State == LocalInstallmentRefundStepState.ResultUnknown))
        {
            return new InstallmentOperationResult<InstallmentCancelResponse>(false, Message: "退款结果未知，已锁定等待主管三态结案。", RequiresReview: true);
        }

        var expected = supervisorResolved
            ? new[] { LocalInstallmentOperationState.Prepared, LocalInstallmentOperationState.TerminalApproved, LocalInstallmentOperationState.ResultUnknown }
            : new[] { LocalInstallmentOperationState.Prepared, LocalInstallmentOperationState.TerminalApproved };
        var claimed = await repository.TryTransitionAsync(operation.OperationGuid, expected, LocalInstallmentOperationState.TerminalSubmitting, DateTimeOffset.UtcNow, cancellationToken: cancellationToken);
        if (!claimed)
        {
            return new InstallmentOperationResult<InstallmentCancelResponse>(false, Message: "取消操作正在恢复。", RequiresReview: true);
        }

        foreach (var step in steps.Where(step => step.State == LocalInstallmentRefundStepState.Prepared))
        {
            // 中文注释：Linkly 被主管确认“银行明确未退款”后必须先保存新 TxnRef，断电恢复才能继续查询同一笔重试。
            // LocalIp 退款不得复用原销售 TxnRef；唯一退款号需在调用终端前已落盘，避免 GetLast 命中原扣款。
            var retryReference = await ShouldGenerateLocalLinklyRetryReferenceAsync(step) && string.IsNullOrWhiteSpace(step.RefundReference)
                ? LinklyTerminalClient.BuildTxnRef(session)
                : null;
            if (!await repository.TryTransitionRefundStepAsync(
                    step.RefundStepGuid,
                    [LocalInstallmentRefundStepState.Prepared],
                    LocalInstallmentRefundStepState.TerminalSubmitting,
                    DateTimeOffset.UtcNow,
                    refundReference: retryReference,
                    cancellationToken: CancellationToken.None))
            {
                continue;
            }

            var terminalStep = retryReference is null ? step : step with { RefundReference = retryReference };
            var result = await RefundStepAsync(terminalStep, installmentNumber, session, cancellationToken);
            if (result.RequiresReview)
            {
                await repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.TerminalSubmitting], LocalInstallmentOperationState.ResultUnknown, DateTimeOffset.UtcNow, failureMessage: result.Message, cancellationToken: CancellationToken.None);
                return new InstallmentOperationResult<InstallmentCancelResponse>(false, Message: result.Message, RequiresReview: true);
            }

            if (!result.Succeeded)
            {
                var hasApproved = (await repository.GetRefundStepsAsync(operation.OperationGuid, cancellationToken)).Any(IsRefundApproved);
                // 终端明确拒绝没有退款副作用。保留已批准步骤，下一次取消只会执行仍处于 Prepared 的步骤。
                await repository.TryTransitionAsync(
                    operation.OperationGuid,
                    [LocalInstallmentOperationState.TerminalSubmitting],
                    hasApproved ? LocalInstallmentOperationState.TerminalApproved : LocalInstallmentOperationState.Prepared,
                    DateTimeOffset.UtcNow,
                    failureMessage: result.Message,
                    cancellationToken: CancellationToken.None);
                return new InstallmentOperationResult<InstallmentCancelResponse>(false, Message: result.Message);
            }
        }

        steps = await repository.GetRefundStepsAsync(operation.OperationGuid, cancellationToken);
        if (!steps.All(IsRefundApproved))
        {
            return new InstallmentOperationResult<InstallmentCancelResponse>(false, Message: "退款尚未全部完成，取消保持锁定。", RequiresReview: true);
        }

        return await PersistApprovedRefundSnapshotAndSubmitAsync(operation, steps, cancellationToken);
    }

    private async Task<InstallmentOperationResult<InstallmentCancelResponse>> PersistApprovedRefundSnapshotAndSubmitAsync(
        LocalInstallmentOperation operation,
        IReadOnlyList<LocalInstallmentRefundStep> steps,
        CancellationToken cancellationToken)
    {
        var original = Deserialize<InstallmentCancelRequest>(operation.RequestJson);
        var request = original with
        {
            Refunds = steps.Select(step => new InstallmentRefundPaymentCommandDto(
                DeterministicGuid($"refund-command:{step.RefundStepGuid:D}"),
                step.Method,
                step.Amount,
                step.RefundReference ?? step.OriginalReference,
                DeserializeTransactions(step.CardTransactionsJson),
                step.IdempotencyKey)).ToList()
        };

        // 退款全部获批后，先用 CAS 固化最终退款快照；读取确认成功后才调用取消 API。
        var snapshotPersisted = await repository.TryTransitionAsync(
            operation.OperationGuid,
            [LocalInstallmentOperationState.TerminalSubmitting, LocalInstallmentOperationState.TerminalApproved, LocalInstallmentOperationState.ResultUnknown],
            LocalInstallmentOperationState.TerminalApproved,
            DateTimeOffset.UtcNow,
            requestJson: JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken: CancellationToken.None);
        var persisted = await repository.GetAsync(operation.OperationGuid, CancellationToken.None);
        if (persisted is null || persisted.State is not (LocalInstallmentOperationState.TerminalApproved or LocalInstallmentOperationState.ApiSubmitting or LocalInstallmentOperationState.ResultUnknown))
        {
            return new InstallmentOperationResult<InstallmentCancelResponse>(false, Message: "退款快照正在由其他恢复流程处理，取消保持锁定。", RequiresReview: true);
        }

        // CAS 未命中说明其他恢复入口已经接管；只能使用其已保存的请求快照，不能用内存中的旧版本覆盖或重发不同退款明细。
        if (!snapshotPersisted)
        {
            request = Deserialize<InstallmentCancelRequest>(persisted.RequestJson);
        }

        // ApiSubmitting 是先前已发出取消 API 的边界；仅争抢过期租约，绝不再次遍历退款步骤。
        return await SubmitCancelAsync(persisted, request, cancellationToken, persisted.State == LocalInstallmentOperationState.ApiSubmitting);
    }

    private async Task<InstallmentOperationResult<InstallmentCancelResponse>> SubmitCancelAsync(LocalInstallmentOperation operation, InstallmentCancelRequest request, CancellationToken cancellationToken, bool allowStaleApiSubmittingClaim = false)
    {
        if (!await ClaimApiAsync(operation.OperationGuid, allowStaleApiSubmittingClaim, cancellationToken))
        {
            return new InstallmentOperationResult<InstallmentCancelResponse>(false, Message: "取消 API 正在恢复或结果未知。", RequiresReview: true);
        }

        try
        {
            var response = await apiClient.CancelAsync(request, cancellationToken);
            var local = ToLocalOrder(response.Details);
            if (!await repository.CompleteWithSnapshotAsync(operation.OperationGuid, [LocalInstallmentOperationState.ApiSubmitting], local, JsonSerializer.Serialize(response, JsonOptions), true, DateTimeOffset.UtcNow, cancellationToken))
            {
                return new InstallmentOperationResult<InstallmentCancelResponse>(false, Message: "取消结果已提交，正在安全对账，不会重复退款。", RequiresReview: true);
            }
            return new InstallmentOperationResult<InstallmentCancelResponse>(true, response, local, response.Message);
        }
        catch (OperationCanceledException)
        {
            await MarkApiUnknownAsync(operation.OperationGuid, "取消 API 调用已取消，结果未知。", CancellationToken.None);
            return new InstallmentOperationResult<InstallmentCancelResponse>(false, Message: "取消结果未知，退款不会重复执行。", RequiresReview: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MarkApiUnknownAsync(operation.OperationGuid, exception.Message, CancellationToken.None);
            return new InstallmentOperationResult<InstallmentCancelResponse>(false, Message: "取消 API 结果未知，退款不会重复执行。", RequiresReview: true);
        }
    }

    private async Task<InstallmentOperationResult<bool>> RefundStepAsync(LocalInstallmentRefundStep step, string installmentNumber, PosSessionState session, CancellationToken cancellationToken)
    {
        try
        {
            PaymentAuthorizationResult authorization = step.Method switch
            {
                PaymentMethodKind.Card => cardTerminalClient is IIdempotentCardRefundClient idempotentRefundClient
                    ? await idempotentRefundClient.RefundAsync(step.Amount, session, step.OriginalReference, ResolveRefundIdempotencyKey(step), cancellationToken)
                    : await cardTerminalClient.RefundAsync(step.Amount, session, step.OriginalReference, cancellationToken),
                PaymentMethodKind.Voucher => await voucherTenderClient.IssueRefundAsync(step.Amount, session, installmentNumber, step.IdempotencyKey, "取消分期退款", cancellationToken),
                _ => new PaymentAuthorizationResult(true, step.OriginalReference, AuthorizedAmount: step.Amount)
            };
            if (authorization.ResultUnknown)
            {
                await repository.TryTransitionRefundStepAsync(step.RefundStepGuid, [LocalInstallmentRefundStepState.TerminalSubmitting], LocalInstallmentRefundStepState.ResultUnknown, DateTimeOffset.UtcNow, failureMessage: authorization.Message ?? "退款结果未知。", cancellationToken: CancellationToken.None);
                return new InstallmentOperationResult<bool>(false, Message: "退款结果未知，等待主管结案。", RequiresReview: true);
            }

            if (!authorization.Approved)
            {
                // 明确拒绝意味着该退款没有金融副作用，回到 Prepared 供下一次取消安全重试。
                await repository.TryTransitionRefundStepAsync(step.RefundStepGuid, [LocalInstallmentRefundStepState.TerminalSubmitting], LocalInstallmentRefundStepState.Prepared, DateTimeOffset.UtcNow, failureMessage: authorization.Message, cancellationToken: CancellationToken.None);
                return new InstallmentOperationResult<bool>(false, Message: authorization.Message ?? "退款未获批准。");
            }

            if (!HasExactAuthorizedAmount(authorization, step.Amount) ||
                (step.Method == PaymentMethodKind.Card && !HasCardRefundEvidence(authorization)))
            {
                await repository.TryTransitionRefundStepAsync(
                    step.RefundStepGuid,
                    [LocalInstallmentRefundStepState.TerminalSubmitting],
                    LocalInstallmentRefundStepState.ResultUnknown,
                    DateTimeOffset.UtcNow,
                    failureMessage: "Refund approval evidence or amount did not match the persisted step.",
                    cancellationToken: CancellationToken.None);
                return new InstallmentOperationResult<bool>(false, Message: "退款批准信息无法安全核验，等待主管结案。", RequiresReview: true);
            }

            var approvedPersisted = await repository.TryTransitionRefundStepAsync(step.RefundStepGuid, [LocalInstallmentRefundStepState.TerminalSubmitting], LocalInstallmentRefundStepState.Approved, DateTimeOffset.UtcNow, authorization.Reference, authorization.CardTransactions is null ? null : JsonSerializer.Serialize(authorization.CardTransactions, JsonOptions), cancellationToken: CancellationToken.None);
            if (!approvedPersisted)
            {
                var current = (await repository.GetRefundStepsAsync(step.OperationGuid, CancellationToken.None)).FirstOrDefault(item => item.RefundStepGuid == step.RefundStepGuid);
                return current is not null && IsRefundApproved(current)
                    ? new InstallmentOperationResult<bool>(true, true)
                    : new InstallmentOperationResult<bool>(false, Message: "退款结果正在恢复或未知，保持锁定。", RequiresReview: true);
            }

            return new InstallmentOperationResult<bool>(true, true);
        }
        catch (OperationCanceledException)
        {
            await repository.TryTransitionRefundStepAsync(step.RefundStepGuid, [LocalInstallmentRefundStepState.TerminalSubmitting], LocalInstallmentRefundStepState.ResultUnknown, DateTimeOffset.UtcNow, failureMessage: "退款终端调用被取消，结果未知。", cancellationToken: CancellationToken.None);
            return new InstallmentOperationResult<bool>(false, Message: "退款结果未知，等待主管结案。", RequiresReview: true);
        }
        catch (Exception exception)
        {
            await repository.TryTransitionRefundStepAsync(
                step.RefundStepGuid,
                [LocalInstallmentRefundStepState.TerminalSubmitting],
                LocalInstallmentRefundStepState.ResultUnknown,
                DateTimeOffset.UtcNow,
                failureMessage: exception.Message,
                cancellationToken: CancellationToken.None);
            return new InstallmentOperationResult<bool>(false, Message: "退款结果未知，等待主管结案。", RequiresReview: true);
        }
    }

    private async Task<TerminalAttemptRecovery> ReadPersistedTerminalApprovalAsync(LocalInstallmentOperation operation, PosSessionState session, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(operation.TerminalAttemptGuid, out var attemptGuid))
        {
            return TerminalAttemptRecovery.Unknown;
        }

        var cardAttempt = cardPaymentAttemptRepository is null ? null : await cardPaymentAttemptRepository.GetAttemptAsync(attemptGuid, cancellationToken);
        if (cardAttempt is not null)
        {
            if (cardAttempt.Status is LocalCardPaymentAttemptStatus.Declined or LocalCardPaymentAttemptStatus.Cancelled)
            {
                return TerminalAttemptRecovery.RejectedResult;
            }

            if (cardTerminalClient is IInstallmentTerminalRecoveryClient recoveryClient)
            {
                return ToTerminalAttemptRecovery(await recoveryClient.RecoverLinklyAsync(cardAttempt, session, CancellationToken.None), cardAttempt.Amount);
            }

            return TerminalAttemptRecovery.Unknown;
        }

        var squareAttempt = squarePaymentAttemptRepository is null ? null : await squarePaymentAttemptRepository.GetAttemptAsync(attemptGuid, cancellationToken);
        if (squareAttempt is not null)
        {
            if (squareAttempt.Status == LocalSquarePaymentAttemptStatus.PaymentVerified)
            {
                if (string.IsNullOrWhiteSpace(squareAttempt.PaymentId))
                {
                    return TerminalAttemptRecovery.Unknown;
                }

                return new TerminalAttemptRecovery(true, false, $"SQ:{squareAttempt.PaymentId}",
                    [new CardTransactionDto("Square", squareAttempt.PaymentId, null, null, null, null, null, squareAttempt.ResponseCode, squareAttempt.ResponseText, null, squareAttempt.CompletedAt, squareAttempt.Amount, null)]);
            }

            if (squareAttempt.Status == LocalSquarePaymentAttemptStatus.Canceled)
            {
                return TerminalAttemptRecovery.RejectedResult;
            }

            if (cardTerminalClient is IInstallmentTerminalRecoveryClient recoveryClient)
            {
                return ToTerminalAttemptRecovery(await recoveryClient.RecoverSquareAsync(squareAttempt, session, CancellationToken.None), squareAttempt.Amount);
            }

            return TerminalAttemptRecovery.Unknown;
        }

        return TerminalAttemptRecovery.Unknown;
    }

    private static TerminalAttemptRecovery ToTerminalAttemptRecovery(PaymentAuthorizationResult authorization, decimal expectedAmount)
    {
        if (authorization.Approved && authorization.AuthorizedAmount is decimal amount && Math.Abs(amount - expectedAmount) < 0.001m && authorization.CardTransactions is { Count: > 0 })
        {
            return new TerminalAttemptRecovery(true, false, authorization.Reference, authorization.CardTransactions);
        }

        // 中文注释：GetLast/网络查询失败也会返回未批准；没有终端最终证据绝不能把已扣款操作解锁。
        return !authorization.ResultUnknown && !authorization.Approved && HasExplicitTerminalRejection(authorization)
            ? TerminalAttemptRecovery.RejectedResult
            : TerminalAttemptRecovery.Unknown;
    }

    private static bool HasExplicitTerminalRejection(PaymentAuthorizationResult authorization)
    {
        if (authorization.ResultUnknown || authorization.Approved)
        {
            return false;
        }

        return string.Equals(authorization.ResponseCode, "05", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.ResponseCode, "C0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.ResponseCode, "CN", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(authorization.ResponseText) &&
             (authorization.ResponseText.Contains("DECLINED", StringComparison.OrdinalIgnoreCase) ||
              authorization.ResponseText.Contains("REJECTED", StringComparison.OrdinalIgnoreCase) ||
              authorization.ResponseText.Contains("CANCELLED", StringComparison.OrdinalIgnoreCase) ||
              authorization.ResponseText.Contains("CANCELED", StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<InstallmentOperationRecoveryResult> RecoverCreateAsync(LocalInstallmentOperation operation, PosSessionState session, CancellationToken cancellationToken)
    {
        var request = Deserialize<InstallmentCreateRequest>(operation.RequestJson);
        if (request.DownPayment.Method == PaymentMethodKind.Card && request.DownPayment.CardTransactions is not { Count: > 0 })
        {
            var terminalRecovery = await ReadPersistedTerminalApprovalAsync(operation, session, cancellationToken);
            if (terminalRecovery.Rejected)
            {
                await repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.ResultUnknown], LocalInstallmentOperationState.Failed, DateTimeOffset.UtcNow, failureMessage: "终端明确拒绝或取消。", cancellationToken: CancellationToken.None);
                return new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, LocalInstallmentOperationState.Failed, false, "终端已明确拒绝或取消，可重新操作。");
            }

            if (terminalRecovery.Approved)
            {
                request = request with { DownPayment = request.DownPayment with { Reference = terminalRecovery.Reference, CardTransactions = terminalRecovery.Transactions } };
                if (!await repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.ResultUnknown], LocalInstallmentOperationState.TerminalApproved, DateTimeOffset.UtcNow, requestJson: JsonSerializer.Serialize(request, JsonOptions), cancellationToken: CancellationToken.None))
                {
                    return new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, LocalInstallmentOperationState.ResultUnknown, false, "终端批准正在由其他恢复入口对账。");
                }

                operation = (await repository.GetAsync(operation.OperationGuid, cancellationToken))!;
            }
            else
            return new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, LocalInstallmentOperationState.ResultUnknown, false, "未确认终端批准，禁止自动重扣。");
        }

        var result = await SubmitCreateAsync(operation, request, cancellationToken, allowStaleApiSubmittingClaim: true);
        return new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, result.Succeeded ? LocalInstallmentOperationState.Completed : LocalInstallmentOperationState.ResultUnknown, result.Succeeded, result.Message);
    }

    private async Task<InstallmentOperationRecoveryResult> RecoverRepaymentAsync(LocalInstallmentOperation operation, PosSessionState session, CancellationToken cancellationToken)
    {
        var request = Deserialize<InstallmentAppendPaymentRequest>(operation.RequestJson);
        if (request.Method == PaymentMethodKind.Card && request.CardTransactions is not { Count: > 0 })
        {
            var terminalRecovery = await ReadPersistedTerminalApprovalAsync(operation, session, cancellationToken);
            if (terminalRecovery.Rejected)
            {
                await repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.ResultUnknown], LocalInstallmentOperationState.Failed, DateTimeOffset.UtcNow, failureMessage: "终端明确拒绝或取消。", cancellationToken: CancellationToken.None);
                return new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, LocalInstallmentOperationState.Failed, false, "终端已明确拒绝或取消，可重新操作。");
            }

            if (terminalRecovery.Approved)
            {
                request = request with { Reference = terminalRecovery.Reference, CardTransactions = terminalRecovery.Transactions };
                if (!await repository.TryTransitionAsync(operation.OperationGuid, [LocalInstallmentOperationState.ResultUnknown], LocalInstallmentOperationState.TerminalApproved, DateTimeOffset.UtcNow, requestJson: JsonSerializer.Serialize(request, JsonOptions), cancellationToken: CancellationToken.None))
                {
                    return new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, LocalInstallmentOperationState.ResultUnknown, false, "终端批准正在由其他恢复入口对账。");
                }

                operation = (await repository.GetAsync(operation.OperationGuid, cancellationToken))!;
            }
            else
            return new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, LocalInstallmentOperationState.ResultUnknown, false, "未确认终端批准，禁止自动重扣。");
        }

        var result = await SubmitRepaymentAsync(operation, request, cancellationToken, allowStaleApiSubmittingClaim: true);
        return new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, result.Succeeded ? LocalInstallmentOperationState.Completed : LocalInstallmentOperationState.ResultUnknown, result.Succeeded, result.Message);
    }

    private async Task<InstallmentOperationRecoveryResult> RecoverCancelAsync(LocalInstallmentOperation operation, PosSessionState session, CancellationToken cancellationToken)
    {
        var steps = await repository.GetRefundStepsAsync(operation.OperationGuid, cancellationToken);
        if (!steps.All(IsRefundApproved))
        {
            return new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, operation.State, false, "退款未全部确认，保持锁定。" );
        }

        var result = await PersistApprovedRefundSnapshotAndSubmitAsync(operation, steps, cancellationToken);
        return new InstallmentOperationRecoveryResult(operation.OperationGuid, operation.Kind, result.Succeeded ? LocalInstallmentOperationState.Completed : LocalInstallmentOperationState.ResultUnknown, result.Succeeded, result.Message);
    }

    private async Task<bool> ClaimApiAsync(Guid operationGuid, bool allowStaleApiSubmittingClaim, CancellationToken cancellationToken)
    {
        return await repository.TryClaimApiAsync(
            operationGuid,
            _apiClaimToken,
            allowStaleApiSubmittingClaim,
            DateTimeOffset.UtcNow,
            ApiClaimLease,
            cancellationToken);
    }

    private async Task MarkApiUnknownAsync(Guid operationGuid, string message, CancellationToken cancellationToken)
    {
        await repository.TryTransitionAsync(operationGuid, [LocalInstallmentOperationState.ApiSubmitting], LocalInstallmentOperationState.ResultUnknown, DateTimeOffset.UtcNow, failureMessage: message, cancellationToken: cancellationToken);
    }

    private static LocalFinancialSupervisorResolution BuildInstallmentRefundSupervisorJournal(
        LocalInstallmentOperation operation,
        LocalInstallmentRefundStep step,
        InstallmentRefundSupervisorResolution resolution,
        string environment,
        DateTimeOffset resolvedAt)
    {
        var resolutionGuid = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();
        var processor = string.IsNullOrWhiteSpace(operation.TerminalProcessor)
            ? step.Method.ToString()
            : operation.TerminalProcessor;
        var auditEvent = new OperationAuditEventDto
        {
            EventId = auditEventId,
            OccurredAtUtc = resolvedAt,
            OperationType = "INSTALLMENT_REFUND_SUPERVISOR_RESOLUTION",
            Outcome = resolution.Decision.ToString(),
            CashierId = resolution.OperatorId,
            UserGuid = resolution.OperatorUserGuid,
            CashierName = resolution.OperatorName,
            StoreCode = operation.StoreCode,
            DeviceCode = operation.DeviceCode,
            CorrelationId = operation.OperationGuid.ToString("D"),
            PaymentMethod = processor,
            ReasonCode = resolution.Decision.ToString(),
            SafeMessage = resolution.Reason,
            PaymentAmount = Math.Abs(step.Amount),
            Properties = new Dictionary<string, string?>
            {
                ["operationGuid"] = operation.OperationGuid.ToString("D"),
                ["refundStepGuid"] = step.RefundStepGuid.ToString("D"),
                ["evidence"] = resolution.Evidence,
                ["financialReference"] = resolution.RefundReference,
                ["retryReference"] = step.IdempotencyKey
            }
        };
        return new LocalFinancialSupervisorResolution(
            resolutionGuid,
            LocalFinancialSupervisorResolutionTarget.InstallmentRefund,
            processor,
            environment,
            operation.StoreCode,
            operation.DeviceCode,
            null,
            step.RefundStepGuid,
            operation.OperationGuid,
            null,
            resolution.Decision.ToString(),
            resolution.OperatorId,
            resolution.OperatorUserGuid,
            resolution.OperatorName,
            resolution.Reason,
            resolution.Evidence,
            resolution.RefundReference,
            step.IdempotencyKey,
            resolvedAt,
            auditEventId,
            JsonSerializer.Serialize(auditEvent, JsonOptions));
    }

    private static LocalInstallmentOperation CreateOperation(Guid operationGuid, LocalInstallmentOperationKind kind, Guid installmentGuid, Guid? paymentGuid, string storeCode, string deviceCode, string cashierId, string idempotencyKey, string requestJson)
    {
        var now = DateTimeOffset.UtcNow;
        return new LocalInstallmentOperation(operationGuid, kind, installmentGuid, paymentGuid, storeCode, deviceCode, cashierId, idempotencyKey, requestJson, LocalInstallmentOperationState.Prepared, null, null, null, null, now, now);
    }

    private static LocalInstallmentOrder ToLocalOrder(InstallmentDetailsDto details) => new(
        details.InstallmentGuid, details.InstallmentGuid, details.InstallmentNumber, details.StoreCode, details.DeviceCode,
        details.CashierId, details.CashierName, details.CustomerName, details.CustomerPhone, details.CreatedAt, DateTimeOffset.UtcNow,
        details.TotalAmount, details.MinimumDownPayment, details.DownPaymentAmount, details.PaidAmount, details.BalanceAmount,
        details.Status, details.Lines, details.Payments, details.PickupInfo, details.Note, details.CancellationInfo);

    private static string EnsureIdempotencyKey(string? value, Guid scope) => string.IsNullOrWhiteSpace(value) ? $"{scope:D}:installment" : value.Trim();
    private static bool IsRefundApproved(LocalInstallmentRefundStep step) => step.State is LocalInstallmentRefundStepState.Approved or LocalInstallmentRefundStepState.Completed or LocalInstallmentRefundStepState.SupervisorConfirmedRefunded;
    private static bool HasExactAuthorizedAmount(PaymentAuthorizationResult authorization, decimal expectedAmount)
    {
        return authorization.AuthorizedAmount is decimal authorizedAmount &&
            authorizedAmount > 0m &&
            decimal.Round(authorizedAmount, 2, MidpointRounding.AwayFromZero) == decimal.Round(expectedAmount, 2, MidpointRounding.AwayFromZero);
    }

    private static bool HasCardRefundEvidence(PaymentAuthorizationResult authorization) =>
        !string.IsNullOrWhiteSpace(authorization.Reference) && authorization.CardTransactions is { Count: > 0 };
    private static bool IsLinklyRefundStep(LocalInstallmentRefundStep step) =>
        step.Method == PaymentMethodKind.Card &&
        (DeserializeTransactions(step.CardTransactionsJson)?.Any(transaction =>
            string.Equals(transaction.Processor, "ANZ", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(transaction.Processor, "Linkly", StringComparison.OrdinalIgnoreCase)) == true ||
         step.OriginalReference?.StartsWith("ANZ:", StringComparison.OrdinalIgnoreCase) == true);

    private async Task<bool> ShouldGenerateLocalLinklyRetryReferenceAsync(LocalInstallmentRefundStep step)
    {
        if (!IsLinklyRefundStep(step) || cardTerminalSettingsProvider is null)
        {
            return false;
        }

        var settings = await cardTerminalSettingsProvider.GetSettingsAsync(CancellationToken.None);
        return settings.Processor == CardProcessorKind.Linkly &&
            CardTerminalSettings.NormalizeLinklyConnectionMode(settings.LinklyConnectionMode) == LinklyConnectionMode.LocalIp;
    }

    // 中文注释：Square 复用业务幂等键；LocalIp Linkly 复用已落盘的退款 TxnRef，原交易引用仍独立传递。
    private static string ResolveRefundIdempotencyKey(LocalInstallmentRefundStep step) =>
        IsLinklyRefundStep(step) && !string.IsNullOrWhiteSpace(step.RefundReference)
            ? step.RefundReference
            : step.IdempotencyKey;

    private static IReadOnlyList<CardTransactionDto>? DeserializeTransactions(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<IReadOnlyList<CardTransactionDto>>(json, JsonOptions);
    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidOperationException("分期操作快照无效。");
    private static Guid DeterministicGuid(string value)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash);
    }

    private sealed record TerminalReady(bool Succeeded, LocalInstallmentOperation? Operation, string? Message, bool RequiresReview)
    {
        public static TerminalReady Success(LocalInstallmentOperation operation) => new(true, operation, null, false);
        public static TerminalReady Failed(string message) => new(false, null, message, false);
        public static TerminalReady Unknown(string message) => new(false, null, message, true);
    }

    private sealed record TerminalAttemptRecovery(
        bool Approved,
        bool Rejected,
        string? Reference,
        IReadOnlyList<CardTransactionDto>? Transactions)
    {
        public static TerminalAttemptRecovery Unknown { get; } = new(false, false, null, null);
        public static TerminalAttemptRecovery RejectedResult { get; } = new(false, true, null, null);
    }

    private sealed class TerminalAttemptScope(
        Guid? attemptGuid,
        string? processor,
        Func<IDisposable?>? beginContext,
        Func<PaymentAuthorizationResult, bool>? hasVerifiedApprovalEvidence,
        Func<PaymentAuthorizationResult, CancellationToken, Task>? recordOutcomeAsync) : IDisposable
    {
        public static TerminalAttemptScope Empty { get; } = new(null, null, null, null, null);
        public Guid? AttemptGuid { get; } = attemptGuid;
        public string? Processor { get; } = processor;

        public IDisposable? BeginContext() => beginContext?.Invoke();

        public bool HasVerifiedApprovalEvidence(PaymentAuthorizationResult authorization) =>
            hasVerifiedApprovalEvidence?.Invoke(authorization) ?? true;

        public Task RecordOutcomeAsync(PaymentAuthorizationResult authorization, CancellationToken cancellationToken) =>
            recordOutcomeAsync?.Invoke(authorization, cancellationToken) ?? Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
