using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.ProductWarehouse;

internal static class WarehouseProductSingleCreationResultAssembler
{
    internal static CreateSingleProductResponseDto CreatePending() =>
        new() { Success = false, Message = "创建失败" };

    internal static CreateSingleProductResponseDto Reject(string message) =>
        new() { Success = false, Message = message };

    internal static void Reject(CreateSingleProductResponseDto response, string message) =>
        response.Message = message;

    internal static void Complete(
        CreateSingleProductResponseDto response,
        string productCode,
        string itemNumber,
        string? barcode,
        bool barcodeExists,
        List<string> warnings
    )
    {
        response.Success = true;
        response.Message = "商品创建成功";
        response.ProductCode = productCode;
        response.ItemNumber = itemNumber;
        response.Barcode = barcode;
        response.BarcodeExists = barcodeExists;
        response.Warnings = warnings;
    }

    internal static void RejectExecution(
        CreateSingleProductResponseDto response,
        string error
    ) => response.Message = "创建失败: " + error;
}
