using BlazorApp.Api.Features.StoreOrders.Orders.Domain;
using BlazorApp.Api.Features.StoreOrders.Orders.Infrastructure;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Orders.Application;

internal sealed record UpdateStoreContactCommand(UpdateStoreOrderStoreContactDto? Request);

internal sealed class UpdateStoreContactValidator
{
    internal UpdateStoreContactInput Validate(UpdateStoreContactCommand command)
    {
        ArgumentNullException.ThrowIfNull(command.Request);
        return new UpdateStoreContactInput(
            command.Request.OrderGUID.Trim(),
            command.Request.StoreCode.Trim(),
            command.Request.Address,
            command.Request.ContactEmail
        );
    }
}

internal sealed class UpdateStoreContactHandler(
    UpdateStoreContactValidator validator,
    StoreOrderCommandStore commandStore
)
{
    internal async Task<ApiResponse<StoreOrderStoreContactDto>> HandleAsync(
        UpdateStoreContactCommand command
    )
    {
        var result = await commandStore.UpdateStoreContactAsync(validator.Validate(command));
        return result.Success
            ? ApiResponse<StoreOrderStoreContactDto>.OK(
                result.Data!,
                "更新分店联系信息成功"
            )
            : ApiResponse<StoreOrderStoreContactDto>.Error(
                result.ErrorMessage!,
                result.ErrorCode
            );
    }
}
