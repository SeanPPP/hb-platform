import { beforeEach, describe, expect, it, jest } from "@jest/globals";
import { fireEvent, render, waitFor } from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import {
  resolveSpecialProductsAccess,
  SPECIAL_PRODUCTS_ADD_TO_CART_PERMISSION,
  SPECIAL_PRODUCTS_MANAGE_PERMISSION,
  SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
  SPECIAL_PRODUCTS_VIEW_PERMISSION,
  SpecialProductsScreen,
  type SpecialProductsScreenPresenter,
  type SpecialProductsState,
} from "./index";

import type { SpecialProductItem } from "@/core/contracts";

jest.mock("@/ui/feedback", () => ({
  usePosSound: () => ({ play: jest.fn() }),
}));

let mockLanguage: "en" | "zh" = "zh";

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: mockLanguage, resolvedLanguage: mockLanguage },
  }),
}));

describe("SpecialProductsScreen", () => {
  beforeEach(() => {
    mockLanguage = "zh";
  });

  it("离线显示本地列表并允许加购，但所有管理写操作禁用且触控至少 44pt", async () => {
    const presenter = new ScreenPresenter({
      items: [product("A"), product("B")],
      online: false,
    });
    const onBack = jest.fn();
    const screen = await render(
      <SpecialProductsScreen onBack={onBack} presenter={presenter} />,
    );

    // 卡片整卡即可加购（触屏目标），离线时管理写操作仍禁用
    const cardA = screen.getByTestId("special-product-card-A");
    expect(cardA).toBeTruthy();
    expect(screen.getByTestId("special-products-offline-note")).toBeTruthy();
    const download = screen.getByTestId("special-products-download");
    const remove = screen.getByTestId("special-products-remove-A");
    const moveDown = screen.getByTestId("special-products-move-down-A");
    for (const control of [cardA, download, remove, moveDown]) {
      expect(StyleSheet.flatten(control.props.style).minHeight).toBeGreaterThanOrEqual(
        SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
      );
    }
    expect(cardA.props.accessibilityState).toEqual({ disabled: false });
    expect(download.props.accessibilityState).toEqual({ disabled: true });
    expect(remove.props.accessibilityState).toEqual({ disabled: true });
    expect(moveDown.props.accessibilityState).toEqual({ disabled: true });

    await fireEvent.press(cardA);
    expect(presenter.addToCartCalls).toEqual(["A"]);
    await fireEvent.press(download);
    expect(presenter.downloadCalls).toBe(0);

    const back = screen.getByTestId("special-products-back");
    expect(StyleSheet.flatten(back.props.style).minHeight).toBeGreaterThanOrEqual(
      SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
    );
    await fireEvent.press(back);
    expect(onBack).toHaveBeenCalledTimes(1);
    await screen.unmount();
  });

  it("仅 View 权限可浏览卡片，但不可加购且不显示管理面板", async () => {
    const presenter = new ScreenPresenter({
      items: [product("A")],
      permissions: [SPECIAL_PRODUCTS_VIEW_PERMISSION],
    });
    const screen = await render(
      <SpecialProductsScreen presenter={presenter} />,
    );

    const cardA = screen.getByTestId("special-product-card-A");
    expect(cardA).toBeTruthy();
    expect(cardA.props.accessibilityState).toEqual({ disabled: true });
    expect(screen.queryByTestId("special-products-management")).toBeNull();
    expect(screen.queryByTestId("special-products-remove-A")).toBeNull();
    expect(
      StyleSheet.flatten(
        screen.getByTestId("special-products-refresh-local").props.style,
      ).minHeight,
    ).toBeGreaterThanOrEqual(SPECIAL_PRODUCTS_MIN_TOUCH_TARGET);
    await screen.unmount();
  });

  it("卡片显示商品图片，无图或加载失败时回落文字占位", async () => {
    const withImage: SpecialProductItem = {
      ...product("IMG"),
      productImage: "data:image/png;base64,iVBORw0KGgo=",
    };
    const fileUri: SpecialProductItem = {
      ...product("FILE"),
      productImage: "file:///etc/passwd",
    };
    const presenter = new ScreenPresenter({
      items: [withImage, product("PLAIN"), fileUri],
    });
    const screen = await render(
      <SpecialProductsScreen presenter={presenter} />,
    );

    // 有图卡片渲染图片容器且无占位；无图卡片显示商品名首字符占位
    // （占位是装饰性文本带 accessibilityElementsHidden，需显式查询隐藏元素）
    expect(
      screen.getByTestId("special-product-card-image-IMG-content"),
    ).toBeTruthy();
    expect(
      screen.queryByTestId("special-product-card-image-IMG-placeholder", {
        includeHiddenElements: true,
      }),
    ).toBeNull();
    expect(
      screen.getByTestId("special-product-card-image-PLAIN-placeholder", {
        includeHiddenElements: true,
      }),
    ).toBeTruthy();
    // 协议白名单：非 https/http/data:image 的 uri（如 file://）不进入渲染管线
    expect(
      screen.queryByTestId("special-product-card-image-FILE-content"),
    ).toBeNull();
    expect(
      screen.getByTestId("special-product-card-image-FILE-placeholder", {
        includeHiddenElements: true,
      }),
    ).toBeTruthy();

    // 图片加载失败时回落为文字占位
    fireEvent(
      screen.getByTestId("special-product-card-image-IMG-content"),
      "error",
    );
    await waitFor(() => {
      expect(
        screen.getByTestId("special-product-card-image-IMG-placeholder", {
          includeHiddenElements: true,
        }),
      ).toBeTruthy();
    });
    await screen.unmount();
  });

  it("在线管理把候选搜索、标记和排序交给 presenter", async () => {
    const presenter = new ScreenPresenter({
      candidates: [product("C")],
      items: [product("A"), product("B")],
      online: true,
    });
    const screen = await render(
      <SpecialProductsScreen presenter={presenter} />,
    );
    const keyboardScroll = screen.getByTestId(
      "special-products-management-keyboard-scroll",
    );
    expect(keyboardScroll.props.automaticallyAdjustKeyboardInsets).toBe(true);
    expect(keyboardScroll.props.keyboardDismissMode).toBe("interactive");
    expect(keyboardScroll.props.keyboardShouldPersistTaps).toBe("handled");

    await fireEvent.changeText(
      screen.getByTestId("special-products-search-input"),
      "Product C",
    );
    await fireEvent.press(screen.getByTestId("special-products-search"));
    expect(presenter.searchCalls).toBe(1);
    expect(presenter.searchQueries).toEqual(["Product C"]);

    await fireEvent.press(screen.getByTestId("special-products-mark-C"));
    expect(presenter.markCalls).toEqual([
      { isSpecialProduct: true, productCode: "C" },
    ]);
    await fireEvent.press(
      screen.getByTestId("special-products-move-down-A"),
    );
    expect(presenter.reorderCalls).toEqual([
      { delta: 1, productCode: "A" },
    ]);
    await screen.unmount();
  });

  it("失败状态只显示稳定安全文案", async () => {
    const presenter = new ScreenPresenter({
      items: [product("A")],
      online: true,
      statusCode: "download-failed",
    });
    const screen = await render(
      <SpecialProductsScreen presenter={presenter} />,
    );

    expect(screen.getByText(/下载未完成/)).toBeTruthy();
    expect(screen.queryByText(/very-secret/)).toBeNull();
    expect(screen.queryByText(/https:\/\/host/)).toBeNull();
    await screen.unmount();
  });

  it("按当前语言只显示一种特殊商品文案，旧双语字符串不会出现", async () => {
    const chinese = await render(
      <SpecialProductsScreen presenter={new ScreenPresenter()} />,
    );
    expect(chinese.getByText("特殊商品")).toBeTruthy();
    expect(chinese.queryByText("Special products")).toBeNull();
    expect(chinese.queryByText("特殊商品 / Special products")).toBeNull();
    await chinese.unmount();

    mockLanguage = "en";
    const english = await render(
      <SpecialProductsScreen presenter={new ScreenPresenter()} />,
    );
    expect(english.getByText("Special products")).toBeTruthy();
    expect(english.queryByText("特殊商品")).toBeNull();
    expect(english.queryByText("特殊商品 / Special products")).toBeNull();
    await english.unmount();
  });
});

class ScreenPresenter implements SpecialProductsScreenPresenter {
  public readonly addToCartCalls: string[] = [];
  public downloadCalls = 0;
  public readonly markCalls: {
    productCode: string;
    isSpecialProduct: boolean;
  }[] = [];
  public readonly reorderCalls: {
    productCode: string;
    delta: -1 | 1;
  }[] = [];
  public searchCalls = 0;
  public readonly searchQueries: string[] = [];
  private readonly listeners = new Set<() => void>();
  private state: SpecialProductsState;

  public constructor(
    options: Partial<{
      candidates: readonly SpecialProductItem[];
      items: readonly SpecialProductItem[];
      online: boolean;
      permissions: readonly string[];
      statusCode: SpecialProductsState["statusCode"];
    }> = {},
  ) {
    this.state = {
      access: resolveSpecialProductsAccess(
        options.permissions ?? [
          SPECIAL_PRODUCTS_VIEW_PERMISSION,
          SPECIAL_PRODUCTS_MANAGE_PERMISSION,
          SPECIAL_PRODUCTS_ADD_TO_CART_PERMISSION,
        ],
      ),
      busy: false,
      candidates: options.candidates ?? [],
      items: options.items ?? [],
      kind: "ready",
      online: options.online ?? false,
      searching: false,
      searchQuery: "",
      statusCode: options.statusCode ?? null,
    };
  }

  public readonly getState = () => this.state;

  public readonly subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public async load(): Promise<void> {}

  public setSearchQuery(searchQuery: string): void {
    this.searchQueries.push(searchQuery);
    this.patch({ searchQuery });
  }

  public async searchCandidates(): Promise<void> {
    this.searchCalls += 1;
  }

  public async addToCart(productCode: string): Promise<void> {
    this.addToCartCalls.push(productCode);
  }

  public async download(): Promise<void> {
    this.downloadCalls += 1;
  }

  public async mark(
    productCode: string,
    isSpecialProduct: boolean,
  ): Promise<void> {
    this.markCalls.push({ productCode, isSpecialProduct });
  }

  public async reorder(
    productCode: string,
    delta: -1 | 1,
  ): Promise<void> {
    this.reorderCalls.push({ productCode, delta });
  }

  private patch(patch: Partial<SpecialProductsState>): void {
    this.state = { ...this.state, ...patch };
    for (const listener of this.listeners) listener();
  }
}

function product(productCode: string): SpecialProductItem {
  return {
    barcode: `barcode-${productCode}`,
    discountRate: null,
    displayName: `Product ${productCode}`,
    itemNumber: `item-${productCode}`,
    lookupCode: `lookup-${productCode}`,
    priceSource: 0,
    productCode,
    productImage: null,
    quantityFactor: 1,
    referenceCode: null,
    retailPriceCents: 1_250,
    sortOrder: 0,
    storeCode: "S1",
  };
}
