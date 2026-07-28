using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Data.Sqlite;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class PromotionReactServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public PromotionReactServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
        _sqliteConnection.Open();

        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _sqliteConnection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

        _db.CodeFirst.InitTables(typeof(Promotion), typeof(PromotionProduct), typeof(PromotionStore));
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresInvalidApplyQuantityWithoutThrowing()
    {
        await SeedPromotionAsync("promo-invalid-quantity", applyQuantity: 0, fixedPrice: 10m);
        var service = CreateService();

        var result = await service.EvaluateAsync(
            new PromotionEvaluateRequest
            {
                StoreCode = "S01",
                Items = new List<CartItemInputDto>
                {
                    new() { ProductCode = "P01", Qty = 2, UnitPrice = 5m },
                },
            }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!.AppliedPromotions);
        Assert.Empty(result.Data.AdjustedItems);
        Assert.Equal(0m, result.Data.TotalDiscount);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotApplyPromotionWhenFixedPriceIsHigherThanCartGroup()
    {
        await SeedPromotionAsync("promo-expensive-fixed-price", applyQuantity: 2, fixedPrice: 20m);
        var service = CreateService();

        var result = await service.EvaluateAsync(
            new PromotionEvaluateRequest
            {
                StoreCode = "S01",
                Items = new List<CartItemInputDto>
                {
                    new() { ProductCode = "P01", Qty = 2, UnitPrice = 5m },
                },
            }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!.AppliedPromotions);
        Assert.Empty(result.Data.AdjustedItems);
        Assert.Equal(0m, result.Data.TotalDiscount);
    }

    [Fact]
    public void BuildValidWindowPredicate_IncludesEffectiveStartAndEndBoundaries()
    {
        var now = new DateTime(2026, 7, 28, 3, 0, 0, DateTimeKind.Utc);
        var isValid = PromotionReactService.BuildValidWindowPredicate(now).Compile();

        Assert.True(
            isValid(
                new Promotion
                {
                    IsEnabled = true,
                    EffectiveStart = now,
                    EffectiveEnd = now.AddMinutes(1),
                }
            )
        );
        Assert.True(
            isValid(
                new Promotion
                {
                    IsEnabled = true,
                    EffectiveStart = now.AddMinutes(-1),
                    EffectiveEnd = now,
                }
            )
        );
    }

    [Fact]
    public async Task GetValidByProductAndStoreAsync_ReturnsCurrentStoreAndHeadquartersPromotionsInStableOrder()
    {
        var now = new DateTime(2026, 7, 28, 3, 0, 0, DateTimeKind.Utc);

        Promotion BuildPromotion(
            string id,
            int priority = 0,
            DateTime? effectiveStart = null,
            DateTime? effectiveEnd = null,
            bool isEnabled = true,
            bool isDeleted = false
        )
        {
            return new Promotion
            {
                Id = id,
                Name = id,
                EffectiveStart = effectiveStart ?? now.AddHours(-1),
                EffectiveEnd = effectiveEnd ?? now.AddHours(1),
                IsEnabled = isEnabled,
                IsExclusive = false,
                Priority = priority,
                ApplyQuantity = 2,
                FixedPrice = 5m,
                CreatedAt = now,
                IsDeleted = isDeleted,
            };
        }

        await _db
            .Insertable(
                new[]
                {
                    BuildPromotion("store-only", priority: 12, effectiveStart: now.AddMinutes(-1)),
                    BuildPromotion("multi-store", priority: 10, effectiveStart: now.AddHours(-2)),
                    BuildPromotion("headquarters-early", priority: 5, effectiveStart: now.AddHours(-3)),
                    BuildPromotion("headquarters-a", priority: 5, effectiveStart: now.AddHours(-2)),
                    BuildPromotion("headquarters-b", priority: 5, effectiveStart: now.AddHours(-2)),
                    BuildPromotion("other-store", priority: 100),
                    BuildPromotion("wrong-product"),
                    BuildPromotion("disabled", isEnabled: false),
                    BuildPromotion("not-started", effectiveStart: now.AddMinutes(1)),
                    BuildPromotion("expired", effectiveEnd: now.AddMinutes(-1)),
                    BuildPromotion("deleted-promotion", isDeleted: true),
                    BuildPromotion("deleted-product-link"),
                    BuildPromotion("deleted-current-store-link"),
                }
            )
            .ExecuteCommandAsync();

        await _db
            .Insertable(
                new[]
                {
                    new PromotionProduct
                    {
                        Id = "store-only-product",
                        PromotionId = "store-only",
                        ProductCode = "P01",
                        CreatedAt = now,
                    },
                    new PromotionProduct
                    {
                        Id = "store-only-deleted-product",
                        PromotionId = "store-only",
                        ProductCode = "P02",
                        CreatedAt = now,
                        IsDeleted = true,
                    },
                    new PromotionProduct
                    {
                        Id = "multi-store-product",
                        PromotionId = "multi-store",
                        ProductCode = "P01",
                        CreatedAt = now,
                    },
                    new PromotionProduct
                    {
                        Id = "headquarters-early-product",
                        PromotionId = "headquarters-early",
                        ProductCode = "P01",
                        CreatedAt = now,
                    },
                    new PromotionProduct
                    {
                        Id = "headquarters-a-product",
                        PromotionId = "headquarters-a",
                        ProductCode = "P01",
                        CreatedAt = now,
                    },
                    new PromotionProduct
                    {
                        Id = "headquarters-b-product",
                        PromotionId = "headquarters-b",
                        ProductCode = "P01",
                        CreatedAt = now,
                    },
                    new PromotionProduct
                    {
                        Id = "other-store-product",
                        PromotionId = "other-store",
                        ProductCode = "P01",
                        CreatedAt = now,
                    },
                    new PromotionProduct
                    {
                        Id = "wrong-product-link",
                        PromotionId = "wrong-product",
                        ProductCode = "P010",
                        CreatedAt = now,
                    },
                    new PromotionProduct
                    {
                        Id = "disabled-product",
                        PromotionId = "disabled",
                        ProductCode = "P01",
                        CreatedAt = now,
                    },
                    new PromotionProduct
                    {
                        Id = "not-started-product",
                        PromotionId = "not-started",
                        ProductCode = "P01",
                        CreatedAt = now,
                    },
                    new PromotionProduct
                    {
                        Id = "expired-product",
                        PromotionId = "expired",
                        ProductCode = "P01",
                        CreatedAt = now,
                    },
                    new PromotionProduct
                    {
                        Id = "deleted-promotion-product",
                        PromotionId = "deleted-promotion",
                        ProductCode = "P01",
                        CreatedAt = now,
                    },
                    new PromotionProduct
                    {
                        Id = "deleted-product-link-product",
                        PromotionId = "deleted-product-link",
                        ProductCode = "P01",
                        CreatedAt = now,
                        IsDeleted = true,
                    },
                    new PromotionProduct
                    {
                        Id = "deleted-current-store-link-product",
                        PromotionId = "deleted-current-store-link",
                        ProductCode = "P01",
                        CreatedAt = now,
                    },
                }
            )
            .ExecuteCommandAsync();

        await _db
            .Insertable(
                new[]
                {
                    new PromotionStore
                    {
                        Id = "store-only-store",
                        PromotionId = "store-only",
                        StoreCode = "S01",
                        CreatedAt = now,
                    },
                    new PromotionStore
                    {
                        Id = "store-only-deleted-store",
                        PromotionId = "store-only",
                        StoreCode = "S99",
                        CreatedAt = now,
                        IsDeleted = true,
                    },
                    new PromotionStore
                    {
                        Id = "multi-store-s01",
                        PromotionId = "multi-store",
                        StoreCode = "S01",
                        CreatedAt = now,
                    },
                    new PromotionStore
                    {
                        Id = "multi-store-s02",
                        PromotionId = "multi-store",
                        StoreCode = "S02",
                        CreatedAt = now,
                    },
                    new PromotionStore
                    {
                        Id = "headquarters-a-deleted-store",
                        PromotionId = "headquarters-a",
                        StoreCode = "S99",
                        CreatedAt = now,
                        IsDeleted = true,
                    },
                    new PromotionStore
                    {
                        Id = "other-store-s02",
                        PromotionId = "other-store",
                        StoreCode = "S02",
                        CreatedAt = now,
                    },
                    new PromotionStore
                    {
                        Id = "deleted-current-store-link-s01",
                        PromotionId = "deleted-current-store-link",
                        StoreCode = "S01",
                        CreatedAt = now,
                        IsDeleted = true,
                    },
                    new PromotionStore
                    {
                        Id = "deleted-current-store-link-s02",
                        PromotionId = "deleted-current-store-link",
                        StoreCode = "S02",
                        CreatedAt = now,
                    },
                }
            )
            .ExecuteCommandAsync();

        var service = CreateService();

        var result = await service.GetValidByProductAndStoreAsync("P01", "S01", now);

        Assert.True(result.Success);
        var items = Assert.IsType<List<PromotionListDto>>(result.Data);
        Assert.Equal(
            new[] { "store-only", "multi-store", "headquarters-early", "headquarters-a", "headquarters-b" },
            items.Select(item => item.Id).ToArray()
        );
        Assert.Equal(1, items.Single(item => item.Id == "store-only").ProductsCount);
        Assert.Equal(1, items.Single(item => item.Id == "store-only").StoresCount);
        Assert.Equal(2, items.Single(item => item.Id == "multi-store").StoresCount);
        Assert.Equal(0, items.Single(item => item.Id == "headquarters-a").StoresCount);
    }

    [Fact]
    public async Task GetStoreGridAsync_ReturnsCurrentStorePromotionsWithScopeTags()
    {
        var now = DateTime.UtcNow;
        await SeedPromotionWithStoresAsync("store-only", "本店促销", new[] { "S01" }, now);
        await SeedPromotionWithStoresAsync("multi-store", "多店促销", new[] { "S01", "S02" }, now);
        await SeedPromotionWithStoresAsync("headquarters", "总部促销", Array.Empty<string>(), now);
        await SeedPromotionWithStoresAsync("other-store", "其他门店促销", new[] { "S02" }, now);
        await SeedPromotionWithStoresAsync("other-multi", "其他多店促销", new[] { "S02", "S03" }, now);
        var service = CreateService(new[] { "S01" });

        var result = await service.GetStoreGridAsync(
            new StorePromotionGridRequestDto
            {
                StoreCode = "S01",
                StartRow = 0,
                PageSize = 20,
            }
        );

        Assert.True(result.Success);
        Assert.Equal(3, result.Total);
        Assert.Equal(
            new[] { "headquarters", "multi-store", "store-only" },
            result.Items!.Select(item => item.Id).OrderBy(item => item).ToArray()
        );

        var storeOnly = result.Items!.Single(item => item.Id == "store-only");
        Assert.Equal(PromotionStoreScopeTypes.StoreOnly, storeOnly.ScopeType);
        Assert.True(storeOnly.CanEditInStoreScope);
        Assert.False(storeOnly.CanCopyToStore);

        var multiStore = result.Items!.Single(item => item.Id == "multi-store");
        Assert.Equal(PromotionStoreScopeTypes.MultiStore, multiStore.ScopeType);
        Assert.False(multiStore.CanEditInStoreScope);
        Assert.True(multiStore.CanCopyToStore);

        var headquarters = result.Items!.Single(item => item.Id == "headquarters");
        Assert.Equal(PromotionStoreScopeTypes.Headquarters, headquarters.ScopeType);
        Assert.False(headquarters.CanEditInStoreScope);
        Assert.True(headquarters.CanCopyToStore);
    }

    [Fact]
    public async Task CreateStorePromotionAsync_ForcesCurrentStoreAndDefaultPriority()
    {
        var service = CreateService(new[] { "S01" });

        var result = await service.CreateStorePromotionAsync(
            "S01",
            new CreatePromotionDto
            {
                Name = "本店新促销",
                EffectiveStart = DateTime.UtcNow.AddDays(-1),
                EffectiveEnd = DateTime.UtcNow.AddDays(1),
                IsEnabled = true,
                IsExclusive = true,
                Priority = 99,
                ApplyQuantity = 2,
                FixedPrice = 5m,
                Stores = new List<PromotionStoreItemDto>
                {
                    new() { StoreCode = "S01" },
                    new() { StoreCode = "S02" },
                },
            }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data!.Priority);
        Assert.Equal(PromotionStoreScopeTypes.StoreOnly, result.Data.ScopeType);
        Assert.True(result.Data.CanEditInStoreScope);
        var stores = await _db.Queryable<PromotionStore>().Where(item => item.PromotionId == result.Data.Id).ToListAsync();
        var store = Assert.Single(stores);
        Assert.Equal("S01", store.StoreCode);
    }

    [Fact]
    public async Task UpdateStorePromotionAsync_RejectsMultiStorePromotion()
    {
        var now = DateTime.UtcNow;
        await SeedPromotionWithStoresAsync("multi-store", "多店促销", new[] { "S01", "S02" }, now);
        var service = CreateService(new[] { "S01" });

        var result = await service.UpdateStorePromotionAsync(
            "multi-store",
            "S01",
            new UpdatePromotionDto
            {
                Name = "不应更新",
                EffectiveStart = now.AddDays(-1),
                EffectiveEnd = now.AddDays(1),
                ApplyQuantity = 2,
                FixedPrice = 5m,
            }
        );

        Assert.False(result.Success);
        Assert.Equal("PROMOTION_NOT_STORE_ONLY", result.ErrorCode);
    }

    [Fact]
    public async Task CopyToStoreAsync_CopiesTemplateAsStoreOnlyWithDefaultPriority()
    {
        var now = DateTime.UtcNow;
        await SeedPromotionWithStoresAsync("headquarters", "总部促销", Array.Empty<string>(), now, priority: 8);
        await SeedPromotionProductAsync("headquarters", "P99", 3);
        var service = CreateService(new[] { "S01" });

        var result = await service.CopyToStoreAsync(
            new CopyStorePromotionRequestDto
            {
                SourcePromotionId = "headquarters",
                StoreCode = "S01",
            }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.NotEqual("headquarters", result.Data!.Id);
        Assert.Equal("总部促销 - 本店副本", result.Data.Name);
        Assert.Equal(0, result.Data.Priority);
        Assert.False(result.Data.IsEnabled);
        Assert.Single(result.Data.Stores);
        Assert.Equal("S01", result.Data.Stores[0].StoreCode);
        Assert.Single(result.Data.Products);
        Assert.Equal("P99", result.Data.Products[0].ProductCode);
        Assert.Equal(3, result.Data.Products[0].UnitWeight);
        Assert.Equal(PromotionStoreScopeTypes.StoreOnly, result.Data.ScopeType);
        Assert.True(result.Data.CanEditInStoreScope);
    }

    [Fact]
    public async Task CreateStorePromotionAsync_RejectsEnabledExclusiveConflict()
    {
        var now = DateTime.UtcNow;
        await SeedPromotionWithStoresAsync("existing", "已有排他促销", new[] { "S01" }, now, priority: 3);
        var service = CreateService(new[] { "S01" });

        var result = await service.CreateStorePromotionAsync(
            "S01",
            new CreatePromotionDto
            {
                Name = "冲突促销",
                EffectiveStart = now.AddHours(-1),
                EffectiveEnd = now.AddHours(1),
                IsEnabled = true,
                IsExclusive = true,
                Priority = 0,
                ApplyQuantity = 2,
                FixedPrice = 5m,
            }
        );

        Assert.False(result.Success);
        Assert.Equal("exclusive_conflict", result.ErrorCode);
    }

    [Fact]
    public async Task StorePromotionMethods_RejectUnauthorizedStore()
    {
        var service = CreateService(new[] { "S01" });

        var result = await service.GetStoreGridAsync(
            new StorePromotionGridRequestDto
            {
                StoreCode = "S02",
                StartRow = 0,
                PageSize = 20,
            }
        );

        Assert.False(result.Success);
        Assert.Equal("STORE_FORBIDDEN", result.Message);
    }

    [Fact]
    public async Task StorePromotionWriteMethods_RejectUnauthorizedStore()
    {
        var now = DateTime.UtcNow;
        await SeedPromotionWithStoresAsync("store-only", "本店促销", new[] { "S02" }, now);
        await SeedPromotionWithStoresAsync("headquarters", "总部促销", Array.Empty<string>(), now);
        var service = CreateService(new[] { "S01" });

        var create = await service.CreateStorePromotionAsync("S02", BuildCreateDto("新促销", now));
        var update = await service.UpdateStorePromotionAsync("store-only", "S02", BuildUpdateDto("更新", now));
        var copy = await service.CopyToStoreAsync(
            new CopyStorePromotionRequestDto
            {
                SourcePromotionId = "headquarters",
                StoreCode = "S02",
            }
        );
        var enable = await service.EnableStorePromotionAsync("store-only", "S02", true);

        Assert.Equal("STORE_FORBIDDEN", create.ErrorCode);
        Assert.Equal("STORE_FORBIDDEN", update.ErrorCode);
        Assert.Equal("STORE_FORBIDDEN", copy.ErrorCode);
        Assert.Equal("STORE_FORBIDDEN", enable.ErrorCode);
    }

    [Fact]
    public async Task CopyToStoreAsync_RejectsStoreOnlyPromotion()
    {
        var now = DateTime.UtcNow;
        await SeedPromotionWithStoresAsync("store-only", "本店促销", new[] { "S01" }, now);
        var service = CreateService(new[] { "S01" });

        var result = await service.CopyToStoreAsync(
            new CopyStorePromotionRequestDto
            {
                SourcePromotionId = "store-only",
                StoreCode = "S01",
            }
        );

        Assert.False(result.Success);
        Assert.Equal("PROMOTION_NOT_COPYABLE", result.ErrorCode);
    }

    [Fact]
    public async Task EnableStorePromotionAsync_RejectsMultiStorePromotion()
    {
        var now = DateTime.UtcNow;
        await SeedPromotionWithStoresAsync("multi-store", "多店促销", new[] { "S01", "S02" }, now);
        var service = CreateService(new[] { "S01" });

        var result = await service.EnableStorePromotionAsync("multi-store", "S01", false);

        Assert.False(result.Success);
        Assert.Equal("PROMOTION_NOT_STORE_ONLY", result.ErrorCode);
    }

    [Fact]
    public async Task StorePromotionMethods_FailClosedWithoutScopeService()
    {
        var service = new PromotionReactService(CreateSqlSugarContext(_db));

        var result = await service.GetStoreGridAsync(
            new StorePromotionGridRequestDto
            {
                StoreCode = "S01",
                StartRow = 0,
                PageSize = 20,
            }
        );

        Assert.False(result.Success);
        Assert.Equal("STORE_FORBIDDEN", result.Message);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();

        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private PromotionReactService CreateService(IReadOnlyList<string>? accessibleStoreCodes = null)
    {
        return new PromotionReactService(
            CreateSqlSugarContext(_db),
            new FakeStoreScopeService(accessibleStoreCodes ?? new[] { "S01" })
        );
    }

    private async Task SeedPromotionAsync(string id, int applyQuantity, decimal fixedPrice)
    {
        var now = DateTime.UtcNow;

        await _db.Insertable(
            new Promotion
            {
                Id = id,
                Name = id,
                EffectiveStart = now.AddDays(-1),
                EffectiveEnd = now.AddDays(1),
                IsEnabled = true,
                IsExclusive = false,
                Priority = 0,
                ApplyQuantity = applyQuantity,
                FixedPrice = fixedPrice,
                CreatedAt = now,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new PromotionProduct
            {
                Id = $"{id}-product",
                PromotionId = id,
                ProductCode = "P01",
                UnitWeight = 1,
                CreatedAt = now,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _db.Insertable(
            new PromotionStore
            {
                Id = $"{id}-store",
                PromotionId = id,
                StoreCode = "S01",
                CreatedAt = now,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedPromotionWithStoresAsync(
        string id,
        string name,
        IReadOnlyCollection<string> storeCodes,
        DateTime now,
        int priority = 0
    )
    {
        await _db.Insertable(
            new Promotion
            {
                Id = id,
                Name = name,
                EffectiveStart = now.AddDays(-1),
                EffectiveEnd = now.AddDays(1),
                IsEnabled = true,
                IsExclusive = true,
                Priority = priority,
                ApplyQuantity = 2,
                FixedPrice = 5m,
                CreatedAt = now,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        foreach (var storeCode in storeCodes)
        {
            await _db.Insertable(
                new PromotionStore
                {
                    Id = $"{id}-{storeCode}",
                    PromotionId = id,
                    StoreCode = storeCode,
                    CreatedAt = now,
                    IsDeleted = false,
                }
            ).ExecuteCommandAsync();
        }
    }

    private async Task SeedPromotionProductAsync(string promotionId, string productCode, int unitWeight)
    {
        await _db.Insertable(
            new PromotionProduct
            {
                Id = $"{promotionId}-{productCode}",
                PromotionId = promotionId,
                ProductCode = productCode,
                UnitWeight = unitWeight,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private static CreatePromotionDto BuildCreateDto(string name, DateTime now)
    {
        return new CreatePromotionDto
        {
            Name = name,
            EffectiveStart = now.AddHours(-1),
            EffectiveEnd = now.AddHours(1),
            IsEnabled = false,
            IsExclusive = true,
            ApplyQuantity = 2,
            FixedPrice = 5m,
        };
    }

    private static UpdatePromotionDto BuildUpdateDto(string name, DateTime now)
    {
        return new UpdatePromotionDto
        {
            Name = name,
            EffectiveStart = now.AddHours(-1),
            EffectiveEnd = now.AddHours(1),
            IsEnabled = false,
            IsExclusive = true,
            ApplyQuantity = 2,
            FixedPrice = 5m,
        };
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }

    private sealed class FakeStoreScopeService : ICurrentUserManageableStoreScopeService
    {
        private readonly IReadOnlyList<string> _storeCodes;

        public FakeStoreScopeService(IReadOnlyList<string> storeCodes)
        {
            _storeCodes = storeCodes;
        }

        public Task<CurrentUserManageableStoreScope> GetScopeAsync()
        {
            return Task.FromResult(
                new CurrentUserManageableStoreScope
                {
                    IsAllowed = true,
                    IsAuthenticated = true,
                    StoreCodes = _storeCodes,
                }
            );
        }

        public Task<IReadOnlyList<string>> GetAccessibleStoreCodesAsync() => Task.FromResult(_storeCodes);

        public Task<bool> CanAccessStoreCodeAsync(string storeCode)
        {
            return Task.FromResult(_storeCodes.Any(item => item.Equals(storeCode, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<bool> CanAccessOrderAsync(string orderGuid) => Task.FromResult(false);

        public Task<bool> CanManageStoreAsync(string storeGuid) => Task.FromResult(false);

        public Task<bool> CanManageUserAsync(string userGuid) => Task.FromResult(false);
    }
}
