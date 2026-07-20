using BlazorApp.Api.Services.React;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Models;
using Xunit;

namespace BlazorApp.Api.Tests;

public class PreorderRulesTests
{
    [Fact]
    public async Task 启动迁移包含Preorder八张独立表和业务唯一索引()
    {
        var repoRoot = FindRepoRoot();
        var source = await File.ReadAllTextAsync(
            Path.Combine(repoRoot, "services/backend/BlazorApp.Api/Data/StartupSchemaMigrator.cs")
        );

        foreach (var table in new[]
        {
            "PreorderTemplate",
            "PreorderTemplateItem",
            "PreorderTemplateStore",
            "PreorderActivation",
            "PreorderActivationItem",
            "PreorderActivationStore",
            "PreorderWarehouseOrder",
            "PreorderWarehouseOrderItem",
        })
        {
            Assert.Contains($"OBJECT_ID(N'[dbo].[{table}]', N'U') IS NULL", source);
        }

        Assert.Contains("UX_PreorderActivation_Template_Period", source);
        Assert.Contains("IX_PreorderActivationStore_StoreGuid", source);
        Assert.Contains("UX_PreorderWarehouseOrder_Activation_Store", source);
        Assert.Contains("UX_PreorderWarehouseOrderItem_Order_Item", source);
        Assert.Contains("IX_PreorderWarehouseOrderItem_OrderGuid", source);
        Assert.Contains("CK_PreorderActivation_Status", source);
        Assert.Contains("CK_PreorderWarehouseOrder_Status", source);
        Assert.Contains("ReturnedForRevision", source);
        Assert.Contains("definition NOT LIKE '%ReturnedForRevision%'", source);
        Assert.Contains("[EstimatedArrivalDate] date NULL", source);
        Assert.Contains(
            "COL_LENGTH(N'dbo.PreorderActivation', N'EstimatedArrivalDate') IS NULL",
            source
        );
        Assert.Contains(
            "ALTER TABLE [dbo].[PreorderActivation] ADD [EstimatedArrivalDate] date NULL",
            source
        );
        Assert.Contains("await EnsurePreorderSchemaAsync(db, logger);", source);
    }

    [Fact]
    public async Task Preorder三种ProviderBootstrap均包含订单明细OrderGuid非过滤索引()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/Data/PreorderSchemaBootstrap.cs"
            )
        );

        Assert.Equal(
            3,
            source.Split('\n').Count(line =>
                line.Contains(
                    "IX_PreorderWarehouseOrderItem_OrderGuid",
                    StringComparison.Ordinal
                )
            )
        );
        Assert.DoesNotContain(
            "IX_PreorderWarehouseOrderItem_OrderGuid\" ON \"PreorderWarehouseOrderItem\"(\"OrderGuid\") WHERE",
            source,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Preorder服务不写普通仓库订单表()
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(
                FindRepoRoot(),
                "services/backend/BlazorApp.Api/Services/React/PreorderReactService.cs"
            )
        );

        Assert.DoesNotContain("Queryable<WareHouseOrder>", source);
        Assert.DoesNotContain("Insertable<WareHouseOrder>", source);
        Assert.DoesNotContain("Updateable<WareHouseOrder>", source);
        Assert.DoesNotContain("WareHouseOrderDetails", source);
    }

    [Fact]
    public void 时间区间_首尾相接时不视为重叠()
    {
        var firstStart = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var boundary = firstStart.AddDays(7);

        Assert.False(PreorderRules.IntervalsOverlap(firstStart, boundary, boundary, boundary.AddDays(7)));
        Assert.True(PreorderRules.IntervalsOverlap(firstStart, boundary, boundary.AddTicks(-1), boundary.AddDays(7)));
    }

    [Theory]
    [InlineData(PreorderActivationStatuses.Cancelled, false)]
    [InlineData(PreorderActivationStatuses.Closed, false)]
    [InlineData(PreorderActivationStatuses.Scheduled, false)]
    [InlineData(PreorderActivationStatuses.Active, true)]
    public void 只有有效期内的Active批次阻塞普通订货(string status, bool expected)
    {
        var now = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            expected,
            PreorderRules.IsActivationActive(status, now.AddHours(-1), now.AddHours(1), now)
        );
    }

    [Theory]
    [InlineData(PreorderWarehouseOrderStatuses.Draft, false)]
    [InlineData(PreorderWarehouseOrderStatuses.Submitted, true)]
    [InlineData(PreorderWarehouseOrderStatuses.NoDemand, true)]
    [InlineData(PreorderWarehouseOrderStatuses.Processing, true)]
    [InlineData(PreorderWarehouseOrderStatuses.Completed, true)]
    [InlineData(PreorderWarehouseOrderStatuses.Cancelled, true)]
    [InlineData(PreorderWarehouseOrderStatuses.ReturnedForRevision, false)]
    [InlineData("Unexpected", false)]
    [InlineData("", false)]
    public void 只有明确的完成状态才视为已响应(string status, bool expected)
    {
        Assert.Equal(expected, PreorderRules.IsResponseCompleted(status));
    }

    [Theory]
    [InlineData(PreorderWarehouseOrderStatuses.Draft, true)]
    [InlineData(PreorderWarehouseOrderStatuses.ReturnedForRevision, true)]
    [InlineData(PreorderWarehouseOrderStatuses.Submitted, false)]
    public void 只有草稿和退回修改允许分店继续编辑(string status, bool expected)
    {
        Assert.Equal(expected, PreorderRules.IsStoreEditableOrderStatus(status));
    }

    [Fact]
    public void 份数乘MOQ溢出返回稳定业务错误()
    {
        Assert.Equal(36, PreorderRules.CalculateOrderedQuantity(3, 12));
        var error = Assert.Throws<PreorderBusinessException>(() =>
            PreorderRules.CalculateOrderedQuantity(int.MaxValue, 2)
        );
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("PREORDER_INVALID_REQUEST", error.ErrorCode);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("找不到仓库根目录");
    }
}
