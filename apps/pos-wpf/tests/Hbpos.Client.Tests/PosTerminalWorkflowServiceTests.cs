using System.Collections.Concurrent;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Client.Tests;

public sealed class PosTerminalWorkflowServiceTests
{
    [Fact]
    public void Process_scan_auto_adds_single_exact_match()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var item = CreateItem("SKU-201", "Workflow Water", "930201", PriceSourceKind.StoreRetailPrice, 2.5m);
        index.ReplaceAll([item]);
        var service = new PosTerminalWorkflowService(index, cart);

        var result = service.ProcessScan(Session, "930201", preferExactLookup: false, source: "manual");

        Assert.Equal("pos.status.added", result.StatusKey);
        Assert.True(result.ClearScanText);
        Assert.False(result.MatchesPopupOpen);
        Assert.Same(item, result.SelectedItem);
        var line = Assert.Single(cart.Lines);
        Assert.Same(line, result.SelectedCartLine);
        Assert.Equal("Workflow Water", line.DisplayName);
    }

    [Fact]
    public void Process_scan_keeps_duplicate_exact_matches_for_selection()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var first = CreateItem("SKU-211", "Workflow Apple Small", "930211", PriceSourceKind.StoreRetailPrice, 1.5m);
        var second = CreateItem("SKU-212", "Workflow Apple Large", "930211", PriceSourceKind.StoreRetailPrice, 2.5m);
        index.ReplaceAll([first, second]);
        var service = new PosTerminalWorkflowService(index, cart);

        var result = service.ProcessScan(Session, "930211", preferExactLookup: true, source: "raw");

        Assert.Equal("pos.status.multipleMatches", result.StatusKey);
        Assert.False(result.ClearScanText);
        Assert.True(result.MatchesPopupOpen);
        Assert.Equal(2, Assert.IsAssignableFrom<IReadOnlyList<SellableItemDto>>(result.Matches).Count);
        Assert.Empty(cart.Lines);
    }

    [Fact]
    public async Task Process_scan_async_remote_match_adds_cart_line_after_local_miss()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var remoteItem = CreateItem("SKU-213", "Remote Workflow Juice", "930213", PriceSourceKind.StoreRetailPrice, 6.5m);
        var lookupCalls = 0;
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (storeCode, lookupCode, _) =>
            {
                Assert.Equal("S001", storeCode);
                Assert.Equal("930213", lookupCode);
                Interlocked.Increment(ref lookupCalls);
                return Task.FromResult(new RemoteLookupRefreshResult(storeCode, lookupCode, Found: true, remoteItem, DeletedCount: 0));
            });

        var result = await service.ProcessScanAsync(Session, "930213", preferExactLookup: true, source: "raw");

        Assert.Equal("pos.status.added", result.StatusKey);
        Assert.True(result.ClearScanText);
        Assert.False(result.MatchesPopupOpen);
        Assert.Same(remoteItem, result.SelectedItem);
        Assert.Equal(1, Volatile.Read(ref lookupCalls));
        var line = Assert.Single(cart.Lines);
        Assert.Same(line, result.SelectedCartLine);
        Assert.Equal("Remote Workflow Juice", line.DisplayName);
        Assert.Equal(6.5m, line.UnitPrice);
        Assert.Same(remoteItem, Assert.Single(index.FindExactMatches("S001", "930213")));
    }

    [Fact]
    public async Task Process_scan_async_remote_not_found_keeps_no_local_match_after_local_miss()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (storeCode, lookupCode, _) =>
                Task.FromResult(new RemoteLookupRefreshResult(storeCode, lookupCode, Found: false, Item: null, DeletedCount: 1)));

        var result = await service.ProcessScanAsync(Session, "930214", preferExactLookup: true, source: "raw");

        Assert.Equal("pos.status.noLocalMatch", result.StatusKey);
        Assert.False(result.ClearScanText);
        Assert.False(result.MatchesPopupOpen);
        Assert.Empty(cart.Lines);
        Assert.Empty(index.FindExactMatches("S001", "930214"));
    }

    [Fact]
    public async Task Process_scan_async_offline_does_not_remote_lookup_after_local_miss()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (_, _, _) => throw new InvalidOperationException("Remote lookup should not run while offline."));

        var result = await service.ProcessScanAsync(
            Session with { IsOnline = false },
            "930215",
            preferExactLookup: true,
            source: "raw");

        Assert.Equal("pos.status.noLocalMatch", result.StatusKey);
        Assert.Empty(cart.Lines);
    }

    [Fact]
    public async Task Process_scan_async_remote_exception_keeps_no_local_match_after_local_miss()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (_, _, _) => throw new InvalidOperationException("remote failed"));

        var result = await service.ProcessScanAsync(Session, "930216", preferExactLookup: true, source: "raw");

        Assert.Equal("pos.status.noLocalMatch", result.StatusKey);
        Assert.Empty(cart.Lines);
        Assert.Empty(index.FindExactMatches("S001", "930216"));
    }

    [Fact]
    public async Task Process_scan_async_remote_canceled_keeps_no_local_match_after_local_miss()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (_, _, cancellationToken) => throw new OperationCanceledException(cancellationToken));

        var result = await service.ProcessScanAsync(Session, "930217", preferExactLookup: true, source: "raw");

        Assert.Equal("pos.status.noLocalMatch", result.StatusKey);
        Assert.Empty(cart.Lines);
        Assert.Empty(index.FindExactMatches("S001", "930217"));
    }

    [Fact]
    public async Task Process_scan_async_concurrent_same_local_miss_shares_remote_lookup_and_adds_each_scan()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var remoteItem = CreateItem("SKU-218", "Remote Workflow Cola", "930218", PriceSourceKind.StoreRetailPrice, 3.25m);
        var remoteLookup = new TaskCompletionSource<RemoteLookupRefreshResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookupCalls = 0;
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (storeCode, lookupCode, _) =>
            {
                Interlocked.Increment(ref lookupCalls);
                return remoteLookup.Task;
            });

        var first = service.ProcessScanAsync(Session, "930218", preferExactLookup: true, source: "raw");
        await WaitUntilAsync(() => Volatile.Read(ref lookupCalls) == 1);
        var second = service.ProcessScanAsync(Session, "930218", preferExactLookup: true, source: "raw");

        remoteLookup.SetResult(new RemoteLookupRefreshResult("S001", "930218", Found: true, remoteItem, DeletedCount: 0));
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal("pos.status.added", result.StatusKey));
        Assert.Equal(1, Volatile.Read(ref lookupCalls));
        var line = Assert.Single(cart.Lines);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(6.5m, cart.TotalAmount);
        Assert.Same(remoteItem, Assert.Single(index.FindExactMatches("S001", "930218")));
    }

    [Fact]
    public void Modify_selected_line_quantity_rejects_decimal_values()
    {
        var cart = new PosCartService();
        var item = CreateItem("SKU-221", "Workflow Tea", "930221", PriceSourceKind.StoreRetailPrice, 4m);
        var line = cart.AddItem(item);
        var service = new PosTerminalWorkflowService(new LocalSellableItemIndex(), cart);

        var result = service.ModifySelectedLineQuantity(line, "1.5");

        Assert.Equal("cart.status.quantityMustBeInteger", result.StatusKey);
        Assert.False(result.ClearKeypadBuffer);
        Assert.Equal(1m, line.Quantity);
    }

    [Fact]
    public void Guard_payment_blocks_zero_price_and_non_integer_quantity()
    {
        var zeroPriceCart = new PosCartService();
        zeroPriceCart.AddItem(CreateItem("SKU-231", "Zero Tea", "930231", PriceSourceKind.StoreRetailPrice, 0m));
        var zeroPriceService = new PosTerminalWorkflowService(new LocalSellableItemIndex(), zeroPriceCart);

        var zeroPriceResult = zeroPriceService.GuardPayment();

        Assert.False(zeroPriceResult.PaymentAllowed);
        Assert.Equal("cart.status.zeroPriceItem", zeroPriceResult.StatusKey);

        var invalidQuantityCart = new PosCartService();
        var invalidQuantityLine = invalidQuantityCart.AddItem(CreateItem("SKU-232", "Fraction Tea", "930232", PriceSourceKind.StoreRetailPrice, 5m));
        SetUnsafeQuantity(invalidQuantityLine, 1.5m);
        var invalidQuantityService = new PosTerminalWorkflowService(new LocalSellableItemIndex(), invalidQuantityCart);

        var invalidQuantityResult = invalidQuantityService.GuardPayment();

        Assert.False(invalidQuantityResult.PaymentAllowed);
        Assert.Equal("cart.status.quantityMustBeInteger", invalidQuantityResult.StatusKey);
    }

    [Fact]
    public void Guard_payment_allows_return_lines_when_net_amount_is_negative()
    {
        var cart = new PosCartService();
        cart.AddReturnLine(new ReturnCartLineRequest(
            "S001",
            "SKU-RET-GUARD",
            null,
            "Guard Refund Tea",
            "930233",
            "ITEM-RET-GUARD",
            null,
            1m,
            5m,
            PriceSourceKind.StoreRetailPrice,
            PriceSourceKind.StoreRetailPrice.ToString(),
            "RETURN-GUARD-1",
            Guid.NewGuid(),
            Guid.NewGuid()));
        var service = new PosTerminalWorkflowService(new LocalSellableItemIndex(), cart);

        var result = service.GuardPayment();

        Assert.True(result.PaymentAllowed);
        Assert.Null(result.StatusKey);
    }

    [Fact]
    public async Task Add_selected_item_remote_delete_keeps_local_cart_line()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var item = CreateItem("SKU-241", "Retired Workflow Snack", "930241", PriceSourceKind.StoreRetailPrice, 4.2m);
        var remoteLookup = new TaskCompletionSource<RemoteLookupRefreshResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalogReloaded = new TaskCompletionSource<IReadOnlyList<SellableItemDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        index.ReplaceAll([item]);
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (_, _, _) => remoteLookup.Task,
            reloadCatalogAsync: _ =>
            {
                index.ReplaceAll([]);
                return Task.FromResult<IReadOnlyList<SellableItemDto>>([]);
            });
        service.CatalogReloaded += (_, args) => catalogReloaded.TrySetResult(args.CatalogItems);

        service.AddSelectedItem(Session, item, clearScanText: true, closeMatchesPopup: false, operation: "manual-add-selected");
        Assert.Single(cart.Lines);

        remoteLookup.SetResult(new RemoteLookupRefreshResult("S001", "930241", Found: false, Item: null, DeletedCount: 1));
        var catalogItems = await catalogReloaded.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Empty(catalogItems);
        var line = Assert.Single(cart.Lines);
        Assert.Equal("Retired Workflow Snack", line.DisplayName);
    }

    [Fact]
    public async Task Add_selected_item_remote_timeout_keeps_cart_unchanged()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var item = CreateItem("SKU-251", "Timeout Workflow Tea", "930251", PriceSourceKind.StoreRetailPrice, 5.5m);
        var logs = new ConcurrentQueue<string>();
        index.ReplaceAll([item]);
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: async (_, _, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return new RemoteLookupRefreshResult("S001", "930251", Found: false, Item: null, DeletedCount: 1);
            });

        using var logCapture = CaptureClientLog(logs);

        service.AddSelectedItem(Session, item, clearScanText: true, closeMatchesPopup: false, operation: "manual-add-selected");

        await WaitUntilAsync(() => HasLog(logs, "remote lookup timeout"));

        var line = Assert.Single(cart.Lines);
        Assert.Equal("Timeout Workflow Tea", line.DisplayName);
        Assert.Equal(5.5m, line.UnitPrice);
    }

    [Fact]
    public async Task Add_selected_item_logs_remote_lookup_skipped_when_session_is_offline()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var logs = new ConcurrentQueue<string>();
        var item = CreateItem("SKU-255", "Offline Workflow Tea", "930255", PriceSourceKind.StoreRetailPrice, 5.5m);
        index.ReplaceAll([item]);
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (_, _, _) => throw new InvalidOperationException("Remote lookup should not run while offline."));

        using var logCapture = CaptureClientLog(logs);

        service.AddSelectedItem(
            Session with { IsOnline = false },
            item,
            clearScanText: true,
            closeMatchesPopup: false,
            operation: "manual-add-selected");

        await WaitUntilAsync(() => HasLog(logs, "remote lookup skipped") && HasLog(logs, "reason=offline"));

        Assert.Single(cart.Lines);
    }

    [Fact]
    public async Task Add_selected_item_DeduplicatesRemoteLookupForSameLookupCode()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var item = CreateItem("SKU-261", "Workflow Soda", "930261", PriceSourceKind.StoreRetailPrice, 3.5m);
        var remoteItem = CreateItem("SKU-261", "Workflow Soda Remote", "930261", PriceSourceKind.StoreRetailPrice, 4.5m);
        var remoteLookup = new TaskCompletionSource<RemoteLookupRefreshResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookupCalls = 0;
        index.ReplaceAll([item]);
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (_, _, _) =>
            {
                Interlocked.Increment(ref lookupCalls);
                return remoteLookup.Task;
            },
            reloadCatalogAsync: _ => throw new InvalidOperationException("Full catalog reload should not run after lookup refresh."));

        service.AddSelectedItem(Session, item, clearScanText: true, closeMatchesPopup: false, operation: "manual-add-selected");
        service.AddSelectedItem(Session, item, clearScanText: true, closeMatchesPopup: false, operation: "manual-add-selected");

        await WaitUntilAsync(() => Volatile.Read(ref lookupCalls) == 1);

        remoteLookup.SetResult(new RemoteLookupRefreshResult("S001", "930261", Found: true, Item: remoteItem, DeletedCount: 0));
        await WaitUntilAsync(() => cart.Lines.Single().DisplayName == "Workflow Soda Remote");

        Assert.Equal(1, Volatile.Read(ref lookupCalls));
        Assert.Single(index.FindExactMatches("S001", "930261"));
    }

    [Fact]
    public void Add_selected_item_intermittent_same_item_still_merges_globally()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var apple = CreateItem("SKU-262", "Workflow Apple", "930262", PriceSourceKind.StoreRetailPrice, 3.5m);
        var banana = CreateItem("SKU-263", "Workflow Banana", "930263", PriceSourceKind.StoreRetailPrice, 4.5m);
        index.ReplaceAll([apple, banana]);
        var service = new PosTerminalWorkflowService(index, cart);

        var firstAppleResult = service.AddSelectedItem(Session, apple, clearScanText: true, closeMatchesPopup: false, operation: "manual-add-selected");
        var bananaResult = service.AddSelectedItem(Session, banana, clearScanText: true, closeMatchesPopup: false, operation: "manual-add-selected");
        var mergedAppleResult = service.AddSelectedItem(Session, apple, clearScanText: true, closeMatchesPopup: false, operation: "manual-add-selected");

        Assert.Equal(2, cart.Lines.Count);
        Assert.Same(firstAppleResult.SelectedCartLine, cart.Lines[0]);
        Assert.Same(bananaResult.SelectedCartLine, cart.Lines[1]);
        Assert.Same(cart.Lines[0], mergedAppleResult.SelectedCartLine);
        Assert.Equal(2m, cart.Lines[0].Quantity);
        Assert.Equal(1m, cart.Lines[1].Quantity);
        Assert.Equal("930262", cart.Lines[0].LookupCodeNormalized);
        Assert.Equal("930263", cart.Lines[1].LookupCodeNormalized);
    }

    [Fact]
    public async Task Process_scan_remote_lookup_updates_all_non_consecutive_matching_sale_lines()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var apple = CreateItem("SKU-264", "Workflow Apple Local", "930264", PriceSourceKind.StoreRetailPrice, 2m);
        var banana = CreateItem("SKU-265", "Workflow Banana", "930265", PriceSourceKind.StoreRetailPrice, 3m);
        var remoteApple = CreateItem("SKU-264", "Workflow Apple Remote", "930264", PriceSourceKind.StoreClearancePrice, 5m);
        var appleLookup = new TaskCompletionSource<RemoteLookupRefreshResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var appleLookupCalls = 0;
        index.ReplaceAll([apple, banana]);
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (storeCode, lookupCode, _) =>
            {
                if (string.Equals(lookupCode, "930264", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref appleLookupCalls);
                    return appleLookup.Task;
                }

                return Task.FromResult(new RemoteLookupRefreshResult(storeCode, lookupCode, Found: false, Item: null, DeletedCount: 1));
            });

        service.ProcessScan(Session, "930264", preferExactLookup: true, source: "raw");
        await WaitUntilAsync(() => Volatile.Read(ref appleLookupCalls) == 1);
        service.ProcessScan(Session, "930265", preferExactLookup: true, source: "raw");
        service.ProcessScan(Session, "930264", preferExactLookup: true, source: "raw");
        service.ProcessScan(Session, "930264", preferExactLookup: true, source: "raw");

        Assert.Equal(3, cart.Lines.Count);
        Assert.Equal(2m, cart.Lines[0].UnitPrice);
        Assert.Equal(2m, cart.Lines[2].UnitPrice);

        appleLookup.SetResult(new RemoteLookupRefreshResult("S001", "930264", Found: true, Item: remoteApple, DeletedCount: 0));
        await WaitUntilAsync(() => cart.Lines
            .Where(line => line.LookupCodeNormalized == "930264")
            .All(line => line.DisplayName == "Workflow Apple Remote" && line.UnitPrice == 5m));

        Assert.Equal(1, Volatile.Read(ref appleLookupCalls));
        Assert.Equal(18m, cart.TotalAmount);
    }

    [Fact]
    public async Task Add_selected_item_logs_remote_lookup_queue_and_dispatch()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var logs = new ConcurrentQueue<string>();
        var item = CreateItem("SKU-266", "Queued Workflow Tea", "930266", PriceSourceKind.StoreRetailPrice, 3.5m);
        var remoteLookup = new TaskCompletionSource<RemoteLookupRefreshResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        index.ReplaceAll([item]);
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (_, _, _) => remoteLookup.Task);

        using var logCapture = CaptureClientLog(logs);

        service.AddSelectedItem(Session, item, clearScanText: true, closeMatchesPopup: false, operation: "manual-add-selected");

        await WaitUntilAsync(() => HasLog(logs, "remote lookup queued") && HasLog(logs, "remote lookup dispatch"));
        remoteLookup.SetResult(new RemoteLookupRefreshResult("S001", "930266", Found: false, Item: null, DeletedCount: 1));

        Assert.Single(cart.Lines);
    }

    [Fact]
    public async Task Add_selected_item_StillRunsRemoteLookupWhileCatalogSyncIsActive()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var logs = new ConcurrentQueue<string>();
        var item = CreateItem("SKU-271", "Workflow Busy Item", "930271", PriceSourceKind.StoreRetailPrice, 2.5m);
        var lookupCalls = 0;
        index.ReplaceAll([item]);
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (_, _, _) =>
            {
                Interlocked.Increment(ref lookupCalls);
                return Task.FromResult(new RemoteLookupRefreshResult("S001", "930271", Found: false, Item: null, DeletedCount: 1));
            },
            isCatalogSyncActive: () => true);

        using var logCapture = CaptureClientLog(logs);

        service.AddSelectedItem(Session, item, clearScanText: true, closeMatchesPopup: false, operation: "manual-add-selected");

        await WaitUntilAsync(() => Volatile.Read(ref lookupCalls) == 1);

        Assert.Equal(1, Volatile.Read(ref lookupCalls));
        Assert.Single(cart.Lines);
        Assert.True(HasLog(logs, "remote lookup queued"));
        Assert.True(HasLog(logs, "remote lookup dispatch"));
        Assert.False(HasLog(logs, "remote lookup deferred"));
    }

    [Fact]
    public async Task Add_selected_item_finishes_mainline_before_remote_lookup_dispatch()
    {
        var cart = new PosCartService();
        var index = new LocalSellableItemIndex();
        var logs = new ConcurrentQueue<string>();
        var item = CreateItem("SKU-281", "Workflow Async Item", "930281", PriceSourceKind.StoreRetailPrice, 2.5m);
        var remoteLookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiPriority = new BlockingUiPriorityCoordinator();
        index.ReplaceAll([item]);
        var service = new PosTerminalWorkflowService(
            index,
            cart,
            remoteLookupRefreshAsync: (_, _, _) =>
            {
                remoteLookupStarted.TrySetResult();
                return Task.FromResult(new RemoteLookupRefreshResult("S001", "930281", Found: false, Item: null, DeletedCount: 1));
            },
            uiPriorityCoordinator: uiPriority);

        using var logCapture = CaptureClientLog(logs);

        var result = service.AddSelectedItem(Session, item, clearScanText: true, closeMatchesPopup: false, operation: "manual-add-selected");

        Assert.Equal("pos.status.added", result.StatusKey);
        Assert.Single(cart.Lines);
        Assert.False(remoteLookupStarted.Task.IsCompleted);
        Assert.False(HasLog(logs, "remote lookup dispatch"));
        Assert.True(HasLog(logs, "cart add completed"));

        uiPriority.Release();
        await remoteLookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => HasLog(logs, "remote lookup dispatch"));

        var orderedLogs = logs.ToArray();
        var cartAddIndex = Array.FindIndex(orderedLogs, line => line.Contains("cart add completed", StringComparison.OrdinalIgnoreCase));
        var dispatchIndex = Array.FindIndex(orderedLogs, line => line.Contains("remote lookup dispatch", StringComparison.OrdinalIgnoreCase));
        Assert.True(cartAddIndex >= 0);
        Assert.True(dispatchIndex > cartAddIndex);
    }

    private static PosSessionState Session => new("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

    private static SellableItemDto CreateItem(
        string productCode,
        string name,
        string barcode,
        PriceSourceKind priceSource,
        decimal price,
        string? referenceCode = null,
        string? itemNumber = null,
        string? productBarcode = null,
        string? productImage = null)
    {
        return new SellableItemDto(
            StoreCode: "S001",
            ProductCode: productCode,
            ReferenceCode: referenceCode,
            DisplayName: name,
            LookupCode: barcode,
            ItemNumber: itemNumber ?? productCode,
            Barcode: productBarcode ?? barcode,
            RetailPrice: price,
            PriceSource: priceSource,
            PriceSourceLabel: priceSource.ToString(),
            QuantityFactor: 1m,
            UpdatedAt: DateTimeOffset.UtcNow,
            ProductImage: productImage);
    }

    private static void SetUnsafeQuantity(CartLine line, decimal quantity)
    {
        var field = typeof(CartLine).GetField("_quantity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(line, quantity);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static IDisposable CaptureClientLog(ConcurrentQueue<string> lines)
    {
        void Capture(string line)
        {
            lines.Enqueue(line);
        }

        ConsoleLog.LineWritten += Capture;
        return new DisposableAction(() => ConsoleLog.LineWritten -= Capture);
    }

    private static bool HasLog(ConcurrentQueue<string> lines, string text)
    {
        return lines.Any(line => line.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class DisposableAction(Action dispose) : IDisposable
    {
        public void Dispose()
        {
            dispose();
        }
    }

    private sealed class BlockingUiPriorityCoordinator : IUiPriorityCoordinator
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsUiActive => !_released.Task.IsCompleted;

        public void NotifyUserInput()
        {
        }

        public IDisposable BeginUiOperation(string name)
        {
            return new DisposableAction(() => { });
        }

        public Task WaitForUiIdleAsync(CancellationToken cancellationToken = default)
        {
            return cancellationToken.CanBeCanceled
                ? _released.Task.WaitAsync(cancellationToken)
                : _released.Task;
        }

        public void Release()
        {
            _released.TrySetResult();
        }
    }
}
