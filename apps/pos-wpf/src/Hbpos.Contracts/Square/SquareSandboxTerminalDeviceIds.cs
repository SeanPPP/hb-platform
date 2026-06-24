namespace Hbpos.Contracts.Square;

public sealed record SquareSandboxTerminalDevice(
    string DeviceId,
    string Name,
    string Result,
    string Details);

public static class SquareSandboxTerminalDeviceIds
{
    public const string TestDeviceStatus = "SANDBOX_TEST";
    public const string CreditCardSuccess = "9fa747a2-25ff-48ee-b078-04381f7c828f";
    public const string BuyerCanceled = "841100b9-ee60-4537-9bcf-e30b2ba5e215";
    public const string SquareTimedOut = "0a956d49-619a-4530-8e5e-8eac603ffc5e";
    public const string TerminalNotPickedUp = "da40d603-c2ea-4a65-8cfd-f42e36dab0c7";

    private const string DevicesApiPrefix = "device:";

    public static IReadOnlyList<SquareSandboxTerminalDevice> CheckoutDevices { get; } =
    [
        new(CreditCardSuccess, "Sandbox: success credit card", "Success", "Credit card payment is completed."),
        new("22cd266c-6246-4c06-9983-67f0c26346b0", "Sandbox: success credit card with 20% tip", "Success", "Credit card payment is completed with a 20% tip."),
        new("4mp4e78c-88ed-4d55-a269-8008dfe14e9", "Sandbox: success gift card", "Success", "Square gift card payment is completed."),
        new("388b5a08-a77c-48ef-ad2a-4a790e6f2789", "Sandbox: success Interac credit card (CAD)", "Success", "Interac credit card payment is completed for CAD amounts."),
        new("2b0b734b-b187-47f0-9d6f-288745210bdb", "Sandbox: success Interac with 20% tip (CAD)", "Success", "Interac credit card payment is completed with a 20% tip for CAD amounts."),
        new("19a01fbd-3dcd-4d9f-a499-a641684af745", "Sandbox: success eMoney/FeLiCa", "Success", "eMoney or FeLiCa credit card payment is completed."),
        new("819f8d79-961e-4097-8f70-ef70b3e7db28", "Sandbox: success Afterpay", "Success", "Afterpay payment is completed."),
        new("cae0ee02-f83b-11ec-b939-0242ac120002", "Sandbox: success PayPay (Japan)", "Success", "PayPay QR code payment is completed for Japan sandbox locations."),
        new(BuyerCanceled, "Sandbox: cancel by buyer", "Failure", "Buyer cancels the checkout on the device."),
        new(SquareTimedOut, "Sandbox: timeout by Square", "Failure", "Square immediately times out the checkout."),
        new(TerminalNotPickedUp, "Sandbox: offline terminal", "Failure", "Checkout is not picked up by a terminal and can be canceled.")
    ];

    public static string ResolveCheckoutDeviceId(string? deviceId)
    {
        var normalized = NormalizeDeviceId(deviceId);
        var checkoutDevice = CheckoutDevices.FirstOrDefault(device =>
            string.Equals(device.DeviceId, normalized, StringComparison.OrdinalIgnoreCase));
        return checkoutDevice?.DeviceId ?? CreditCardSuccess;
    }

    public static bool IsCheckoutDeviceId(string? deviceId)
    {
        var normalized = NormalizeDeviceId(deviceId);
        return normalized is not null &&
            CheckoutDevices.Any(device => string.Equals(device.DeviceId, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static bool AreSameDeviceId(string? left, string? right)
    {
        return string.Equals(NormalizeDeviceId(left), NormalizeDeviceId(right), StringComparison.OrdinalIgnoreCase);
    }

    public static string? NormalizeDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        var trimmed = deviceId.Trim();
        // Sandbox 官方测试值不能带 Devices API 的 device: 前缀，否则 Terminal checkout 不会识别该模拟结果。
        return trimmed.StartsWith(DevicesApiPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[DevicesApiPrefix.Length..]
            : trimmed;
    }
}
