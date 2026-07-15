using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IContainerAllocationSalesReportService
{
    Task<ContainerAllocationSalesReportResponse> QueryAsync(
        string containerGuid,
        ContainerAllocationSalesQueryRequest request
    );

    Task<ContainerAllocationSalesBranchesResponse> QueryBranchesAsync(
        string containerGuid,
        ContainerAllocationSalesBranchesQueryRequest request
    );
}
