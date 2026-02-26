## 目标与原因
- 统一主键类型为 `Guid`，与 `Product` 模型保持一致，减少字符串主键带来的类型不一致与序列化问题。
- 使用 `UuidHelper.GenerateUuid7()` 生成的 UUIDv7，改为 `Guid.Parse(UuidHelper.GenerateUuid7())` 初始化 `Guid` 主键。

## 影响范围
- 已确认需要修改的文件：
  - `BlazorApp.Shared/Models/HBweb/StoreMultiCodeProduct.cs:17`（当前为 `public string UUID { get; set; }`）
  - `BlazorApp.Shared/Models/HBweb/StoreRetailPrice.cs:17`
  - `BlazorApp.Shared/Models/HBweb/StoreClearancePrice.cs:17`
- 已确认无需修改：
  - `BlazorApp.Shared/Models/HBweb/Product.cs:15-17` 已是 `Guid` 类型，无需变更。
- 可选一致性改进（非本次必做）：
  - `ProductSetCode.cs` 的主键字段名为 `SetCodeId` 而非 `UUID`，若需要也可改为 `Guid` 类型以统一主键类型，但本次按“仅修改 string UUID”为准不变更。

## 实施步骤
1. 将上述三个文件中的 `UUID` 属性类型由 `string` 改为 `Guid`。
2. 初始化方式改为：`public Guid UUID { get; set; } = Guid.Parse(UuidHelper.GenerateUuid7());`
3. 保留现有 `SqlSugar` 注解（如 `IsPrimaryKey = true`、`ColumnName = "uuid"` 等），仅调整类型与初始化表达式。
4. 全局搜索引用：检查是否有对 `UUID` 字段的字符串依赖（如作为字符串进行拼接、比较、序列化），若存在则改为 `Guid` 处理或调用 `UUID.ToString()`。

## 数据库与兼容性
- SqlSugar 映射：`Guid` 在 SQL Server 通常映射为 `uniqueidentifier`，在 MySQL 通常映射为 `char(36)`。请按当前数据库类型确认列类型。
- 若使用 Code-First 自动建表/更新：执行初始化以更新列类型；若使用已有表：
  - SQL Server：`ALTER TABLE <table> ALTER COLUMN uuid uniqueidentifier NOT NULL;`
  - MySQL：`ALTER TABLE <table> MODIFY COLUMN uuid CHAR(36) NOT NULL;`
- 序列化/前端：`Guid` 将以标准 GUID 字符串序列化；若前端表单控件绑定为文本，需要确保绑定兼容（读取为字符串时使用 `ToString()`，写入时解析为 `Guid`）。

## 验证
- 编译解决方案，确保无类型错误。
- 启动应用，执行与这三张表相关的新增/查询/保存流程，验证 `UUID` 正常生成为 GUIDv7、持久化与导航属性正常。
- 如有现存数据，抽样验证旧数据是否成功迁移或能被正确读取（必要时执行列类型转换与数据清洗）。

## 变更预览（示例）
- 以 `StoreRetailPrice.cs` 为例：
  - 变更前：`public string UUID { get; set; } = UuidHelper.GenerateUuid7();`
  - 变更后：`public Guid UUID { get; set; } = Guid.Parse(UuidHelper.GenerateUuid7());`

请确认以上方案，确认后我将执行代码修改并完成验证。