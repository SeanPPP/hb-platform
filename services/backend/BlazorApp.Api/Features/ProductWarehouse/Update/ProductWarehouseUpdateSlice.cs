using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Features.ProductWarehouse;

internal sealed class ProductWarehouseUpdateSlice
    : ProductWarehouseSliceBase,
      IProductWarehouseUpdateSlice
{
    internal ProductWarehouseUpdateSlice(ProductWarehouseSliceContext context)
        : base(context) { }

    /// <summary>
    /// 仓库商品完整更新：同一 db 顺序查、一次性取列表，事务内更新 DomesticProduct、Product、WarehouseProduct、StoreRetailPrice、StoreMultiCodeProduct、ProductSetCode。
    /// 分店零售价强联动：StoreRetailPriceValue / MultiCodeRetailPrice 用主表零售价（OEM）覆盖，PurchasePrice 用进口价覆盖。
    /// </summary>
    public Task<WarehouseProductFullUpdateResultDto> FullUpdateAsync(
        string productCode,
        WarehouseProductFullUpdateDto dto
    )
    {
        return FullUpdateAsync(productCode, dto, SystemUpdatedBy);
    }

    public async Task<WarehouseProductFullUpdateResultDto> FullUpdateAsync(
        string productCode,
        WarehouseProductFullUpdateDto dto,
        string? updatedBy
    )
    {
        var result = new WarehouseProductFullUpdateResultDto
        {
            Success = false,
            Message = "更新失败",
        };
        if (string.IsNullOrWhiteSpace(productCode) || dto == null)
        {
            result.Message = "商品编码或请求体为空";
            return result;
        }

        var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);

        try
        {
            _context.Db.Ado.BeginTran();
            var setChildPurchasePriceLock =
                await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                    _context.Db,
                    new[] { productCode }
                );

            // 1. WarehouseProduct 作为同商品写入门闩；统一先锁仓库商品，再读取国内商品与主商品。
            var warehouseProduct = await WithWarehouseProductUpdateLock(
                    _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(w => w.ProductCode == productCode)
                )
                .FirstAsync();
            if (warehouseProduct == null)
            {
                _context.Db.Ado.RollbackTran();
                result.Message = "仓库商品不存在";
                return result;
            }
            var domesticProduct = await WithWarehouseProductUpdateLock(
                    _context
                        .Db.Queryable<DomesticProduct>()
                        .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                )
                .FirstAsync();
            var product = await WithWarehouseProductUpdateLock(
                    _context.Db.Queryable<Product>().Where(p => p.ProductCode == productCode)
                )
                .FirstAsync();
            if (product == null)
            {
                _context.Db.Ado.RollbackTran();
                result.Message = "商品不存在（Product 表无此 ProductCode）";
                return result;
            }
            var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                new[] { productCode }
            );
            var storeRetailPrices = await _context
                .Db.Queryable<StoreRetailPrice>()
                .Where(srp => srp.ProductCode == productCode && !srp.IsDeleted)
                .ToListAsync();
            var storeMultiCodeProducts = await _context
                .Db.Queryable<StoreMultiCodeProduct>()
                .Where(mcp => mcp.ProductCode == productCode && !mcp.IsDeleted)
                .ToListAsync();
            var productSetCodes = await _context
                .Db.Queryable<ProductSetCode>()
                .Where(psc => psc.ProductCode == productCode && !psc.IsDeleted)
                .ToListAsync();

            var now = DateTime.Now;

            var shouldInsertDomesticProduct = false;
            if (
                domesticProduct == null
                && !string.IsNullOrWhiteSpace(dto.SupplierCode)
            )
            {
                // 用户在仓库商品中明确选择国内供应商时，补建或恢复国内商品映射，
                // 避免非国内导入商品出现“保存成功但供应商未落库”。
                domesticProduct = await WithWarehouseProductUpdateLock(
                        _context
                            .Db.Queryable<DomesticProduct>()
                            .Where(p => p.ProductCode == productCode && p.IsDeleted)
                    )
                    .FirstAsync();
                if (domesticProduct == null)
                {
                    shouldInsertDomesticProduct = true;
                    domesticProduct = new DomesticProduct
                    {
                        ProductCode = productCode,
                        ProductName = product.ProductName,
                        EnglishProductName = product.EnglishName,
                        HBProductNo = product.ItemNumber,
                        Barcode = product.Barcode,
                        ProductType = product.ProductType ?? dto.ProductType,
                        DomesticPrice = warehouseProduct.DomesticPrice,
                        OEMPrice = warehouseProduct.OEMPrice ?? product.RetailPrice,
                        ImportPrice = warehouseProduct.ImportPrice ?? product.PurchasePrice,
                        PackingQuantity = warehouseProduct.PackingQuantity,
                        UnitVolume = warehouseProduct.Volume,
                        MiddlePackQuantity = warehouseProduct.MinOrderQuantity,
                        ProductImage = product.ProductImage,
                        IsActive = warehouseProduct.IsActive,
                        IsDeleted = false,
                        CreatedAt = now,
                        CreatedBy = effectiveUpdatedBy,
                    };
                }
                else
                {
                    domesticProduct.ProductName = product.ProductName;
                    domesticProduct.EnglishProductName = product.EnglishName;
                    domesticProduct.HBProductNo = product.ItemNumber;
                    domesticProduct.Barcode = product.Barcode;
                    domesticProduct.ProductType = product.ProductType ?? dto.ProductType;
                    domesticProduct.DomesticPrice = warehouseProduct.DomesticPrice;
                    domesticProduct.OEMPrice =
                        warehouseProduct.OEMPrice ?? product.RetailPrice;
                    domesticProduct.ImportPrice =
                        warehouseProduct.ImportPrice ?? product.PurchasePrice;
                    domesticProduct.PackingQuantity = warehouseProduct.PackingQuantity;
                    domesticProduct.UnitVolume = warehouseProduct.Volume;
                    domesticProduct.MiddlePackQuantity = warehouseProduct.MinOrderQuantity;
                    domesticProduct.ProductImage = product.ProductImage;
                    domesticProduct.IsActive = warehouseProduct.IsActive;
                    domesticProduct.IsDeleted = false;
                }
            }

            // 2. 更新 DomesticProduct；明确选择供应商时允许补建国内商品映射。
            if (domesticProduct != null)
            {
                if (dto.ProductName != null)
                    domesticProduct.ProductName = dto.ProductName;
                if (dto.EnglishName != null)
                    domesticProduct.EnglishProductName = dto.EnglishName;
                if (dto.ProductSpecification != null)
                    domesticProduct.ProductSpecification = dto.ProductSpecification;
                domesticProduct.ProductType = dto.ProductType;
                if (dto.DomesticPrice.HasValue)
                    domesticProduct.DomesticPrice = dto.DomesticPrice;
                if (dto.OEMPrice.HasValue)
                    domesticProduct.OEMPrice = dto.OEMPrice;
                if (dto.ImportPrice.HasValue)
                    domesticProduct.ImportPrice = dto.ImportPrice;
                if (dto.PackingQuantity.HasValue)
                    domesticProduct.PackingQuantity = dto.PackingQuantity;
                if (dto.UnitVolume.HasValue)
                    domesticProduct.UnitVolume = dto.UnitVolume;
                // MinOrderQuantity 与中包数量同源：仅回写有效国内商品，不改 Product.MiddlePackageQuantity。
                if (dto.MinOrderQuantity.HasValue)
                    domesticProduct.MiddlePackQuantity = dto.MinOrderQuantity;
                if (dto.MiddlePackQuantity.HasValue)
                    domesticProduct.MiddlePackQuantity = dto.MiddlePackQuantity;
                if (dto.ProductImage != null)
                {
                    domesticProduct.ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                        dto.ProductImage,
                        domesticProduct.HBProductNo ?? domesticProduct.ProductCode
                    );
                }
                domesticProduct.IsActive = dto.IsActive;
                if (dto.SupplierCode != null)
                    domesticProduct.SupplierCode = dto.SupplierCode;
                domesticProduct.UpdatedAt = now;
                domesticProduct.UpdatedBy = effectiveUpdatedBy;
                if (shouldInsertDomesticProduct)
                    await _context.Db.Insertable(domesticProduct).ExecuteCommandAsync();
                else
                    await _context.Db.Updateable(domesticProduct).ExecuteCommandAsync();
            }

            // 3. 更新 Product
            if (dto.ProductName != null)
                product.ProductName = dto.ProductName;
            if (dto.EnglishName != null)
                product.EnglishName = dto.EnglishName;
            if (dto.ImportPrice.HasValue)
                product.PurchasePrice = dto.ImportPrice;
            if (dto.OEMPrice.HasValue)
                product.RetailPrice = dto.OEMPrice;
            if (dto.WarehouseCategoryGUID != null)
                product.WarehouseCategoryGUID = dto.WarehouseCategoryGUID;
            product.ProductType = dto.ProductType;
            product.IsAutoPricing = dto.IsAutoPricing;
            if (dto.MiddlePackQuantity.HasValue)
                product.MiddlePackageQuantity = dto.MiddlePackQuantity;
            if (dto.LocalSupplierCode != null)
                product.LocalSupplierCode = dto.LocalSupplierCode;
            if (dto.ProductImage != null)
            {
                product.ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                    dto.ProductImage,
                    product.ItemNumber ?? product.ProductCode ?? string.Empty
                );
            }
            product.IsActive = dto.IsActive;
            product.UpdatedAt = now;
            product.UpdatedBy = effectiveUpdatedBy;
            await _context
                .Db.Updateable(product)
                .UpdateColumns(p => new
                {
                    p.ProductName,
                    p.EnglishName,
                    p.PurchasePrice,
                    p.RetailPrice,
                    p.WarehouseCategoryGUID,
                    p.ProductType,
                    p.IsAutoPricing,
                    p.MiddlePackageQuantity,
                    p.LocalSupplierCode,
                    p.ProductImage,
                    p.IsActive,
                    p.UpdatedAt,
                    p.UpdatedBy,
                })
                .ExecuteCommandAsync();

            // 4. 更新 WarehouseProduct
            if (dto.DomesticPrice.HasValue)
                warehouseProduct.DomesticPrice = dto.DomesticPrice;
            if (dto.OEMPrice.HasValue)
                warehouseProduct.OEMPrice = dto.OEMPrice;
            if (dto.ImportPrice.HasValue)
                warehouseProduct.ImportPrice = dto.ImportPrice;
            if (dto.UnitVolume.HasValue)
                warehouseProduct.Volume = dto.UnitVolume;
            if (dto.MinOrderQuantity.HasValue)
                warehouseProduct.MinOrderQuantity = dto.MinOrderQuantity;
            warehouseProduct.IsActive = dto.IsActive;
            warehouseProduct.UpdatedAt = now;
            warehouseProduct.UpdatedBy = effectiveUpdatedBy;
            await _context
                .Db.Updateable(warehouseProduct)
                .UpdateColumns(w => new
                {
                    w.DomesticPrice,
                    w.OEMPrice,
                    w.ImportPrice,
                    w.Volume,
                    w.MinOrderQuantity,
                    w.IsActive,
                    w.UpdatedAt,
                    w.UpdatedBy,
                })
                .ExecuteCommandAsync();

            // 5. 强联动：批量更新 StoreRetailPrice（主表零售价/进货价覆盖）
            var mainRetail = dto.OEMPrice ?? product.RetailPrice;
            var mainPurchase = dto.ImportPrice ?? product.PurchasePrice;
            foreach (var srp in storeRetailPrices)
            {
                srp.StoreRetailPriceValue = mainRetail;
                srp.PurchasePrice = mainPurchase;
                srp.IsActive = dto.IsActive;
                srp.UpdatedAt = now;
            }
            if (storeRetailPrices.Any())
            {
                await _context
                    .Db.Updateable(storeRetailPrices)
                    .UpdateColumns(srp => new
                    {
                        srp.StoreRetailPriceValue,
                        srp.PurchasePrice,
                        srp.IsActive,
                        srp.UpdatedAt,
                    })
                    .ExecuteCommandAsync();
            }

            // 6. 强联动：批量更新 StoreMultiCodeProduct
            var activeSetChildCodes = new HashSet<string>(
                productSetCodes
                    .Where(psc =>
                        IsCostDerivedSetType(psc.SetType)
                        && psc.IsActive
                        && !string.IsNullOrWhiteSpace(psc.SetProductCode)
                    )
                    .Select(psc => psc.SetProductCode!),
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var mcp in storeMultiCodeProducts)
            {
                // 套装子项保留自身零售价，进货价在明细更新完成后按兄弟子项零售价统一分摊。
                // 普通多码商品继续沿用主商品价格联动行为。
                if (
                    string.IsNullOrWhiteSpace(mcp.MultiCodeProductCode)
                    || !activeSetChildCodes.Contains(mcp.MultiCodeProductCode)
                )
                {
                    mcp.MultiCodeRetailPrice = mainRetail;
                    mcp.PurchasePrice = mainPurchase;
                }
                mcp.IsActive = dto.IsActive;
                mcp.UpdatedAt = now;
            }
            if (storeMultiCodeProducts.Any())
            {
                await _context
                    .Db.Updateable(storeMultiCodeProducts)
                    .UpdateColumns(mcp => new
                    {
                        mcp.MultiCodeRetailPrice,
                        mcp.PurchasePrice,
                        mcp.IsActive,
                        mcp.UpdatedAt,
                    })
                    .ExecuteCommandAsync();
            }

            // 7. 条码价明细：按条码/SetCodeId/MultiCodeUuid 更新 ProductSetCode 与 StoreMultiCodeProduct
            if (dto.BarcodePrices != null && dto.BarcodePrices.Any())
            {
                foreach (var item in dto.BarcodePrices)
                {
                    if (!string.IsNullOrWhiteSpace(item.SetCodeId))
                    {
                        var setCode = productSetCodes.FirstOrDefault(psc =>
                            psc.SetCodeId == item.SetCodeId
                        );
                        if (setCode != null)
                        {
                            if (item.RetailPrice.HasValue)
                                setCode.SetRetailPrice = item.RetailPrice;
                            if (
                                item.PurchasePrice.HasValue
                                && !IsCostDerivedSetType(setCode.SetType)
                            )
                                setCode.SetPurchasePrice = item.PurchasePrice;
                            setCode.UpdatedAt = now;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(item.MultiCodeUuid))
                    {
                        var mcp = storeMultiCodeProducts.FirstOrDefault(x =>
                            x.UUID == item.MultiCodeUuid
                        );
                        if (mcp != null)
                        {
                            if (item.RetailPrice.HasValue)
                                mcp.MultiCodeRetailPrice = item.RetailPrice;
                            if (
                                item.PurchasePrice.HasValue
                                && (
                                    string.IsNullOrWhiteSpace(mcp.MultiCodeProductCode)
                                    || !activeSetChildCodes.Contains(mcp.MultiCodeProductCode)
                                )
                            )
                                mcp.PurchasePrice = item.PurchasePrice;
                            mcp.UpdatedAt = now;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(item.Barcode))
                    {
                        var setCode = productSetCodes.FirstOrDefault(psc =>
                            psc.SetBarcode == item.Barcode
                        );
                        if (setCode != null)
                        {
                            if (item.RetailPrice.HasValue)
                                setCode.SetRetailPrice = item.RetailPrice;
                            if (
                                item.PurchasePrice.HasValue
                                && !IsCostDerivedSetType(setCode.SetType)
                            )
                                setCode.SetPurchasePrice = item.PurchasePrice;
                            setCode.UpdatedAt = now;
                        }
                        var mcp = storeMultiCodeProducts.FirstOrDefault(x =>
                            x.MultiBarcode == item.Barcode
                        );
                        if (mcp != null)
                        {
                            if (item.RetailPrice.HasValue)
                                mcp.MultiCodeRetailPrice = item.RetailPrice;
                            if (
                                item.PurchasePrice.HasValue
                                && (
                                    string.IsNullOrWhiteSpace(mcp.MultiCodeProductCode)
                                    || !activeSetChildCodes.Contains(mcp.MultiCodeProductCode)
                                )
                            )
                                mcp.PurchasePrice = item.PurchasePrice;
                            mcp.UpdatedAt = now;
                        }
                    }
                }
                if (productSetCodes.Any())
                {
                    await _context
                        .Db.Updateable(productSetCodes)
                        .UpdateColumns(psc => new
                        {
                            psc.SetRetailPrice,
                            psc.SetPurchasePrice,
                            psc.UpdatedAt,
                        })
                        .ExecuteCommandAsync();
                }
                if (storeMultiCodeProducts.Any())
                {
                    await _context
                        .Db.Updateable(storeMultiCodeProducts)
                        .UpdateColumns(mcp => new
                        {
                            mcp.MultiCodeRetailPrice,
                            mcp.PurchasePrice,
                            mcp.UpdatedAt,
                        })
                        .ExecuteCommandAsync();
                }
            }

            // 套装子项成本是派生值：忽略客户端提交的套装进货价，按最新主成本和全部子项零售价重算。
            var recalculation = await new SetChildPurchasePriceService(
                _context.Db
            ).RecalculateLockedAsync(
                setChildPurchasePriceLock,
                new[] { productCode },
                storeCodes: null,
                updatedBy: effectiveUpdatedBy
            );
            EnsureSetChildPurchasePriceRecalculated(
                recalculation,
                "完整更新后的套装子项成本重算不完整"
            );

            await RecordProductChangeHistoryAsync(
                beforeSnapshots,
                new[] { productCode },
                action: "Update",
                source: "WarehouseProducts",
                actorName: effectiveUpdatedBy
            );

            _context.Db.Ado.CommitTran();
            result.Success = true;
            result.Message = "更新成功";
        }
        catch (Exception ex)
        {
            _context.Db.Ado.RollbackTran();
            if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _))
            {
                throw;
            }
            _logger.LogError(ex, "仓库商品完整更新失败 ProductCode={ProductCode}", productCode);
            result.Message = "更新失败: " + ex.Message;
        }

        return result;
    }

    /// <summary>
    /// 仓库商品窄列 PATCH：一次只更新一个非负字段，事务内窄列更新并记录当前操作人。
    /// MinOrderQuantity 同步 WP.MinOrderQuantity 与有效 DP.MiddlePackQuantity；
    /// DomesticPrice 同步 WP 与有效 DP；
    /// ImportPrice 同步 WP、有效 DP、Product.PurchasePrice 与全部启用未删除分店的有效进货价（缺失补建）；
    /// OEMPrice 同步 WP、有效 DP、Product.RetailPrice 与全部启用未删除分店的零售价（缺失补建）。
    /// 不联动套装/多码/批量，不复活软删价格，另一列价格不动。
    /// </summary>
    public Task<WarehouseProductPatchResultDto?> PatchAsync(
        string productCode,
        WarehouseProductPatchDto dto
    )
    {
        return PatchAsync(productCode, dto, SystemUpdatedBy);
    }

    public async Task<WarehouseProductPatchResultDto?> PatchAsync(
        string productCode,
        WarehouseProductPatchDto dto,
        string? updatedBy
    )
    {
        if (string.IsNullOrWhiteSpace(productCode))
            throw new InvalidOperationException("商品编码不能为空");
        if (dto == null)
            throw new InvalidOperationException("请求数据不能为空");
        var validationError = WarehouseProductPatchDto.Validate(dto);
        if (validationError != null)
            throw new InvalidOperationException(validationError);

        var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);
        var now = DateTime.Now;

        await _context.Db.Ado.BeginTranAsync();
        try
        {
            var setChildPurchasePriceLock =
                await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                    _context.Db,
                    new[] { productCode }
                );
            // SQL Server 使用更新锁串行化同一商品的局部更新；其他数据库保持普通事务查询。
            var warehouseProduct = await WithWarehouseProductUpdateLock(
                    _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(w => w.ProductCode == productCode && !w.IsDeleted)
                )
                .FirstAsync();
            if (warehouseProduct == null)
            {
                await _context.Db.Ado.RollbackTranAsync();
                return null;
            }
            var domesticProduct = await WithWarehouseProductUpdateLock(
                    _context
                        .Db.Queryable<DomesticProduct>()
                        .Where(dp => dp.ProductCode == productCode && !dp.IsDeleted)
                )
                .FirstAsync();
            var product = await WithWarehouseProductUpdateLock(
                    _context
                        .Db.Queryable<Product>()
                        .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                )
                .FirstAsync();
            if (product == null)
            {
                await _context.Db.Ado.RollbackTranAsync();
                return null;
            }
            var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                new[] { productCode }
            );

            // 窄列更新 WarehouseProduct：只写本次请求字段与审计列，避免覆盖并发库存或价格。
            var warehouseUpdate = _context
                .Db.Updateable<WarehouseProduct>()
                .SetColumns(w => w.UpdatedAt == now)
                .SetColumns(w => w.UpdatedBy == effectiveUpdatedBy)
                .Where(w => w.ProductCode == productCode && !w.IsDeleted);
            if (dto.MinOrderQuantity.HasValue)
            {
                warehouseUpdate = warehouseUpdate.SetColumns(w =>
                    w.MinOrderQuantity == dto.MinOrderQuantity.Value
                );
            }
            if (dto.DomesticPrice.HasValue)
            {
                warehouseUpdate = warehouseUpdate.SetColumns(w =>
                    w.DomesticPrice == dto.DomesticPrice.Value
                );
            }
            if (dto.ImportPrice.HasValue)
            {
                warehouseUpdate = warehouseUpdate.SetColumns(w =>
                    w.ImportPrice == dto.ImportPrice.Value
                );
            }
            if (dto.OEMPrice.HasValue)
            {
                warehouseUpdate = warehouseUpdate.SetColumns(w =>
                    w.OEMPrice == dto.OEMPrice.Value
                );
            }
            var warehouseAffected = await warehouseUpdate.ExecuteCommandAsync();
            if (warehouseAffected <= 0)
            {
                await _context.Db.Ado.RollbackTranAsync();
                return null;
            }

            // 更新触发器或并发状态可能改变主商品；复读后再计算分店缺失记录的另一列价格。
            product = await WithWarehouseProductUpdateLock(
                    _context
                        .Db.Queryable<Product>()
                        .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                )
                .FirstAsync();
            if (product == null)
            {
                await _context.Db.Ado.RollbackTranAsync();
                return null;
            }

            // 有效国内商品窄列联动；不创建 DomesticProduct。
            if (domesticProduct != null)
            {
                var domesticUpdate = _context
                    .Db.Updateable<DomesticProduct>()
                    .SetColumns(dp => dp.UpdatedAt == now)
                    .SetColumns(dp => dp.UpdatedBy == effectiveUpdatedBy)
                    .Where(dp => dp.ProductCode == productCode && !dp.IsDeleted);
                if (dto.MinOrderQuantity.HasValue)
                {
                    domesticUpdate = domesticUpdate.SetColumns(dp =>
                        dp.MiddlePackQuantity == dto.MinOrderQuantity.Value
                    );
                }
                if (dto.DomesticPrice.HasValue)
                {
                    domesticUpdate = domesticUpdate.SetColumns(dp =>
                        dp.DomesticPrice == dto.DomesticPrice.Value
                    );
                }
                if (dto.ImportPrice.HasValue)
                {
                    domesticUpdate = domesticUpdate.SetColumns(dp =>
                        dp.ImportPrice == dto.ImportPrice.Value
                    );
                }
                if (dto.OEMPrice.HasValue)
                {
                    domesticUpdate = domesticUpdate.SetColumns(dp =>
                        dp.OEMPrice == dto.OEMPrice.Value
                    );
                }
                await domesticUpdate.ExecuteCommandAsync();
            }

            if (dto.ImportPrice.HasValue)
            {
                // 进口价同步主表进货价与全部启用未删除分店的有效进货价；另一列零售价不动。
                var productAffected = await _context
                    .Db.Updateable<Product>()
                    .SetColumns(p => p.UpdatedAt == now)
                    .SetColumns(p => p.UpdatedBy == effectiveUpdatedBy)
                    .SetColumns(p => p.PurchasePrice == dto.ImportPrice.Value)
                    .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    .ExecuteCommandAsync();
                if (productAffected <= 0)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    return null;
                }
                product = await WithWarehouseProductUpdateLock(
                        _context
                            .Db.Queryable<Product>()
                            .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    )
                    .FirstAsync();
                if (product == null)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    return null;
                }
                await UpsertActiveStoreRetailPricesAsync(
                    product,
                    purchasePrice: dto.ImportPrice,
                    retailPrice: null,
                    now,
                    effectiveUpdatedBy
                );
            }
            if (dto.OEMPrice.HasValue)
            {
                // 零售价同步主表零售价与全部启用未删除分店的零售价；另一列进货价不动。
                var productAffected = await _context
                    .Db.Updateable<Product>()
                    .SetColumns(p => p.UpdatedAt == now)
                    .SetColumns(p => p.UpdatedBy == effectiveUpdatedBy)
                    .SetColumns(p => p.RetailPrice == dto.OEMPrice.Value)
                    .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    .ExecuteCommandAsync();
                if (productAffected <= 0)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    return null;
                }
                product = await WithWarehouseProductUpdateLock(
                        _context
                            .Db.Queryable<Product>()
                            .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    )
                    .FirstAsync();
                if (product == null)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    return null;
                }
                await UpsertActiveStoreRetailPricesAsync(
                    product,
                    purchasePrice: null,
                    retailPrice: dto.OEMPrice,
                    now,
                    effectiveUpdatedBy
                );
            }

            if (dto.ImportPrice.HasValue)
            {
                var recalculation = await new SetChildPurchasePriceService(
                    _context.Db
                ).RecalculateLockedAsync(
                    setChildPurchasePriceLock,
                    new[] { productCode },
                    storeCodes: null,
                    updatedBy: effectiveUpdatedBy
                );
                EnsureSetChildPurchasePriceRecalculated(
                    recalculation,
                    "仓库商品更新后的套装子项成本重算不完整"
                );
            }

            await RecordProductChangeHistoryAsync(
                beforeSnapshots,
                new[] { productCode },
                action: "Patch",
                source: "WarehouseProducts",
                actorName: effectiveUpdatedBy
            );

            await _context.Db.Ado.CommitTranAsync();
        }
        catch
        {
            await _context.Db.Ado.RollbackTranAsync();
            throw;
        }

        return new WarehouseProductPatchResultDto { Success = true, Message = "保存成功" };
    }

    public Task<BatchToggleWarehouseProductsActiveResultDto> BatchToggleActiveAsync(
        BatchToggleWarehouseProductsActiveRequestDto request
    )
    {
        return BatchToggleActiveAsync(request, SystemUpdatedBy);
    }

    public async Task<BatchToggleWarehouseProductsActiveResultDto> BatchToggleActiveAsync(
        BatchToggleWarehouseProductsActiveRequestDto request,
        string? updatedBy
    )
    {
        var result = new BatchToggleWarehouseProductsActiveResultDto
        {
            Success = false,
            Message = "上下架失败",
        };

        if (request == null || request.ProductCodes == null || !request.ProductCodes.Any())
        {
            result.Message = "商品编码不能为空";
            return result;
        }

        var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);
        var batchGuid = Guid.NewGuid();

        var productCodes = request
            .ProductCodes.Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct()
            .ToList();

        if (!productCodes.Any())
        {
            result.Message = "商品编码不能为空";
            return result;
        }

        try
        {
            _context.Db.Ado.BeginTran();

            var existingCodes = await _context
                .Db.Queryable<WarehouseProduct>()
                .Where(w => productCodes.Contains(w.ProductCode) && !w.IsDeleted)
                .Select(w => w.ProductCode)
                .ToListAsync();

            var now = DateTime.Now;
            var validWarehouseProductCodes = existingCodes.Distinct().ToList();
            var existingCodeSet = validWarehouseProductCodes.ToHashSet();
            var missingCodes = productCodes
                .Where(code => !existingCodeSet.Contains(code))
                .ToList();
            var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                validWarehouseProductCodes
            );

            if (validWarehouseProductCodes.Any())
            {
                await _context
                    .Db.Updateable<WarehouseProduct>()
                    .SetColumns(w => w.IsActive == request.IsActive)
                    .SetColumns(w => w.UpdatedAt == now)
                    .SetColumns(w => w.UpdatedBy == effectiveUpdatedBy)
                    .Where(w => validWarehouseProductCodes.Contains(w.ProductCode) && !w.IsDeleted)
                    .ExecuteCommandAsync();

                await _context
                    .Db.Updateable<Product>()
                    .SetColumns(p => p.IsActive == request.IsActive)
                    .SetColumns(p => p.UpdatedAt == now)
                    .SetColumns(p => p.UpdatedBy == effectiveUpdatedBy)
                    .Where(p =>
                        p.ProductCode != null && validWarehouseProductCodes.Contains(p.ProductCode)
                    )
                    .ExecuteCommandAsync();

                await _context
                    .Db.Updateable<DomesticProduct>()
                    .SetColumns(dp => dp.IsActive == request.IsActive)
                    .SetColumns(dp => dp.UpdatedAt == now)
                    .SetColumns(dp => dp.UpdatedBy == effectiveUpdatedBy)
                    .Where(dp => validWarehouseProductCodes.Contains(dp.ProductCode) && !dp.IsDeleted)
                    .ExecuteCommandAsync();

                await _context
                    .Db.Updateable<StoreRetailPrice>()
                    .SetColumns(srp => srp.IsActive == request.IsActive)
                    .SetColumns(srp => srp.UpdatedAt == now)
                    .Where(srp =>
                        srp.ProductCode != null
                        && validWarehouseProductCodes.Contains(srp.ProductCode)
                        && !srp.IsDeleted
                    )
                    .ExecuteCommandAsync();

                await _context
                    .Db.Updateable<StoreMultiCodeProduct>()
                    .SetColumns(mcp => mcp.IsActive == request.IsActive)
                    .SetColumns(mcp => mcp.UpdatedAt == now)
                    .Where(mcp =>
                        mcp.ProductCode != null
                        && validWarehouseProductCodes.Contains(mcp.ProductCode)
                        && !mcp.IsDeleted
                    )
                    .ExecuteCommandAsync();
            }

            await RecordProductChangeHistoryAsync(
                beforeSnapshots,
                validWarehouseProductCodes,
                action: "ToggleActive",
                source: "WarehouseProducts",
                actorName: effectiveUpdatedBy,
                batchGuid: batchGuid
            );

            _context.Db.Ado.CommitTran();

            result.Success = missingCodes.Count == 0;
            result.SuccessCount = validWarehouseProductCodes.Count;
            result.FailedCount = missingCodes.Count;
            if (missingCodes.Any())
            {
                result.Errors.AddRange(missingCodes.Select(code => $"仓库商品不存在: {code}"));
            }
            result.Message = request.IsActive
                ? (missingCodes.Any() ? "部分商品上架成功" : "批量上架成功")
                : (missingCodes.Any() ? "部分商品下架成功" : "批量下架成功");
        }
        catch (Exception ex)
        {
            _context.Db.Ado.RollbackTran();
            _logger.LogError(ex, "仓库商品批量上下架失败");
            result.Success = false;
            result.SuccessCount = 0;
            result.Message = "批量上下架失败: " + ex.Message;
            result.Errors.Add(ex.Message);
        }

        return result;
    }
}
