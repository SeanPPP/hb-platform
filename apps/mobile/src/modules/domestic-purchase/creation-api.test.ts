import assert from "node:assert/strict";
import Module from "node:module";

async function run() {
  Object.assign(globalThis, { __DEV__: false });

  const mockModule = (name: string, exports: object) => {
    const filename = require.resolve(name);
    const module = new Module(filename);
    module.filename = filename;
    module.loaded = true;
    module.exports = exports;
    require.cache[filename] = module;
  };

  // API 契约测试只加载 HTTP 客户端，不启动 Expo 原生运行时。
  mockModule("expo-router", { router: { replace: () => undefined } });
  mockModule("react-native", {
    AppState: { addEventListener: () => ({ remove: () => undefined }) },
    NativeModules: {},
    Platform: { OS: "ios", select: <T>(values: { ios?: T; default?: T }) => values.ios ?? values.default },
  });
  mockModule("expo-secure-store", {
    getItemAsync: async () => null,
    setItemAsync: async () => undefined,
    deleteItemAsync: async () => undefined,
  });
  mockModule("expo-location", {
    hasStartedLocationUpdatesAsync: async () => false,
    stopLocationUpdatesAsync: async () => undefined,
  });
  mockModule("@react-native-async-storage/async-storage", {
    default: {
      getItem: async () => null,
      setItem: async () => undefined,
      removeItem: async () => undefined,
    },
  });

  const { apiClient } = await import("../../shared/api/client");
  const {
    createDomesticProductBatch,
    fetchDomesticProductBatchDetail,
    fetchDomesticSetTemplate,
    fetchDomesticSetTemplates,
    normalizeCreateDomesticProductBatchResult,
    normalizeDomesticSetTemplateDetail,
    normalizeDomesticSetTemplatesResponse,
    saveDomesticSetTemplate,
  } = await import("./api");

  assert.deepEqual(
    normalizeCreateDomesticProductBatchResult({
      BatchNumber: "",
      TotalCreated: "3",
      NormalProductCount: 1,
      SetProductCount: "2",
    }),
    { batchNumber: "", totalCreated: 3, normalProductCount: 1, setProductCount: 2 },
    "创建结果允许空批次号且必须规范化计数",
  );

  const templateRows = normalizeDomesticSetTemplatesResponse([
    {
      TemplateId: "enabled-1",
      SupplierCode: "SUP-1",
      TemplateName: "三件套",
      SetProductName: "礼盒",
      IsEnabled: true,
      SetQuantity: "3",
      UpdatedAt: "2026-09-07T00:00:00Z",
    },
    {
      templateId: "disabled-1",
      supplierCode: "SUP-1",
      templateName: "已停用",
      setProductName: "旧模板",
      isEnabled: false,
    },
    {
      templateId: "other-supplier",
      supplierCode: "SUP-2",
      templateName: "其他供应商",
      setProductName: "礼盒",
      isEnabled: true,
    },
  ], "SUP-1");
  assert.deepEqual(templateRows.map((item) => item.templateId), ["enabled-1"], "模板列表必须隔离供应商并过滤停用项");

  assert.deepEqual(
    normalizeDomesticSetTemplateDetail({
      templateId: "template-1",
      supplierCode: "SUP-1",
      templateName: "三件套",
      setProductName: "礼盒",
      isEnabled: true,
      subItems: [{ productName: "A", privateLabelPrice: "1.25", sortOrder: 0 }],
    }),
    {
      templateId: "template-1",
      supplierCode: "SUP-1",
      templateName: "三件套",
      setProductName: "礼盒",
      isEnabled: true,
      setQuantity: 1,
      updatedAt: undefined,
      subItems: [{ productName: "A", privateLabelPrice: 1.25, sortOrder: 0 }],
    },
    "模板详情必须保留子项顺序、价格和服务端排序号",
  );

  const originalGet = apiClient.get;
  const originalPost = apiClient.post;
  const requests: { method: string; url: string; configOrBody?: unknown; config?: unknown }[] = [];
  const getResponses: unknown[] = [
    {
      data: [
        { templateId: "enabled-1", supplierCode: "SUP-1", templateName: "三件套", setProductName: "礼盒", isEnabled: true, setQuantity: 3 },
        { templateId: "disabled-1", supplierCode: "SUP-1", templateName: "已停用", setProductName: "旧模板", isEnabled: false, setQuantity: 2 },
        { templateId: "other-supplier", supplierCode: "SUP-2", templateName: "越界", setProductName: "礼盒", isEnabled: true, setQuantity: 1 },
      ],
    },
    {
      data: {
        TemplateId: "template/1",
        SupplierCode: "SUP-1",
        TemplateName: "三件套",
        SetProductName: "礼盒",
        IsEnabled: true,
        SubItems: [{ ProductName: "A", PrivateLabelPrice: "1.25", SortOrder: 0 }],
      },
    },
    {
      data: {
        templateId: "template-foreign",
        supplierCode: "SUP-2",
        templateName: "越界",
        setProductName: "礼盒",
        isEnabled: true,
        subItems: [],
      },
    },
    {
      data: {
        templateId: "template-disabled",
        supplierCode: "SUP-1",
        templateName: "停用",
        setProductName: "旧模板",
        isEnabled: false,
        subItems: [],
      },
    },
    {
      data: {
        ProductCode: "relation-row-1",
        HBProductNo: "HB-child-1",
        ProductName: "子项",
        ProductType: 2,
        ParentHBProductNo: "HB-parent-1",
        ParentItemNumber: "wrong-parent",
        ParentProductCode: "relation-parent",
      },
    },
  ];

  apiClient.get = (async (url: string, config?: unknown) => {
    requests.push({ method: "GET", url, configOrBody: config });
    const response = getResponses.shift();
    assert.ok(response, `GET ${url} 必须有模拟响应`);
    if (url.endsWith("/batch/BATCH-1")) {
      const responseData = (response as { data?: unknown }).data;
      return { data: { batchNumber: "BATCH-1", supplierCode: "SUP-1", Items: [responseData] } };
    }
    return response as never;
  }) as typeof apiClient.get;
  apiClient.post = (async (url: string, body?: unknown, config?: unknown) => {
    requests.push({ method: "POST", url, configOrBody: body, config });
    if (url.endsWith("/batch")) {
      return { data: { BatchNumber: "", TotalCreated: 2, NormalProductCount: 2, SetProductCount: 0 } };
    }
    return {
      data: {
        templateId: "saved-1",
        supplierCode: "SUP-1",
        templateName: "保存模板",
        setProductName: "礼盒",
        isEnabled: true,
        setQuantity: 1,
        subItems: [{ productName: "A", privateLabelPrice: 1, sortOrder: 0 }],
      },
    };
  }) as typeof apiClient.post;

  try {
    const payload = {
      supplierCode: "SUP-1",
      items: [{
        productType: 1 as const,
        setQuantity: 2,
        setPrice: 4.5,
        createCount: 2,
        subItems: [{ productType: 2 as const, productName: "A", privateLabelPrice: 1 }],
      }],
    };
    const createResult = await createDomesticProductBatch(payload);
    assert.deepEqual(createResult, { batchNumber: "", totalCreated: 2, normalProductCount: 2, setProductCount: 0 });
    assert.deepEqual(requests[0], { method: "POST", url: "/v1/domestic-product-creation/batch", configOrBody: payload, config: { headers: { "X-Skip-Auth-Recovery": "1" } } });

    const templates = await fetchDomesticSetTemplates(" SUP-1 ");
    assert.deepEqual(templates.map((item) => item.templateId), ["enabled-1"]);
    assert.deepEqual(requests[1], {
      method: "GET",
      url: "/v1/domestic-product-creation/templates",
      configOrBody: { params: { supplierCode: "SUP-1", includeInactive: false } },
    });

    const detail = await fetchDomesticSetTemplate("template/1", "SUP-1");
    assert.equal(detail.subItems[0]?.privateLabelPrice, 1.25);
    assert.equal(requests[2]?.url, "/v1/domestic-product-creation/templates/template%2F1");
    assert.deepEqual(requests[2]?.configOrBody, { params: { supplierCode: "SUP-1" } });

    await assert.rejects(
      () => fetchDomesticSetTemplate("template-foreign", "SUP-1"),
      (error: unknown) => error instanceof Error && (error as Error & { code?: string }).code === "DOMESTIC_SET_TEMPLATE_SCOPE_MISMATCH",
      "模板详情供应商不匹配必须拒绝",
    );
    await assert.rejects(
      () => fetchDomesticSetTemplate("template-disabled", "SUP-1"),
      (error: unknown) => error instanceof Error && (error as Error & { code?: string }).code === "DOMESTIC_SET_TEMPLATE_SCOPE_MISMATCH",
      "模板详情停用时必须拒绝",
    );

    const savePayload = {
      supplierCode: "SUP-1",
      templateName: "保存模板",
      setProductName: "礼盒",
      subItems: [{ productName: "A", privateLabelPrice: 1 }],
    };
    const saved = await saveDomesticSetTemplate(savePayload);
    assert.equal(saved.templateId, "saved-1");
    assert.deepEqual(requests[5], { method: "POST", url: "/v1/domestic-product-creation/templates", configOrBody: savePayload, config: { headers: { "X-Skip-Auth-Recovery": "1" } } });

    const batchDetail = await fetchDomesticProductBatchDetail("BATCH-1");
    assert.equal(batchDetail.items[0]?.productCode, "relation-row-1", "子项编辑必须保留真实 productCode");
    assert.equal(batchDetail.items[0]?.parentItemNumber, "HB-parent-1", "父货号优先使用 parentHBProductNo");
  } finally {
    apiClient.get = originalGet;
    apiClient.post = originalPost;
  }

  console.log("creation-api.test.ts: ok");
}

void run();
