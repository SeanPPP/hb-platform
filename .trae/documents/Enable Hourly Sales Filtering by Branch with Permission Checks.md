我将修改 `SalesDashboardController.cs`，实现分时销售（Hourly Sales）数据的分店筛选功能，并确保权限安全。

### 具体修改内容：
1.  **更新 `GetHourlySales` 方法**：
    -   修改方法签名，增加可选参数 `[FromQuery] List<string>? branchCodes`。
    -   实现权限与筛选逻辑：
        -   **前端指定了分店 (`branchCodes` 有值)**：
            -   **管理员**：直接使用前端传递的 `branchCodes` 进行查询。
            -   **普通用户**：将前端请求的 `branchCodes` 与用户有权访问的 `userBranchCodes` 取**交集**。这样既满足了筛选需求，又防止了越权访问。
        -   **前端未指定分店**：
            -   默认使用 `userBranchCodes`，即查询用户有权访问的所有分店。

这一修改将修复“点击分店销售排行后，分时销售没有过滤出对应分店数据”的问题。