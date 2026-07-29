import { describe, expect, it, jest } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";
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


describe("SpecialProductsScreen", () => {
  it("离线显示本地列表并允许加购，但所有管理写操作禁用且触控至少 44pt", async () => {
    const presenter = new ScreenPresenter({
      items: [product("A"), product("B")],
      online: false,
    });
    const onBack = jest.fn();
    const screen = await render(
      <SpecialProductsScreen onBack={onBack} presenter={presenter} />,
    );

    expect(screen.getByTestId("special-product-row-A")).toBeTruthy();
    expect(screen.getByTestId("special-products-offline-note")).toBeTruthy();
    const add = screen.getByTestId("special-products-add-A");
    const download = screen.getByTestId("special-products-download");
    const remove = screen.getByTestId("special-products-remove-A");
    const moveDown = screen.getByTestId("special-products-move-down-A");
    for (const control of [add, download, remove, moveDown]) {
      expect(StyleSheet.flatten(control.props.style).minHeight).toBeGreaterThanOrEqual(
        SPECIAL_PRODUCTS_MIN_TOUCH_TARGET,
      );
    }
    expect(add.props.accessibilityState).toEqual({ disabled: false });
    expect(download.props.accessibilityState).toEqual({ disabled: true });
    expect(remove.props.accessibilityState).toEqual({ disabled: true });
    expect(moveDown.props.accessibilityState).toEqual({ disabled: true });

    await fireEvent.press(add);
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

  it("仅 View 权限可浏览，但不显示加购或管理面板", async () => {
    const presenter = new ScreenPresenter({
      items: [product("A")],
      permissions: [SPECIAL_PRODUCTS_VIEW_PERMISSION],
    });
    const screen = await render(
      <SpecialProductsScreen presenter={presenter} />,
    );

    expect(screen.getByTestId("special-product-row-A")).toBeTruthy();
    expect(screen.queryByTestId("special-products-add-A")).toBeNull();
    expect(screen.queryByTestId("special-products-management")).toBeNull();
    expect(screen.queryByTestId("special-products-remove-A")).toBeNull();
    expect(
      StyleSheet.flatten(
        screen.getByTestId("special-products-refresh-local").props.style,
      ).minHeight,
    ).toBeGreaterThanOrEqual(SPECIAL_PRODUCTS_MIN_TOUCH_TARGET);
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
