using BlazorApp.Api.Features.StoreOrders.PasteReplace.Domain;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.PasteReplace;

internal sealed class PasteReplaceOrderLinesValidator
{
    internal PasteReplaceValidationResult Validate(PasteReplaceOrderLinesDto request)
    {
        if (!PasteReplaceOrderLinesRules.IsSupportedTargetField(request.TargetField))
        {
            return PasteReplaceValidationResult.Invalid("Unsupported paste target field");
        }

        if (request.Items.Any(item => !PasteReplaceOrderLinesRules.IsSupportedAction(item.Action)))
        {
            return PasteReplaceValidationResult.Invalid("Unsupported paste action");
        }

        return PasteReplaceValidationResult.Valid();
    }
}
