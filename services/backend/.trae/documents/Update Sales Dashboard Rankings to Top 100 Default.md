I will update `SalesDashboardController.cs` to change the default value of the `topN` parameter from its current values (20 or 50) to **100** for all relevant methods.

### Affected Methods:
1.  **`GetStoreSalesRank`**: Change `topN = 50` to `topN = 100`.
2.  **`GetSupplierSalesRank`**: Change `topN = 20` to `topN = 100`.
3.  **`GetChinaSupplierSalesRankAsync`**: Change `topN = 20` to `topN = 100`.
4.  **`GetStoreSupplierSales`**: Change `topN = 20` to `topN = 100`.

This change will ensure that by default, all sales ranking endpoints return the top 100 records unless specified otherwise by the client.