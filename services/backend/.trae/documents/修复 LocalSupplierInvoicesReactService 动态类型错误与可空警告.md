## 错误原因
- 在 DetectBarcodeAsync 中，用 `QueryInChunksAsync<dynamic, string>` + 匿名类型选择，导致泛型期望 `List<dynamic>` 与实际返回 `List<匿名类型>` 不可转换，同时异步 lambda 的委托类型推断不匹配。
- `validBarcodes`、`validItemNumbers`、`productCodes` 等集合类型为 `List<string?>`，传入 `IReadOnlyList<string>` 触发可空差异警告；同时 `chunk.Contains(x.Column)` 在列为 null 时有参数为 null 的警告。

## 修复方案
1) 新增强类型投影
- 在 `LocalSupplierInvoicesReactService` 内新增：
  - `class BarcodeProductProjection { public string Barcode; public string? ProductCode; public string? ProductName; public string? ProductImage; }`
  - `class MultiCodeProductProjection { public string MultiBarcode; public string? ProductCode; public string? Name; public string? Image; }`
  - `class StoreProductCodeProjection { public string ProductCode; public string? StoreProductCode; }`

2) 统一强类型分片查询
- 将三处 `QueryInChunksAsync<dynamic, string>` 改为使用上述投影类型：
  - 条码→商品查询：`QueryInChunksAsync<BarcodeProductProjection, string>`，`Where(p.IsDeleted==false && p.Barcode!=null && chunk.Contains(p.Barcode))`
  - 多码→商品查询：`QueryInChunksAsync<MultiCodeProductProjection, string>`，`Where(m.IsDeleted==false && m.StoreCode==dto.StoreCode && m.MultiBarcode!=null && chunk.Contains(m.MultiBarcode))`
  - 分店商品编码查询：`QueryInChunksAsync<StoreProductCodeProjection, string>`，`Where(x.IsDeleted==false && x.StoreCode==dto.StoreCode && x.ProductCode!=null && chunk.Contains(x.ProductCode))`

3) 规范 keys 集合类型
- 将 `validBarcodes/validItemNumbers/productCodes/allCodes` 统一过滤空白后转为 `List<string>`：例如 `validBarcodes = inputBarcodes.Where(...).Distinct().Select(b => b!).ToList()`。

4) 修复参数为 null 的 Contains 警告
- 在所有 `chunk.Contains(column)` 前加 `column != null` 判定（如 `p.Barcode!=null`、`m.MultiBarcode!=null`、`x.ProductCode!=null`）。

## 验证
- 重新编译确保本文件错误清零。
- 路径行为不变：返回集合与既有结构一致，后续使用字典与遍历保持兼容。

请确认，我将按以上步骤对该文件进行修改并重新诊断。