using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface ICashRegisterUserReactService
    {
        Task<GridResponseDto<CashRegisterUserListDto>> GetGridDataAsync(GridRequestDto request);
        Task<ApiResponse<List<CashRegisterUserUserOptionDto>>> GetUserOptionsAsync();
        Task<ApiResponse<CashRegisterUserDetailDto>> GetByHGuidAsync(string hGuid);
        Task<ApiResponse<CashRegisterUserDetailDto>> CreateAsync(CreateCashRegisterUserDto dto, string createdBy);
        Task<ApiResponse<CashRegisterUserDetailDto>> UpdateAsync(string hGuid, UpdateCashRegisterUserDto dto, string updatedBy);
        Task<ApiResponse<bool>> DeleteAsync(string hGuid, string updatedBy);
        Task<ApiResponse<bool>> BatchDeleteAsync(List<string> hGuids, string updatedBy);
    }
}
