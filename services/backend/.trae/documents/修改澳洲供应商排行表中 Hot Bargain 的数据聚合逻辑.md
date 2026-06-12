## 修改澳洲供应商排行表中 Hot Bargain 的数据聚合逻辑

### 修改文件
- `d:\Development\cline\blazor\BlazorApp.Api\Services\React\SalesDashboardReactService.cs`

### 修改内容

在 `GetSupplierSalesRankAsync` 方法中：

1. **新增国内供应商合计查询**：查询 `IsDomestic == true` 的所有供应商数据并计算合计
2. **修改结果构建逻辑**：
   - 当供应商代码为 `200`（Hot Bargain）时：
     - `TotalAmount` = Hot Bargain 自身金额 + 国内供应商合计金额
     - `TotalQuantity` = Hot Bargain 自身数量 + 国内供应商合计数量
     - `StoreCount` = 合并去重后的门店数
   - 其他供应商保持原逻辑
3. **对比期数据处理**：对比期数据也需要同样的聚合逻辑

### 修改步骤
1. 在方法开始处查询国内供应商的当前期和对比期合计数据
2. 在构建 DTO 时判断供应商代码是否为 200
3. 如果是 200，则将国内供应商的合计数据累加到该供应商
4. 测试验证 Hot Bargain 的金额是否正确包含国内供应商数据