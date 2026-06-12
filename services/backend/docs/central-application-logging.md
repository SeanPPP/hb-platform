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

环境变量示例：

```bash
ApplicationLogging__Projects__1__ApiKeyHash=<sha256-lower-hex>
ApplicationLogging__Projects__2__ApiKeyHash=<sha256-lower-hex>
ApplicationLogging__Projects__3__ApiKeyHash=<sha256-lower-hex>
```

## 查询接口

查询接口需要后台权限 `System.ViewLogs`：

```http
GET /api/system/logs?projectCode=hbweb_rv&level=Error&pageNumber=1&pageSize=50
GET /api/system/logs/{id}
GET /api/system/logs/summary?startUtc=2026-06-05T00:00:00Z
```

常用查询参数：`projectCode`、`environment`、`sourceType`、`level`、`category`、`requestPath`、`traceId`、`userId`、`userName`、`keyword`、`startUtc`、`endUtc`。

## 各端项目码

- 后端：`HBBBackend`，`sourceType=Backend`
- Web 后台：`hbweb_rv`，`sourceType=Web`
- 移动端：`HbwebExpo`，`sourceType=Mobile`
- 收银端：`hbpos_win`，`sourceType=POS`

## 上报原则

- 错误、异常、关键失败全量上报；高频成功日志采样或不上报。
- 日志接口失败必须吞掉或进入本地小队列，不能影响主业务。
- 不上报 token、密码、完整银行卡信息、授权码、敏感图片 URL。
- API 层记录技术失败，页面/业务层只记录关键业务失败，避免重复上报同一错误。
- `sourceType` 只使用 `Backend`、`Web`、`Mobile`、`POS`；请求错误、页面异常、支付同步等细分来源写入 `category`。
