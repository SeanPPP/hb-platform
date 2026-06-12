以下是针对 `/BlazorApp.Api/Services/React/StoreRetailPriceReactService.cs` 的查询优化方案，聚焦“商品信息与分店价格分表联查”的性能提升。

## 目标
- 降低大数据量下的查询耗时与锁等待。
- 让索引更易命中，减少全表扫描与排序代价。

## 查询层优化
- 在联表前尽量缩小主表范围：将等值筛选（如 `storeCode`、`supplierCode`、`productCode`）优先施加在 `StoreRetailPrice` 基表上。
- 对联接表增加“未删除”过滤：`prod.IsDeleted == false`、`sup.IsDeleted == false`、`st.IsDeleted == false`（若表含该字段），避免无效数据进入联表集合。
- 使用 `With(SqlWith.NoLock)`：减少锁竞争与等待（SQL Server），在读多写少场景显著改善并发响应。
- 统一对“编码类字段”优先使用前缀匹配：`StartsWith`/`LIKE 'xxx%'` 更易命中索引；保留名称类字段的 `Contains`。
- 用 `ToPageListAsync` 一次性获取 `items` 与 `total`：避免 `CountAsync` + `Skip/Take` 的双次扫描。

## 代码改动建议（示例片段）
- 构造查询：
```csharp
var db = _context.Db;
var pageIndex = (request.StartRow / request.PageSize) + 1;
var pageSize = request.PageSize;

var baseQuery = db.Queryable<StoreRetailPrice>()
    .With(SqlWith.NoLock)
    .Where(p => p.IsDeleted == false);

// 等值过滤优先在主表施加（存在则执行）
if (request.FilterModel?.TryGetValue("storeCode", out var fStore) == true &&
    !string.IsNullOrWhiteSpace(fStore.Filter) && (fStore.Type?.ToLower() == "equals"))
{
    var v = fStore.Filter.Trim();
    baseQuery = baseQuery.Where(p => p.StoreCode == v);
}
if (request.FilterModel?.TryGetValue("supplierCode", out var fSup) == true &&
    !string.IsNullOrWhiteSpace(fSup.Filter) && (fSup.Type?.ToLower() == "equals"))
{
    var v = fSup.Filter.Trim();
    baseQuery = baseQuery.Where(p => p.SupplierCode == v);
}
if (request.FilterModel?.TryGetValue("productCode", out var fProd) == true &&
    !string.IsNullOrWhiteSpace(fProd.Filter) && (fProd.Type?.ToLower() == "equals"))
{
    var v = fProd.Filter.Trim();
    baseQuery = baseQuery.Where(p => p.ProductCode == v);
}

var query = baseQuery
    .InnerJoin<Product>((p, prod) => p.ProductCode == prod.ProductCode)
    .LeftJoin<HBLocalSupplier>((p, prod, sup) => p.SupplierCode == sup.LocalSupplierCode)
    .LeftJoin<Store>((p, prod, sup, st) => p.StoreCode == st.StoreCode)
    .Where((p, prod, sup, st) => prod.IsDeleted == false)
    .WhereIF(true, (p, prod, sup, st) => sup == null || sup.IsDeleted == false)
    .WhereIF(true, (p, prod, sup, st) => st == null || st.IsDeleted == false);

// GlobalSearch：编码类用前缀匹配，名称类保留 contains
if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
{
    var keyword = request.GlobalSearch.Trim();
    var longEnough = keyword.Length >= 2;
    query = query.Where((p, prod, sup, st) =>
        (p.StoreCode != null && (longEnough ? p.StoreCode.StartsWith(keyword) : p.StoreCode.Contains(keyword)))
        || (p.SupplierCode != null && (longEnough ? p.SupplierCode.StartsWith(keyword) : p.SupplierCode.Contains(keyword)))
        || (p.ProductCode != null && (longEnough ? p.ProductCode.StartsWith(keyword) : p.ProductCode.Contains(keyword)))
        || (prod.ItemNumber != null && (longEnough ? prod.ItemNumber.StartsWith(keyword) : prod.ItemNumber.Contains(keyword)))
        || (st.StoreName != null && st.StoreName.Contains(keyword))
        || (sup.Name != null && sup.Name.Contains(keyword))
        || (prod.ProductName != null && prod.ProductName.Contains(keyword))
    );
}

// 排序保持原逻辑...

var totalRef = new RefAsync<int>(0);
var items = await query
    .Select((p, prod, sup, st) => new StoreRetailPriceListDto { /* 原选择字段 */ })
    .ToPageListAsync(pageIndex, pageSize, totalRef);

return GridResponseDto<StoreRetailPriceListDto>.OK(items, totalRef.Value);
```

## 索引优化建议（SQL Server 示例）
- `StoreRetailPrice`：
```sql
CREATE NONCLUSTERED INDEX IX_SRP_Store_Product_Supplier
ON dbo.StoreRetailPrice(StoreCode, ProductCode, SupplierCode)
INCLUDE (PurchasePrice, StoreRetailPriceValue, DiscountRate, UpdatedAt, IsActive, IsAutoPricing);
```
- `Product`：
```sql
CREATE NONCLUSTERED INDEX IX_Product_ProductCode
ON dbo.Product(ProductCode)
INCLUDE (ProductName, ItemNumber, Barcode, ProductImage, IsSpecialProduct);

CREATE NONCLUSTERED INDEX IX_Product_ItemNumber ON dbo.Product(ItemNumber);
```
- `HBLocalSupplier`：
```sql
CREATE NONCLUSTERED INDEX IX_LocalSupplier_Code ON dbo.HBLocalSupplier(LocalSupplierCode)
INCLUDE (Name);
```
- `Store`：
```sql
CREATE NONCLUSTERED INDEX IX_Store_StoreCode ON dbo.Store(StoreCode)
INCLUDE (StoreName);
```

## 风险与说明
- `NOLOCK` 可能读取未提交数据（脏读），请按业务容忍度使用。
- 索引创建需评估写入成本与磁盘占用，建议在测试环境验证行数与执行计划后再应用到生产。

确认后，我将按上述方案重构 `GetGridDataAsync` 并提交代码，随后配合数据库创建建议索引脚本。