## 结论与调整
- 你后端已允许 `ProductCategoryGUID` 为空（或不做严格验证），当前 400 的主要来源仍需按实际接口校验。
- 为避免 400，我们将按最小必填集构造请求：保留“原商品编码”，并去除/置空 `ProductCategoryGUID`。

## 方案
- 在容器页 `handleCreateNewProducts` 的 `createProduct` payload 中：
  - 设置 `productCategoryGUID: ''`（或不传该字段）
  - 保留：`productCode=原编码`、`productName`、`itemNumber`、`barcode`、`purchasePrice=进口价格`、`retailPrice=贴牌价格`、`isAutoPricing=false`、`localSupplierCode='200'`
- 若后端仍返回 400，将在前端捕获并记录响应体 `message`，在 `message.error` 中提示具体原因。

## 不变项
- 后续仓库/分店/多码的 `ProductCode` 始终沿用原编码
- 资格校验与错误清单提示保持

## 验证
- 重新点击“添加新商品”应不再因 `ProductCategoryGUID` 报 400；若仍有错误，将弹出具体后端消息，便于快速定位。