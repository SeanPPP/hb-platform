using System.Diagnostics;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Domain;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Http;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Application;

internal sealed record ScanLookupProductsQuery(StoreOrderScanLookupRequestDto? Request);

internal sealed class ScanLookupProductsValidator
{
    internal ProductPickerValidationResult<ProductPickerScanLookupInput> Validate(
        ScanLookupProductsQuery query
    )
    {
        var barcode = query.Request?.Barcode?.Trim();
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return ProductPickerValidationResult<ProductPickerScanLookupInput>.Invalid(
                "Barcode is required."
            );
        }

        return ProductPickerValidationResult<ProductPickerScanLookupInput>.Valid(
            new ProductPickerScanLookupInput(barcode, query.Request?.StoreCode)
        );
    }
}

internal sealed class ScanLookupProductsHandler(
    ScanLookupProductsValidator validator,
    ProductPickerScanQueryStore queryStore,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ScanLookupProductsHandler> logger
)
{
    internal async Task<ApiResponse<StoreOrderScanLookupResultDto>> HandleAsync(
        ScanLookupProductsQuery query
    )
    {
        var totalStopwatch = Stopwatch.StartNew();
        var traceId = GetScanTraceId();
        var request = query.Request;

        try
        {
            var validation = validator.Validate(query);
            if (!validation.IsValid)
            {
                logger.LogInformation(
                    "[shop-scan-perf] traceId={TraceId} stage=scan.lookup.service.invalid storeCode={StoreCode} totalMs={TotalMs}",
                    traceId,
                    request?.StoreCode,
                    totalStopwatch.ElapsedMilliseconds
                );
                return new ApiResponse<StoreOrderScanLookupResultDto>
                {
                    Success = false,
                    Message = validation.ErrorMessage!,
                };
            }

            var input = validation.Value!;
            var lookupResult = await queryStore.LookupAsync(input);
            const long fallbackQueryMs = 0;

            logger.LogInformation(
                "[shop-scan-perf] traceId={TraceId} stage=scan.lookup.service.done storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} matchType={MatchType} rawCount={RawCount} itemCount={ItemCount} exactQueryMs={ExactQueryMs} fallbackQueryMs={FallbackQueryMs} buildMs={BuildMs} totalMs={TotalMs}",
                traceId,
                input.StoreCode,
                ProductPickerRules.GetBarcodeTail(input.Barcode),
                ProductPickerRules.GetBarcodeLength(input.Barcode),
                lookupResult.MatchType ?? "none",
                lookupResult.RawCount,
                lookupResult.Items.Count,
                lookupResult.ExactQueryMs,
                fallbackQueryMs,
                lookupResult.BuildMs,
                totalStopwatch.ElapsedMilliseconds
            );

            return new ApiResponse<StoreOrderScanLookupResultDto>
            {
                Success = true,
                Data = new StoreOrderScanLookupResultDto
                {
                    Barcode = input.Barcode,
                    MatchType = lookupResult.MatchType,
                    Items = lookupResult.Items,
                },
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[shop-scan-perf] traceId={TraceId} stage=scan.lookup.service.error storeCode={StoreCode} barcodeTail={BarcodeTail} barcodeLength={BarcodeLength} totalMs={TotalMs}",
                traceId,
                request?.StoreCode,
                ProductPickerRules.GetBarcodeTail(request?.Barcode),
                ProductPickerRules.GetBarcodeLength(request?.Barcode),
                totalStopwatch.ElapsedMilliseconds
            );
            logger.LogError(ex, "ScanLookupProductsAsync failed");
            return new ApiResponse<StoreOrderScanLookupResultDto>
            {
                Success = false,
                Message = ex.Message,
            };
        }
    }

    private string GetScanTraceId()
    {
        return httpContextAccessor
                .HttpContext?.Request.Headers["X-Scan-Trace-Id"]
                .FirstOrDefault()
            ?? httpContextAccessor.HttpContext?.TraceIdentifier
            ?? "no-trace";
    }
}
