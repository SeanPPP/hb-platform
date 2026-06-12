namespace BlazorApp.Shared.DTOs
{
    public class SyncProductsToStoresResult
    {
        public int TotalProducts { get; set; }

        public int TotalStores { get; set; }

        public int StoreMultiCodeProductCreatedCount { get; set; }

        public int StoreMultiCodeProductUpdatedCount { get; set; }

        public int StoreRetailPriceCreatedCount { get; set; }

        public int StoreRetailPriceUpdatedCount { get; set; }

        public int CreatedCount { get; set; }

        public int UpdatedCount { get; set; }

        public int FailedCount { get; set; }

        public List<string> Errors { get; set; } = new();
    }
}
