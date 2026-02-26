## 概览
- 前端 TypeScript：属性大小写不一致导致类型错误（camelCase vs PascalCase）。
- 后端 C#：普遍的可空性与集合使用问题（Contains/TryGetValue 传入可能为 null、`List<string?>` 与 `List<string>` 不匹配、`ToDictionary` 键 notnull 约束、恒真/恒假表达式、过时 API）。
- 共享模型与文档：`required`/默认初始化缺失、XML `<typeparam>` 注释缺失、少量代码风格与格式化提醒。

## 前端修复（ReactUmi）
- 修正属性名大小写：`SupplierItemDetectResult` 在前端类型为 `productImage`，后端 JSON 默认 camelCase；当前使用了 `ProductImage`。
  - 变更：`ReactUmi/my-app/src/components/OpenSourceGrid.tsx:40` 将 `detect?.ProductImage` 改为 `detect?.productImage`，其余逻辑不变。
- 同步核对 `@/types/localSupplierInvoice` 中 `SupplierItemDetectResult` 的属性定义，确保为 camelCase（`productImage`、`exists`、`error` 等）。

## 后端修复（C# 可空性与集合）
- 通用策略
  - 在调用 `List<T>.Contains(x)`、`Dictionary<K,V>.TryGetValue(k, out v)` 前，统一做非空过滤：`if (!string.IsNullOrEmpty(x)) ...`。
  - 对 `List<string?>`/`HashSet<string?>` 等，先过滤并投影为非空集合：`var keys = source.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();`。
  - `ToDictionary` 键必须满足 `notnull`：先过滤分组键非空，再使用 `Key!`。
  - 消除恒真/恒假表达式：例如 `int` 与 `null` 比较、`bool` 与 `null` 比较。
  - `string.Join` 保护空集合：`string.Join(sep, values ?? Enumerable.Empty<string>())`。

- 代表性文件与改动
  - `BlazorApp.Api/Services/WarehouseProductBatchService.cs`
    - `165`：`pl.ProductCode != null && productCodes.Contains(pl.ProductCode)`。
    - `169–171`：`GroupBy(x => x.ProductCode).Where(g => g.Key != null).ToDictionary(g => g.Key!, g => g.Select(x => x.Location).Where(l => l != null).ToList())`。
  - `BlazorApp.Api/Services/React/ProductWarehouseReactService.cs`
    - `294`：查询前加 `p.ProductCode != null` 再 `Contains`。
    - `301`：`if (p.ProductCode != null && importDict.TryGetValue(p.ProductCode, out var importPrice))`。
    - `717`：移除 `dp.ProductType != null &&`（若 `ProductType` 为非可空 `int`），直接 `values.Contains(dp.ProductType.ToString())`。
  - `BlazorApp.Api/Services/React/LocalSupplierInvoicesReactService.cs`
    - `704`：将 `List<string?>` 传参改为非空过滤后的 `IReadOnlyList<string>`。
    - `710`, `743`, `832`：对 `chunk.Contains(p.Barcode)` 等在参数可能为 `null` 时先判空；并按照提示进行多行格式化。
    - `905`, `916`：按格式化提示拆行与对象初始化对齐。
  - `BlazorApp.Api/Services/React/DataSyncReactService.cs`
    - `929–946`：`List<string?>` → 过滤并投影为 `List<string>`。
  - `BlazorApp.Api/Services/ProductSyncService.cs`
    - `255`：`if (!string.IsNullOrEmpty(key) && dict.TryGetValue(key, out var v))`。
    - `368`：`if (!string.IsNullOrWhiteSpace(item) && list.Contains(item))`。
  - `BlazorApp.Api/Services/React/WarehouseCategoryReactService.cs`
    - `119`：移除 `bool` 与 `null` 比较的分支。
    - `284`, `298`：在 `Contains` 前判空或过滤集合。
  - `BlazorApp.Api/Services/WarehouseProductService.cs`
    - `75, 119, 151, 169, 186, 232, 396, 462, 570`：按成员使用点位补充 `null` 守卫或安全访问（`?.`/早返回）。
    - `230`：`Contains` 前确保参数非空。
  - `BlazorApp.Api/Services/YiwuContainerService.cs`
    - `84, 977, 1015, 1126`：解引用前判空。
    - `1022`：`string.Join` 传入 `values ?? Enumerable.Empty<string>()`。
    - `1185`：`Contains` 前判空。
  - `BlazorApp.Api/Services/React/StoreMultiCodePricesReactService.cs`
    - `528`：`Contains` 前判空。
  - `BlazorApp.Api/Services/React/ContainerReactService.cs`
    - `598`：`Contains` 前判空。
  - `BlazorApp.Api/Services/React/HqProductTranslationReactService.cs`
    - `37`：`Contains` 前判空。
  - `BlazorApp.Api/Services/ProductLocationService.cs`
    - `74`：解引用前判空。
  - `BlazorApp.Api/Services/DomesticSetProductService.cs`
    - `333`：可能的 null 赋值处，改目标为可空或先判空再赋值。
  - `BlazorApp.Api/Services/React/ProductReactService.cs`
    - `160, 213`：可能的 null 赋值 → 改为可空或判空后赋值。
  - `BlazorApp.Api/Services/React/ProductWarehouseReactService.cs`
    - `294, 301, 362, 375, 390, 717`：如上综合处理。
  - `BlazorApp.Api/Services/React/StoreRetailPriceReactService.cs`
    - `799`：`Contains` 前判空。

## 共享模型与文档修复（BlazorApp.Shared）
- `HBweb/*` 与 `HqEntities/*`
  - 为不可为 null 的属性添加 `required` 或默认初始化：
    - `PricingStrategy.cs:10`、`PricingStrategyDetail.cs:9, 11`、`PricingStrategyTarget.cs:10, 12`、`DIC_商品零售价表.cs:11–16, 26–27, 45`、`CPT_RED_货柜单主表.cs:138` 等。
    - 例如：`public required string Id { get; set; }` 或 `public string Id { get; set; } = string.Empty;`；集合类型初始化为 `new List<...>()`。
  - `PricingStrategyTarget.cs`：调整 `using` 顺序为 `System.Collections.Generic;` 再 `SqlSugar`（风格警告）。
- 其他微调
  - `ProductCreateWithPricesDtos.cs:23`：按提示插入换行以通过格式化检查。
  - `StoreMultiCodeProduct.cs:40`：删除多余字符。
- XML 文档
  - `BlazorApp.Api/Utils/BatchOperationHelper.cs:137, 185`：补齐 `<typeparam name="TResult">`、`<typeparam name="TKey">` 注释标签。

## 安全性更新
- `BlazorApp.Api/Utils/PasswordHasher.cs:105`：用 `RandomNumberGenerator.Create()`/`RandomNumberGenerator.GetInt32()` 替换 `RNGCryptoServiceProvider`：
  - 生成随机索引：`var idx = RandomNumberGenerator.GetInt32(allChars.Length);`。
  - 填充随机字节：`RandomNumberGenerator.Fill(buffer);` 或使用实例 `rng.GetBytes(buffer)`（非过时）。

## 验证与回归
- 前端：`npm start` 编译无 TS/Lint 错误，手动检查图片列在 `OpenSourceGrid` 正常显示；与后端返回的 `productImage` 字段一致。
- 后端：
  - 编译通过，无可空性警告；关键路径（查询、批量更新/创建）跑通。
  - 针对 `Contains/TryGetValue` 判空逻辑，添加 2–3 个快速单元测试覆盖 `null`/非 `null` 情况。
- 共享模型：运行时创建相关实体不再抛出 `required`/非空初始化异常。

## 执行与交付
- 分批提交以降低风险：
  1) 前端属性修复（单文件变更）。
  2) 后端可空性与集合使用修复（按服务分组）。
  3) 共享模型与文档、密码工具更新。
- 每批次附带变更说明与受影响路径；完成后再次编译运行进行端到端验证。

请确认以上计划，确认后我将按此逐项修改并验证。