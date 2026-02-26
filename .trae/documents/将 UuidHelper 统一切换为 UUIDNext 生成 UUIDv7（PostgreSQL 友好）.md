## 调整目标
- 修改 `BlazorApp.Shared/Helper/UuidHelper.cs` 的生成方法，改用 `UUIDNext.Uuid.NewDatabaseFriendly(Database.PostgreSql)` 生成 UUIDv7。
- 保持原有 API 兼容（`GenerateUuid7()` 返回无连字符，`GenerateUuid7WithHyphens()` 返回带连字符）。

## 具体修改
- 顶部添加命名空间：`using UUIDNext;`
- `GenerateUuid7()` 实现：
  - 返回：`Uuid.NewDatabaseFriendly(Database.PostgreSql).ToString("N")`
- `GenerateUuid7WithHyphens()` 实现：
  - 返回：`Uuid.NewDatabaseFriendly(Database.PostgreSql).ToString()`
- 其他方法（`IsValidUuid7`、`ExtractTimestampFromUuid7`）维持现状，无需改动。

## 影响与验证
- 所有调用 `UuidHelper.GenerateUuid7()` 的实体/服务自动使用更稳定的 UUIDv7 实现，避免自行拼装字节的潜在不一致。
- 编译通过：`Database` 枚举来源于 `UUIDNext` 命名空间，会随 `using UUIDNext;` 正确解析。
- 现有数据与 API 行为保持一致，仅内部生成方式改变。