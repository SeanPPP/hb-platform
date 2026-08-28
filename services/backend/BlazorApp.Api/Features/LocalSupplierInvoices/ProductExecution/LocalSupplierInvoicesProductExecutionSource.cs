using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Features.LocalSupplierInvoices
{
    /// <summary>批量执行的唯一 SQL 读取入口，锁内复读也必须经过这里。</summary>
    internal sealed class LocalSupplierInvoicesProductExecutionSource
    {
        private readonly SqlSugarContext _context;

        public LocalSupplierInvoicesProductExecutionSource(SqlSugarContext context) => _context = context;

        public async Task<ProductExecutionSourceData> LoadInitialAsync(ProductExecutionRequest request) =>
            await LoadAsync(request.InvoiceGuid, request.SelectedDetailGuids, includeProductItemNumbers: false);

        public async Task<ProductExecutionSourceData> ReadLockedAsync(ProductExecutionRequest request) =>
            await LoadAsync(request.InvoiceGuid, request.SelectedDetailGuids, includeProductItemNumbers: true);

        public async Task<bool> ProductExistsByCodeAsync(string productCode) =>
            await _context.Db.Queryable<Product>()
                .AnyAsync(product => product.ProductCode == productCode && product.IsDeleted == false);

        public async Task<bool> StorePriceExistsAsync(string storeCode, string productCode) =>
            await _context.Db.Queryable<StoreRetailPrice>()
                .AnyAsync(price =>
                    price.StoreCode == storeCode
                    && price.ProductCode == productCode
                    && price.IsDeleted == false
                );

        public async Task<bool> HasProductBarcodeAsync(string normalizedBarcode) =>
            await _context.Db.Queryable<Product>().AnyAsync(product =>
                product.IsDeleted == false
                && product.Barcode != null
                && SqlFunc.ToUpper(product.Barcode) == normalizedBarcode
            );

        public async Task<bool> HasStoreMultiCodeBarcodeAsync(string normalizedBarcode) =>
            await _context.Db.Queryable<StoreMultiCodeProduct>().AnyAsync(item =>
                item.IsDeleted == false
                && item.MultiBarcode != null
                && SqlFunc.ToUpper(item.MultiBarcode) == normalizedBarcode
            );

        public async Task<bool> HasProductSetBarcodeAsync(string normalizedBarcode) =>
            await _context.Db.Queryable<ProductSetCode>().AnyAsync(item =>
                item.IsDeleted == false
                && item.SetBarcode != null
                && SqlFunc.ToUpper(item.SetBarcode) == normalizedBarcode
            );

        public async Task<bool> HasSupplierProductIdentityAsync(
            string? supplierCode,
            string? normalizedItemNumber,
            string? normalizedBarcode
        ) =>
            await _context.Db.Queryable<Product>().AnyAsync(product =>
                product.IsDeleted == false
                && product.LocalSupplierCode == supplierCode
                && (
                    (normalizedItemNumber != null && SqlFunc.ToUpper(product.ItemNumber) == normalizedItemNumber)
                    || (normalizedBarcode != null && SqlFunc.ToUpper(product.Barcode) == normalizedBarcode)
                )
            );

        public async Task<bool> BarcodeBelongsToProductAsync(
            string? barcode,
            string? productCode,
            string? storeCode
        )
        {
            var normalizedBarcode = NormalizeCaseInsensitive(barcode);
            var normalizedProductCode = productCode?.Trim();
            if (normalizedBarcode == null || string.IsNullOrWhiteSpace(normalizedProductCode))
                return false;

            var productMatch = await _context.Db.Queryable<Product>().AnyAsync(product =>
                product.IsDeleted == false
                && product.ProductCode == normalizedProductCode
                && product.Barcode != null
                && SqlFunc.ToUpper(product.Barcode) == normalizedBarcode
            );
            if (productMatch)
                return productMatch;

            var normalizedStoreCode = storeCode?.Trim();
            return await _context.Db.Queryable<StoreMultiCodeProduct>().AnyAsync(multiCode =>
                multiCode.IsDeleted == false
                && (normalizedStoreCode == null || multiCode.StoreCode == normalizedStoreCode)
                && multiCode.ProductCode == normalizedProductCode
                && multiCode.MultiBarcode != null
                && SqlFunc.ToUpper(multiCode.MultiBarcode) == normalizedBarcode
            );
        }

        private async Task<ProductExecutionSourceData> LoadAsync(
            string invoiceGuid,
            List<string> detailGuids,
            bool includeProductItemNumbers
        )
        {
            var db = _context.Db;
            var header = await db.Queryable<StoreLocalSupplierInvoice>()
                .Where(invoice => invoice.InvoiceGUID == invoiceGuid && invoice.IsDeleted == false)
                .FirstAsync();
            if (header == null)
                return new ProductExecutionSourceData(null, new(), new(StringComparer.OrdinalIgnoreCase));

            var details = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                .Where(detail =>
                    detail.InvoiceGUID == invoiceGuid
                    && detailGuids.Contains(detail.DetailGUID)
                    && detail.IsDeleted == false
                )
                .ToListAsync();
            var itemNumbers = includeProductItemNumbers
                ? await LoadProductItemNumbersAsync(details)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return new ProductExecutionSourceData(header, details, itemNumbers);
        }

        private async Task<Dictionary<string, string>> LoadProductItemNumbersAsync(
            IEnumerable<StoreLocalSupplierInvoiceDetails> details
        )
        {
            var productCodes = LocalSupplierInvoicesProductExecutionPlan.NormalizeProductCodes(details);
            if (productCodes.Count == 0)
                return new(StringComparer.OrdinalIgnoreCase);

            var products = await _context.Db.Queryable<Product>()
                .Where(product =>
                    product.ProductCode != null
                    && productCodes.Contains(product.ProductCode)
                    && product.IsDeleted == false
                )
                .Select(product => new { product.ProductCode, product.ItemNumber })
                .ToListAsync();
            return products
                .Where(product =>
                    !string.IsNullOrWhiteSpace(product.ProductCode)
                    && !string.IsNullOrWhiteSpace(product.ItemNumber)
                )
                .ToDictionary(product => product.ProductCode!, product => product.ItemNumber!, StringComparer.OrdinalIgnoreCase);
        }

        private static string? NormalizeCaseInsensitive(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    internal sealed record ProductExecutionSourceData(
        StoreLocalSupplierInvoice? Header,
        List<StoreLocalSupplierInvoiceDetails> Details,
        Dictionary<string, string> ProductItemNumbers
    );
}
