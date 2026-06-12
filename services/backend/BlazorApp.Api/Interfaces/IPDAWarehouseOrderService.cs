using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    public interface IPDAWarehouseOrderService
    {
        Task<PDAWarehouseOrderListResponseDto> GetOrderListAsync(
            PDAWarehouseOrderFilterDto filter,
            string storeCode
        );

        Task<PDAWarehouseOrderDto?> GetOrderDetailAsync(string orderGuid, string storeCode);

        Task<PDAWarehouseOrderResponseDto> CreateOrderAsync(
            CreatePDAWarehouseOrderRequestDto request,
            string deviceHardwareId
        );

        Task<PDAWarehouseOrderResponseDto> UpdateOrderAsync(
            UpdatePDAWarehouseOrderRequestDto request,
            string storeCode,
            string deviceHardwareId
        );

        Task<PDAWarehouseOrderResponseDto> SubmitOrderAsync(
            SubmitPDAWarehouseOrderRequestDto request,
            string storeCode,
            string deviceHardwareId
        );

        Task<PDAWarehouseOrderResponseDto> DeleteOrderAsync(
            string orderGuid,
            string storeCode,
            string deviceHardwareId
        );

        Task<PDAWarehouseOrderDetailResponseDto> AddOrderLineAsync(
            AddPDAWarehouseOrderLineRequestDto request,
            string storeCode
        );

        Task<PDAWarehouseOrderDetailResponseDto> UpdateOrderLineAsync(
            UpdatePDAWarehouseOrderLineRequestDto request,
            string storeCode
        );

        Task<PDAWarehouseOrderDetailResponseDto> DeleteOrderLineAsync(
            string detailGuid,
            string storeCode
        );

        Task<PDAWarehouseOrderDetailResponseDto> BatchAddOrderLinesAsync(
            BatchAddPDAWarehouseOrderLinesRequestDto request,
            string storeCode
        );

        Task<PDAWarehouseProductListResponseDto> GetProductsAsync(
            PDAWarehouseProductFilterDto filter
        );

        Task<PDAWarehouseProductDto?> GetProductByCodeAsync(string productCode);

        Task<PDAWarehouseProductDto?> ScanProductAsync(PDAScanProductRequestDto request);

        Task<Dictionary<string, PDAWarehouseProductDto>> BatchGetProductsByItemNumbersAsync(
            List<string> itemNumbers
        );
    }
}
