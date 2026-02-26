## 修改计划

### 问题 1: ABN 没有显示出来

**可能原因：**
- Store 查询失败（store 为 null）
- 数据库中 Store 表的 ABN 字段为空或 null

**排查方案：**
1. 检查 Store 表中对应分店的 ABN 数据是否已填写
2. 确认 `order.BranchCode` 与 `store.StoreCode` 的匹配是否正确

**代码位置：**
- [TaxInvoiceService.cs#L70-L72](file:///d:/Development/cline/blazor/BlazorApp.Api/Services/TaxInvoiceService.cs#L70-L72) - Store 查询
- [TaxInvoiceService.cs#L135-L140](file:///d:/Development/cline/blazor/BlazorApp.Api/Services/TaxInvoiceService.cs#L135-L140) - ABN 显示逻辑

---

### 问题 2: Tax Invoice Preview 下载按钮国际化

**当前问题：**
下载按钮的文本是硬编码的 "Download"，需要使用国际化

**修改位置：**
- [index.tsx#L646-L652](file:///d:/Development/cline/blazor/ReactUmi/my-app/src/pages/PosmSalesOrders/index.tsx#L646-L652)

**需要修改：**
```tsx
// 从：
<DownloadOutlined />
onClick={() => handleDownloadPdf(selectedOrderGuid)}
>
  Download
</Button>

// 改为：
<DownloadOutlined />
onClick={() => handleDownloadPdf(selectedOrderGuid)}
>
  {intl.formatMessage({ id: 'posmSalesOrder.downloadPdf' })}
</Button>
```

**国际化 key 已存在：**
- `posmSalesOrder.downloadPdf` 已在 [en-US.ts#L903](file:///d:/Development/cline/blazor/ReactUmi/my-app/src/locales/en-US.ts#L903) 和 [zh-CN.ts#L819](file:///d:/Development/cline/blazor/ReactUmi/my-app/src/locales/zh-CN.ts#L819) 中定义

---

## 实施步骤

1. **添加下载按钮国际化** - 将硬编码的 "Download" 改为使用 `intl.formatMessage`
2. **调查 ABN 显示问题** - 检查数据库中 Store 表的 ABN 字段数据
3. **测试验证** - 确保 ABN 和地址正确显示，下载按钮文本国际化生效