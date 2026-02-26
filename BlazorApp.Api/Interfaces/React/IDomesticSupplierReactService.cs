using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// React 专用国内供应商服务接口（与原有 IDomesticSupplierService 解耦）
    /// 仅包含 React 控制器所需的方法
    /// </summary>
    public interface IDomesticSupplierReactService
    {
        Task<List<DomesticSupplierDto>> GetActiveSupplierListAsync();
        Task<DomesticSupplierDto?> GetSupplierByCodeAsync(string supplierCode);
    }
}