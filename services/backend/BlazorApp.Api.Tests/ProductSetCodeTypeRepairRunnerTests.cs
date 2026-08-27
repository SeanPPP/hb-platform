using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductSetCodeTypeRepairRunnerTests
{
    [Fact]
    public void ApprovedBaseline_任意统计漂移都会列出字段差异()
    {
        var drifted = ProductSetCodeTypeRepairRunner.ApprovedBaseline with
        {
            MissingStoreProjectionCount = 35770,
            ActiveStoreCount = 32,
        };

        var differences = ProductSetCodeTypeRepairRunner.ApprovedBaseline.Diff(drifted);

        Assert.Equal(2, differences.Count);
        Assert.Contains(differences, x => x.StartsWith("MissingStoreProjectionCount:", StringComparison.Ordinal));
        Assert.Contains(differences, x => x.StartsWith("ActiveStoreCount:", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildFingerprint_相同快照稳定且受软删除行影响()
    {
        var target = BuildTarget();
        var first = ProductSetCodeTypeRepairRunner.BuildFingerprint(target);
        var second = ProductSetCodeTypeRepairRunner.BuildFingerprint(BuildTarget());

        Assert.Equal(first, second);

        target.StoreRetailPrices[0].IsDeleted = true;
        Assert.NotEqual(first, ProductSetCodeTypeRepairRunner.BuildFingerprint(target));
    }

    [Fact]
    public void BuildFingerprint_审计字段变化也会触发并发指纹变化()
    {
        var target = BuildTarget();
        var before = ProductSetCodeTypeRepairRunner.BuildFingerprint(target);

        target.Children[0].UpdatedAt = DateTime.UnixEpoch.AddSeconds(1);

        Assert.NotEqual(before, ProductSetCodeTypeRepairRunner.BuildFingerprint(target));
    }

    [Fact]
    public void BuildRollbackComparableFingerprint_忽略已核验的新增软删除墓碑()
    {
        var before = BuildTarget();
        var restored = BuildTarget();
        restored.StoreRetailPrices.Add(new StoreRetailPrice
        {
            UUID = "repair-created",
            ProductCode = "P-SET",
            StoreCode = "S02",
            IsActive = false,
            IsDeleted = true,
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch,
        });
        var applied = new ProductSetCodeTypeRepairAppliedProduct
        {
            ProductCode = "P-SET",
            InsertedStoreRetailPriceIds = new List<string> { "repair-created" },
        };

        Assert.Equal(
            ProductSetCodeTypeRepairRunner.BuildFingerprint(before),
            ProductSetCodeTypeRepairRunner.BuildRollbackComparableFingerprint(restored, applied)
        );
    }

    [Fact]
    public void BuildRollbackComparableFingerprint_新增行未软删除时拒绝通过()
    {
        var restored = BuildTarget();
        restored.StoreRetailPrices.Add(new StoreRetailPrice
        {
            UUID = "repair-created",
            ProductCode = "P-SET",
            StoreCode = "S02",
            IsActive = true,
            IsDeleted = false,
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch,
        });
        var applied = new ProductSetCodeTypeRepairAppliedProduct
        {
            ProductCode = "P-SET",
            InsertedStoreRetailPriceIds = new List<string> { "repair-created" },
        };

        Assert.Throws<InvalidOperationException>(() =>
            ProductSetCodeTypeRepairRunner.BuildRollbackComparableFingerprint(restored, applied)
        );
    }

    [Fact]
    public void ComputeSha256_输出小写且长度固定()
    {
        var hash = ProductSetCodeTypeRepairRunner.ComputeSha256("same-content");

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.Equal(hash, ProductSetCodeTypeRepairRunner.ComputeSha256("same-content"));
        Assert.NotEqual(hash, ProductSetCodeTypeRepairRunner.ComputeSha256("different-content"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("run/escape")]
    [InlineData("run\\escape")]
    [InlineData("run..escape")]
    [InlineData("含中文")]
    public void ValidateRunId_拒绝路径逃逸和非法字符(string runId)
    {
        Assert.Throws<ArgumentException>(() => ProductSetCodeTypeRepairRunner.ValidateRunId(runId));
    }

    [Fact]
    public void ValidateRunId_接受安全运行编号()
    {
        ProductSetCodeTypeRepairRunner.ValidateRunId("product-setcode-repair_20260826T073105Z.preflight");
    }

    [Fact]
    public void ValidateJournalCoverage_必须完整覆盖合格商品且无失败()
    {
        var snapshot = new ProductSetCodeTypeRepairSnapshot
        {
            RunId = "run-1",
            Eligible = new List<ProductSetCodeTypeRepairTarget>
            {
                BuildTarget(),
                new ProductSetCodeTypeRepairTarget { Product = new Product { ProductCode = "P-SECOND" } },
            },
        };
        var journal = new ProductSetCodeTypeRepairRunReport
        {
            RunId = "run-1",
            SnapshotSha256 = "sha",
            DryRun = false,
            Succeeded = new List<ProductSetCodeTypeRepairAppliedProduct>
            {
                new() { ProductCode = "P-SET" },
                new() { ProductCode = "P-SET" },
            },
            Failed = new List<ProductSetCodeTypeRepairFailure> { new() { ProductCode = "P-SECOND", Reason = "lock" } },
        };

        var violations = ProductSetCodeTypeRepairRunner.ValidateJournalCoverage(snapshot, journal, "different-sha");

        Assert.Contains(violations, x => x.Reason.Contains("SHA-256", StringComparison.Ordinal));
        Assert.Contains(violations, x => x.Reason.Contains("重复", StringComparison.Ordinal));
        Assert.Contains(violations, x => x.Reason.Contains("失败", StringComparison.Ordinal));
        Assert.Contains(violations, x => x.Reason.Contains("完整覆盖", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildInactiveBusinessFingerprint_忽略SetType和审计字段但保留成本差异()
    {
        var target = BuildTarget();
        target.Product.IsActive = false;
        var before = ProductSetCodeTypeRepairRunner.BuildInactiveBusinessFingerprint(target);

        target.Children[0].SetType = 1;
        target.Children[0].UpdatedAt = DateTime.UtcNow;
        Assert.Equal(before, ProductSetCodeTypeRepairRunner.BuildInactiveBusinessFingerprint(target));

        target.Children[0].SetPurchasePrice = 99m;
        Assert.NotEqual(before, ProductSetCodeTypeRepairRunner.BuildInactiveBusinessFingerprint(target));
    }

    private static ProductSetCodeTypeRepairTarget BuildTarget() => new()
    {
        Product = new Product
        {
            UUID = "product-id",
            ProductCode = "P-SET",
            ProductType = 1,
            PurchasePrice = 10m,
            IsActive = true,
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch,
        },
        WarehouseProduct = new WarehouseProduct
        {
            ProductCode = "P-SET",
            ImportPrice = 8m,
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch,
        },
        Children = new List<ProductSetCode>
        {
            new()
            {
                SetCodeId = "set-a", ProductCode = "P-SET", SetProductCode = "CHILD-A", SetItemNumber = "A",
                SetBarcode = "123", SetRetailPrice = 20m, SetType = 2, CreatedAt = DateTime.UnixEpoch, UpdatedAt = DateTime.UnixEpoch,
            },
        },
        StoreRetailPrices = new List<StoreRetailPrice>
        {
            new()
            {
                UUID = "price-soft-deleted", ProductCode = "P-SET", StoreCode = "S01", PurchasePrice = 3m,
                IsDeleted = false, CreatedAt = DateTime.UnixEpoch, UpdatedAt = DateTime.UnixEpoch,
            },
        },
        StoreProjections = new List<StoreMultiCodeProduct>(),
        ActiveStoreCodes = new List<string> { "S01" },
    };
}
