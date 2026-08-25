using System.Text.Json;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;

namespace Hbpos.Client.Tests;

public sealed class CardPaymentHandoffQualificationTests
{
    private static readonly Guid AttemptGuid = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid OrderGuid = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly PosSessionState Session = new(
        "POS",
        "S001",
        "Store 1",
        "D001",
        "C001",
        "Cashier",
        true,
        0);

    [Fact]
    public void SelectCandidate_requires_unique_matching_attempt_with_complete_draft()
    {
        var request = CreateRequest();
        var matching = CreateQueueItem(
            AttemptGuid,
            SerializeDraft(request));

        var candidate = CardPaymentHandoffQualification.SelectCandidate([matching], request);

        Assert.Equal(new CardPaymentHandoffCandidate(matching.Processor, matching.AttemptGuid), candidate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    public void SelectCandidate_rejects_missing_or_invalid_draft(string? draftJson)
    {
        var request = CreateRequest();
        var item = CreateQueueItem(AttemptGuid, draftJson);

        Assert.Null(CardPaymentHandoffQualification.SelectCandidate([item], request));
    }

    [Fact]
    public void SelectCandidate_rejects_incomplete_exact_draft()
    {
        var request = CreateRequest();
        var incompleteDraft = CreateDraft(request) with { OrderGuid = Guid.Empty };
        var item = CreateQueueItem(AttemptGuid, JsonSerializer.Serialize(incompleteDraft));

        Assert.Null(CardPaymentHandoffQualification.SelectCandidate([item], request));
    }

    [Fact]
    public void SelectCandidate_uses_exact_key_and_ignores_identical_unrelated_attempts()
    {
        var request = CreateRequest();
        var unrelated = CreateQueueItem(Guid.NewGuid(), SerializeDraft(request));
        var exact = CreateQueueItem(AttemptGuid, SerializeDraft(request));

        var candidate = CardPaymentHandoffQualification.SelectCandidate([unrelated, exact], request);

        Assert.Equal(new CardPaymentHandoffCandidate(CardProcessorKind.Linkly, AttemptGuid), candidate);
    }

    [Fact]
    public void SelectCandidate_rejects_missing_wrong_provider_key_or_order_guid()
    {
        var exact = CreateQueueItem(AttemptGuid, SerializeDraft(CreateRequest()));

        Assert.Null(CardPaymentHandoffQualification.SelectCandidate([exact], CreateRequest(includeIdentity: false)));
        Assert.Null(CardPaymentHandoffQualification.SelectCandidate(
            [exact],
            CreateRequest(processor: CardProcessorKind.Square)));
        Assert.Null(CardPaymentHandoffQualification.SelectCandidate(
            [exact],
            CreateRequest(attemptGuid: Guid.NewGuid())));
        Assert.Null(CardPaymentHandoffQualification.SelectCandidate(
            [exact],
            CreateRequest(orderGuid: Guid.NewGuid())));
    }

    [Fact]
    public void CandidateStillMatches_rejects_missing_or_mismatched_attempt_guid()
    {
        var request = CreateRequest();
        var item = CreateQueueItem(
            AttemptGuid,
            SerializeDraft(request));
        var missing = new CardPaymentHandoffCandidate(
            item.Processor,
            Guid.Parse("20000000-0000-0000-0000-000000000099"));

        Assert.False(CardPaymentHandoffQualification.CandidateStillMatches([], missing, request));
        Assert.False(CardPaymentHandoffQualification.CandidateStillMatches([item], missing, request));
        Assert.True(CardPaymentHandoffQualification.CandidateStillMatches(
            [item],
            new CardPaymentHandoffCandidate(item.Processor, item.AttemptGuid),
            request));
    }

    private static CardPaymentHandoffRequest CreateRequest(
        bool includeIdentity = true,
        CardProcessorKind processor = CardProcessorKind.Linkly,
        Guid? attemptGuid = null,
        Guid? orderGuid = null)
    {
        var snapshot = new PosCartSnapshot(
        [
            new PosCartLineSnapshot(
                "S001",
                "SKU-HANDOFF",
                null,
                "Handoff Tea",
                "930HANDOFF",
                "ITEM-HANDOFF",
                null,
                1m,
                10m,
                0m,
                null,
                PriceSourceKind.StoreRetailPrice,
                "Store price")
        ]);
        return new CardPaymentHandoffRequest(
            Session,
            snapshot,
            [],
            10m,
            includeIdentity ? new CardRecoveryAttemptKey(processor, attemptGuid ?? AttemptGuid) : null,
            includeIdentity ? orderGuid ?? OrderGuid : null);
    }

    private static string SerializeDraft(CardPaymentHandoffRequest request) =>
        JsonSerializer.Serialize(CreateDraft(request));

    private static CardPaymentOrderDraft CreateDraft(CardPaymentHandoffRequest request) =>
        new(
            request.RecoveryOrderGuid ?? OrderGuid,
            request.Session,
            request.CartSnapshot,
            request.CurrentTenders,
            request.ActualAmount,
            request.ActualAmount,
            "P",
            null,
            DateTimeOffset.Parse("2026-08-21T09:01:00+10:00"));

    private static CardRecoveryQueueItem CreateQueueItem(Guid attemptGuid, string? draftJson) =>
        new(
            CardProcessorKind.Linkly,
            attemptGuid,
            "Purchase",
            10m,
            "S001",
            "D001",
            "C001",
            "Production",
            "ResultUnknown",
            DateTimeOffset.Parse("2026-08-21T09:01:00+10:00"),
            DateTimeOffset.Parse("2026-08-21T09:02:00+10:00"),
            draftJson);
}
