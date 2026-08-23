using System.Text.Json;
using System.Text.Json.Serialization;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Tests;

public sealed class CardRecoveryCartMaterializerTests
{
    [Fact]
    public void TryPrepare_returns_false_for_semantically_invalid_cart_snapshot()
    {
        var draft = CreateDraft();
        var invalidSnapshot = draft.CartSnapshot with
        {
            Lines = [draft.CartSnapshot.Lines[0] with { Quantity = 0m }]
        };
        var json = JsonSerializer.Serialize(
            draft with { CartSnapshot = invalidSnapshot },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var prepared = CardRecoveryCartMaterializer.TryPrepare(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            out var materialized);

        Assert.False(prepared);
        Assert.Null(materialized);
    }

    [Fact]
    public void TryPrepare_returns_false_when_order_identity_is_missing()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(CreateDraft() with { OrderGuid = Guid.Empty }, options);

        var prepared = CardRecoveryCartMaterializer.TryPrepare(json, options, out var materialized);

        Assert.False(prepared);
        Assert.Null(materialized);
    }

    [Fact]
    public void TryPrepare_normalizes_a_valid_snapshot_in_an_isolated_cart()
    {
        var draft = CreateDraft();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(draft, options);

        var prepared = CardRecoveryCartMaterializer.TryPrepare(json, options, out var materialized);

        Assert.True(prepared);
        Assert.NotNull(materialized);
        Assert.NotSame(draft.CartSnapshot, materialized.CartSnapshot);
        Assert.Equal(draft.CartSnapshot.Lines, materialized.CartSnapshot.Lines);
        Assert.Equal(draft.OrderGuid, materialized.OrderGuid);
    }

    [Fact]
    public void TryPrepare_propagates_out_of_memory_exception()
    {
        var exception = new OutOfMemoryException("fatal materialization failure");
        var options = CreateThrowingOptions(exception);

        var thrown = Assert.Throws<OutOfMemoryException>(() =>
            CardRecoveryCartMaterializer.TryPrepare("{}", options, out _));

        Assert.Same(exception, thrown);
    }

    [Fact]
    public void TryPrepare_propagates_stack_overflow_exception()
    {
        var exception = new StackOverflowException("fatal materialization failure");
        var options = CreateThrowingOptions(exception);

        var thrown = Assert.Throws<StackOverflowException>(() =>
            CardRecoveryCartMaterializer.TryPrepare("{}", options, out _));

        Assert.Same(exception, thrown);
    }

    private static CardPaymentOrderDraft CreateDraft()
    {
        var cart = new PosCartService();
        cart.AddItem(new SellableItemDto(
            StoreCode: "S001",
            ProductCode: "SKU-MATERIALIZE",
            ReferenceCode: null,
            DisplayName: "Recovery Tea",
            LookupCode: "930000000001",
            ItemNumber: "SKU-MATERIALIZE",
            Barcode: "930000000001",
            RetailPrice: 4m,
            PriceSource: PriceSourceKind.StoreRetailPrice,
            PriceSourceLabel: PriceSourceKind.StoreRetailPrice.ToString(),
            QuantityFactor: 1m,
            UpdatedAt: DateTimeOffset.Parse("2026-08-23T00:00:00Z")));
        return new CardPaymentOrderDraft(
            Guid.NewGuid(),
            new PosSessionState("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0),
            cart.CreateSnapshot(),
            [],
            4m,
            4m,
            "P",
            null,
            DateTimeOffset.Parse("2026-08-23T00:00:00Z"));
    }

    private static JsonSerializerOptions CreateThrowingOptions(Exception exception)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ThrowingDraftConverter(exception));
        return options;
    }

    private sealed class ThrowingDraftConverter(Exception exception) : JsonConverter<CardPaymentOrderDraft>
    {
        public override CardPaymentOrderDraft? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) => throw exception;

        public override void Write(
            Utf8JsonWriter writer,
            CardPaymentOrderDraft value,
            JsonSerializerOptions options) => throw new NotSupportedException();
    }
}
