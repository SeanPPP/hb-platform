using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed record CardPaymentHandoffCandidate(
    CardProcessorKind Processor,
    Guid AttemptGuid);

public sealed record CardPaymentHandoffRequest(
    PosSessionState Session,
    PosCartSnapshot CartSnapshot,
    IReadOnlyList<PaymentTender> CurrentTenders,
    decimal ActualAmount);
