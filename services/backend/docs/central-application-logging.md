# 多端中心日志接入说明

## 写入接口

外部项目统一调用：

```http
POST /api/system/logs/ingest
X-Log-Project: hbweb_rv
X-Log-Key: <项目日志密钥>
Content-Type: application/json
```

请求体：

```json
{
  "logs": [
    {
      "level": "Error",
      "message": "请求失败",
      "timestampUtc": "2026-06-05T00:00:00Z",
      "projectCode": "hbweb_rv",
      "environment": "Production",
      "sourceType": "Web",
      "serviceName": "hbweb_rv",
      "traceId": "trace-001",
      "requestPath": "/api/example",
      "requestMethod": "GET",
      "statusCode": 500,
      "userId": "user-guid",
      "userName": "sean",
      "exceptionType": "RequestError",
      "exceptionMessage": "服务器内部错误",
      "stackTrace": "stack...",
      "properties": {
        "screen": "System.Users"
      }
    }
  ]
}
```

必填字段：`level`、`message`、`timestampUtc`、`projectCode`、`environment`、`sourceType`。

单次最多写入 200 条。日志客户端必须异步旁路上报，不能阻塞用户请求、移动端 PDA 操作、收银支付或订单同步。

后端会按项目做每分钟写入限流，默认 `MaxIngestRequestsPerMinute=120`、`MaxIngestLogsPerMinute=5000`。浏览器和移动端的写入 key 会进入运行包，只能视为公开写入凭据；生产环境需要配合限流、项目级保留天数和异常审计使用，不能把它当作长期强密钥。

## 项目配置

后端从 `ApplicationLogging:Projects` 读取允许接入的项目：

配置骨架见 `BlazorApp.Api/appsettings.ApplicationLogging.example.json`，部署时合并到实际 `appsettings` 或环境变量中。

```json
{
  "ProjectCode": "hbweb_rv",
  "DisplayName": "Web 后台",
  "ApiKeyHash": "<sha256-lower-hex>",
  "Enabled": true,
  "RetentionDays": 30
}
```

`ApiKeyHash` 是项目日志密钥的 SHA-256 小写十六进制摘要。明文密钥只放部署环境变量或服务器配置，不提交仓库。

生产 compose 已连续配置五个项目。只有已启用的外部项目需要注入合法的 64 位十六进制 SHA-256 摘要：

```bash
CENTER_LOG_HBWEB_RV_KEY_SHA256=<sha256-lower-hex>
CENTER_LOG_HBPOS_API_KEY_SHA256=<sha256-lower-hex>
```

项目清单和默认保留期：

- `HBBBackend`：内部项目，启用，7 天，不配置外部写入摘要。
- `hbweb_rv`：Web 前端，启用，7 天。
- `HbwebExpo`：移动端，禁用，7 天。
- `hbpos_win`：WPF 客户端，禁用，30 天。
- `hbpos_api`：WPF 收银后端，启用，7 天。

清理任务会覆盖 `Projects` 中的全部项目，包括已禁用项目，避免停用后遗留日志无限保留。

## 查询接口

查询接口需要后台权限 `System.ViewLogs`：

```http
GET /api/system/logs?projectCode=hbweb_rv&level=Error&pageNumber=1&pageSize=50
GET /api/system/logs/{id}
GET /api/system/logs/summary?startUtc=2026-06-05T00:00:00Z
```

常用查询参数：`projectCode`、`environment`、`sourceType`、`level`、`category`、`requestPath`、`traceId`、`userId`、`userName`、`keyword`、`startUtc`、`endUtc`。

`summary` 在原有统计和 `pipeline` 指标之外返回 `status`：后端采集开关、最低等级、默认项目/环境、服务名，以及各项目的启用状态、配置状态、有效保留天数和最后接收时间。内部项目的 `credentialConfigured` 固定为 `null`；外部项目只有启用且摘要合法时为 `Ready`。响应绝不返回密钥、摘要或摘要片段。`lastReceivedAtUtc` 按服务端 `CreatedAt` 的项目最大值计算，不受当前统计筛选影响。

## 各端项目码

- 后端：`HBBBackend`，`sourceType=Backend`
- Web 后台：`hbweb_rv`，`sourceType=Web`
- 移动端：`HbwebExpo`，`sourceType=Mobile`
- 收银端：`hbpos_win`，`sourceType=POS`
- 收银后端：`hbpos_api`，`sourceType=Backend`

## 上报原则

- 错误、异常、关键失败全量上报；高频成功日志采样或不上报。
- 日志接口失败必须吞掉或进入本地小队列，不能影响主业务。
- 不上报 token、密码、完整银行卡信息、授权码、敏感图片 URL。
- API 层记录技术失败，页面/业务层只记录关键业务失败，避免重复上报同一错误。
- `sourceType` 只使用 `Backend`、`Web`、`Mobile`、`POS`；请求错误、页面异常、支付同步等细分来源写入 `category`。
