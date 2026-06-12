## 目标
- 新增后端控制器支持“按货柜编号”触发翻译：查找对应主表与所有明细的商品编码，批量将商品字典中的中文名称（不空且含中文）翻译为英文，并且只回写 `英文名称`。
- 控制器与服务层位于 React 目录，复用现有 Kimi 翻译服务与 React 翻译服务的写入逻辑。

## 实现方案
- 控制器：`BlazorApp.Api/Controllers/React/ReactHqProductTranslationController.cs`
  - 新增接口：`POST /api/react/v1/hq-products/translate-by-container-number`
    - 请求体：`{ containerNumbers: string[], overwriteExisting?: boolean }`
    - 处理流程：
      1. 使用 `HqSqlSugarContext.Db.Queryable<CPT_RED_货柜单主表Store>()` 查询这些 `货柜编号` 的 `HGUID`（主表GUID）。
      2. 调用已存在的 `IHqProductTranslationReactService.TranslateNamesByContainersAsync(hguids, overwriteExisting)`。
      3. 返回统计结果：候选数量、成功、跳过、失败及示例映射。
  - 额外接口（可选）：`POST /api/react/v1/hq-products/translate-by-container-number/{containerNumber}`，针对单个编号便捷触发。
  - 授权：`[Authorize(Roles = "Admin,WarehouseManager")]`。

- 服务层：复用已实现的 `HqProductTranslationReactService`
  - 无需新增翻译逻辑；仅在控制器中增加“编号→主表GUID”映射：
    - `var hguids = await _hq.Db.Queryable<CPT_RED_货柜单主表Store>().Where(x => containerNumbers.Contains(x.货柜编号!)).Select(x => x.HGUID!).Distinct().ToListAsync();`
    - 过滤掉空 GUID 后传入现有方法。

## 规则与边界
- 翻译触发条件：只处理商品字典中 `中文名称` 非空且包含中文的记录。
- 写入策略：默认不覆盖已有有效英文（非中文）；仅将 `英文名称` 字段更新。
- 覆盖选项：`overwriteExisting` 为 true 时允许覆盖（可选）。

## 请求/响应示例
- 请求（批量）：
```json
POST /api/react/v1/hq-products/translate-by-container-number
{
  "containerNumbers": ["HB-2024-001", "HB-2024-002"],
  "overwriteExisting": false
}
```
- 响应：
```json
{
  "success": true,
  "data": {
    "totalCandidates": 120,
    "totalTranslated": 90,
    "totalSkipped": 28,
    "totalFailed": 2,
    "samples": { "苹果": "Apple", "衣服": "Clothes" }
  }
}
```

## 测试
- 新增 `.http` 脚本：按编号批量触发、单个编号触发；核查返回统计与商品字典 `英文名称` 字段变化。

## 变更范围
- 仅新增控制器方法（React 目录）与测试脚本；不改动现有翻译服务实现。

请确认本方案，确认后我将按此实现并提交接口与测试脚本。