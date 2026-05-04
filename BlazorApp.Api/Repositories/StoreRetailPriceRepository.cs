using BlazorApp.Api.Data;
using BlazorApp.Api.Repositories.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Repositories
{
    public sealed class StoreRetailPriceRepository
        : SqlSugarRepository<StoreRetailPrice>,
            IStoreRetailPriceRepository
    {
        public StoreRetailPriceRepository(SqlSugarContext context)
            : base(context) { }

        public ISugarQueryable<StoreRetailPrice> QueryActive()
        {
            return Query().Where(x => x.IsDeleted == false);
        }

        public async Task<StoreRetailPriceDetailDto?> GetDetailByUuidAsync(string uuid)
        {
            return await Query()
                .LeftJoin<Product>((p, prod) => p.ProductCode == prod.ProductCode)
                .LeftJoin<HBLocalSupplier>(
                    (p, prod, sup) => p.SupplierCode == sup.LocalSupplierCode
                )
                .LeftJoin<Store>((p, prod, sup, st) => p.StoreCode == st.StoreCode)
                .Where((p, prod, sup, st) => p.UUID == uuid && p.IsDeleted == false)
                .Select(
                    (p, prod, sup, st) =>
                        new StoreRetailPriceDetailDto
                        {
                            UUID = p.UUID,
                            StoreCode = p.StoreCode,
                            StoreName = st.StoreName,
                            SupplierCode = p.SupplierCode,
                            SupplierName = sup.Name,
                            ProductCode = p.ProductCode,
                            ProductName = prod.ProductName,
                            PurchasePrice = p.PurchasePrice,
                            StoreRetailPriceValue = p.StoreRetailPriceValue,
                            DiscountRate = p.DiscountRate,
                            IsActive = p.IsActive,
                            IsAutoPricing = p.IsAutoPricing,
                            CreatedAt = p.CreatedAt,
                            UpdatedAt = p.UpdatedAt,
                            CreatedBy = p.CreatedBy,
                            UpdatedBy = p.UpdatedBy,
                        }
                )
                .FirstAsync();
        }

        public Task<List<StoreRetailPriceListDto>> GetListByUuidsAsync(List<string> uuids)
        {
            return Query()
                .InnerJoin<Product>((p, prod) => p.ProductCode == prod.ProductCode)
                .LeftJoin<HBLocalSupplier>(
                    (p, prod, sup) => p.SupplierCode == sup.LocalSupplierCode
                )
                .LeftJoin<Store>((p, prod, sup, st) => p.StoreCode == st.StoreCode)
                .Where(
                    (p, prod, sup, st) =>
                        p.UUID != null && uuids.Contains(p.UUID) && p.IsDeleted == false
                )
                .Select(
                    (p, prod, sup, st) =>
                        new StoreRetailPriceListDto
                        {
                            UUID = p.UUID,
                            StoreCode = p.StoreCode,
                            StoreName = st.StoreName,
                            SupplierCode = p.SupplierCode,
                            SupplierName = sup.Name,
                            ProductCode = p.ProductCode,
                            ProductName = prod.ProductName,
                            ProductImage = prod.ProductImage,
                            ItemNumber = prod.ItemNumber,
                            Barcode = prod.Barcode,
                            PurchasePrice = p.PurchasePrice,
                            StoreRetailPriceValue = p.StoreRetailPriceValue,
                            DiscountRate = p.DiscountRate,
                            IsActive = p.IsActive,
                            IsAutoPricing = p.IsAutoPricing,
                            UpdatedBy = p.UpdatedBy,
                            UpdatedAt = p.UpdatedAt,
                            IsSpecialProduct = prod.IsSpecialProduct,
                        }
                )
                .ToListAsync();
        }

        public Task<List<string>> GetActiveStoreCodesAsync()
        {
            return Db.Queryable<Store>()
                .Where(s => s.IsActive == true && s.IsDeleted == false)
                .Select(s => s.StoreCode)
                .ToListAsync();
        }

        public Task<int> SoftDeleteByUuidAsync(string uuid, string updatedBy)
        {
            return Db.Updateable<StoreRetailPrice>()
                .SetColumns(x => x.IsDeleted == true)
                .SetColumns(x => x.UpdatedAt == DateTime.UtcNow)
                .SetColumns(x => x.UpdatedBy == updatedBy)
                .Where(x => x.UUID == uuid)
                .ExecuteCommandAsync();
        }

        public Task<int> SoftDeleteByUuidsAsync(List<string> uuids, string updatedBy)
        {
            return Db.Updateable<StoreRetailPrice>()
                .SetColumns(x => x.IsDeleted == true)
                .SetColumns(x => x.UpdatedAt == DateTime.UtcNow)
                .SetColumns(x => x.UpdatedBy == updatedBy)
                .Where(x => uuids.Contains(x.UUID))
                .ExecuteCommandAsync();
        }
    }
}
