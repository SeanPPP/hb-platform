using System.Text.Json;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class InstallmentOrderContractTests
{
    [Fact]
    public void QueryParams_日期使用DateOnly避免混入服务器时区()
    {
        Assert.Equal(
            typeof(DateOnly?),
            typeof(InstallmentOrderQueryParams).GetProperty(nameof(InstallmentOrderQueryParams.StartDate))!.PropertyType
        );
        Assert.Equal(
            typeof(DateOnly?),
            typeof(InstallmentOrderQueryParams).GetProperty(nameof(InstallmentOrderQueryParams.EndDate))!.PropertyType
        );
    }

    [Fact]
    public void List与Detail_使用独立DTO且列表不暴露生命周期自由文本()
    {
        var sharedAssembly = typeof(InstallmentOrderQueryParams).Assembly;
        var summaryType = sharedAssembly.GetType("BlazorApp.Shared.DTOs.InstallmentOrderSummaryDto");
        var detailType = sharedAssembly.GetType("BlazorApp.Shared.DTOs.InstallmentOrderDetailDto");

        Assert.NotNull(summaryType);
        Assert.NotNull(detailType);
        Assert.Null(summaryType!.GetProperty("Note"));
        Assert.Null(summaryType.GetProperty("PickupNote"));
        Assert.Null(summaryType.GetProperty("CancellationReason"));

        var listMethod = typeof(IInstallmentOrderReactService).GetMethod("GetOrderListAsync");
        Assert.NotNull(listMethod);
        Assert.Contains("InstallmentOrderSummaryDto", listMethod!.ReturnType.FullName);

        var responseOrder = typeof(InstallmentOrderDetailResponse).GetProperty("Order");
        Assert.Equal(detailType, responseOrder!.PropertyType);
        Assert.NotNull(typeof(InstallmentOrderDetailResponse).GetProperty("PickupInfo"));
        Assert.NotNull(typeof(InstallmentOrderDetailResponse).GetProperty("CancellationInfo"));
    }

    [Fact]
    public void 所有对外时间字段使用DateTimeOffset并序列化为UTC_Z后缀()
    {
        var sharedAssembly = typeof(InstallmentOrderQueryParams).Assembly;
        var summaryType = RequireType(sharedAssembly, "InstallmentOrderSummaryDto");
        var detailType = RequireType(sharedAssembly, "InstallmentOrderDetailDto");
        var pickupType = RequireType(sharedAssembly, "InstallmentPickupInfoDto");
        var cancellationType = RequireType(sharedAssembly, "InstallmentCancellationInfoDto");

        AssertPropertyType(summaryType, "CreatedAt", typeof(DateTimeOffset));
        AssertPropertyType(summaryType, "UpdatedAt", typeof(DateTimeOffset));
        AssertPropertyType(detailType, "CreatedAt", typeof(DateTimeOffset));
        AssertPropertyType(detailType, "UpdatedAt", typeof(DateTimeOffset));
        AssertPropertyType(pickupType, "PickedUpAt", typeof(DateTimeOffset?));
        AssertPropertyType(cancellationType, "CancelledAt", typeof(DateTimeOffset?));
        AssertPropertyType(typeof(InstallmentPaymentDto), "RecordedAt", typeof(DateTimeOffset));

        var summary = Activator.CreateInstance(summaryType)!;
        summaryType.GetProperty("CreatedAt")!.SetValue(
            summary,
            new DateTimeOffset(2026, 7, 4, 10, 5, 0, TimeSpan.Zero)
        );
        var json = JsonSerializer.Serialize(summary, summaryType);

        Assert.Contains("\"CreatedAt\":\"2026-07-04T10:05:00Z\"", json);
    }

    private static Type RequireType(System.Reflection.Assembly assembly, string typeName)
    {
        var type = assembly.GetType($"BlazorApp.Shared.DTOs.{typeName}");
        Assert.NotNull(type);
        return type!;
    }

    private static void AssertPropertyType(Type ownerType, string propertyName, Type expectedType)
    {
        var property = ownerType.GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
