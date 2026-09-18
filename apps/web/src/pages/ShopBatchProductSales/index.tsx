import BatchProductSalesAnalysisPage from '../ExecutiveSalesIntelligence/BatchProductSalesAnalysis'

/** 订货前台「货号销量」：完整复用后台批量货号销量页面，门店范围由后端按前台权限放开为全部分店。 */
export default function ShopBatchProductSalesPage() {
  return (
    <div className="shop-feature-page">
      <BatchProductSalesAnalysisPage />
    </div>
  )
}
