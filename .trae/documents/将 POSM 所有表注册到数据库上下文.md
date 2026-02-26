## 将 POSM 所有表注册到 POSMSqlSugarContext

### 需要注册的表（7 个）
1. BankTransaction - 银行交易表
2. CashUp - 日结单表  
3. CustomerInfo - 顾客信息表
4. PaymentDetail - 支付明细表
5. SalesOrder - 销售订单主表
6. SalesOrderDetail - 销售订单明细表
7. SalesReturnRecord - 销售退货记录表

### 修改内容
1. **添加 using 引用** - 导入所有 POSM 模型
2. **注册 SimpleClient 属性** - 为每个表创建简化数据访问属性（类似现有的 DeviceRegistrationDb）

**注意**：不修改 InitializeTablesAsync() 和 ForceRecreateTablesAsync() 方法