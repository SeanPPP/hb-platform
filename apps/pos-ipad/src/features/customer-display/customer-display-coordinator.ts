import type {
  CustomerDisplayFrame,
  CustomerDisplayPublishResult,
} from "./customer-display-publisher";

import type {
  CartSnapshot,
  CustomerDisplaySnapshot,
} from "@/core/contracts";

export interface CustomerDisplayCartPort {
  getSnapshot(): CartSnapshot;
  subscribe(listener: () => void): () => void;
}

export interface CustomerDisplayPublisherPort {
  publish(frame: CustomerDisplayFrame): Promise<CustomerDisplayPublishResult>;
}

/**
 * 客显状态仅跟随共享购物车和显式的支付阶段，不持有支付引用或顾客资料。
 * 支付完成清车后仍保留最后一个非空快照，直到主屏明确开始下一笔交易。
 */
export class CustomerDisplayCoordinator {
  private advert: CustomerDisplaySnapshot["advert"] = null;
  private changeCents = 0;
  private destroyed = false;
  private initialized = false;
  private lastNonEmptyCart: CartSnapshot | null = null;
  private mode: CustomerDisplaySnapshot["mode"] = "idle";
  private unsubscribeCart: (() => void) | null = null;

  public constructor(
    private readonly cart: CustomerDisplayCartPort,
    private readonly publisher: CustomerDisplayPublisherPort,
  ) {}

  public async initialize(): Promise<void> {
    this.assertAlive();
    if (this.initialized) return;
    this.initialized = true;
    this.unsubscribeCart = this.cart.subscribe(() => {
      this.onCartChanged();
    });
    const current = this.readCart();
    this.mode = current.lines.length > 0 ? "cart" : "idle";
    await this.publish(current);
  }

  public showCart(): Promise<CustomerDisplayPublishResult> {
    this.assertReady();
    const current = this.readCart();
    this.changeCents = 0;
    this.mode = current.lines.length > 0 ? "cart" : "idle";
    return this.publish(current);
  }

  public showPayment(): Promise<CustomerDisplayPublishResult> {
    this.assertReady();
    this.changeCents = 0;
    this.mode = "payment";
    return this.publish(this.transactionCart());
  }

  public showChange(changeCents: number): Promise<CustomerDisplayPublishResult> {
    this.assertReady();
    this.changeCents = changeCents;
    this.mode = "change";
    return this.publish(this.transactionCart());
  }

  public showSuccess(
    changeCents: number,
  ): Promise<CustomerDisplayPublishResult> {
    this.assertReady();
    this.changeCents = changeCents;
    this.mode = "success";
    return this.publish(this.transactionCart());
  }

  public setAdvert(
    advert: CustomerDisplaySnapshot["advert"],
  ): Promise<CustomerDisplayPublishResult> {
    this.assertReady();
    this.advert = advert;
    return this.publish(
      this.mode === "idle" || this.mode === "cart"
        ? this.readCart()
        : this.transactionCart(),
    );
  }

  /**
   * 锁屏、设备拒绝与 runtime 退出都必须主动覆盖公共外屏；
   * 不能依赖下一笔购物车变更来清除上一位顾客的交易。
   */
  public clearSensitiveContent(): Promise<CustomerDisplayPublishResult> {
    this.assertReady();
    this.lastNonEmptyCart = null;
    this.changeCents = 0;
    this.advert = null;
    this.mode = "idle";
    return this.publish(null);
  }

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.unsubscribeCart?.();
    this.unsubscribeCart = null;
  }

  private onCartChanged(): void {
    if (this.destroyed) return;
    const current = this.readCart();
    if (this.mode !== "idle" && this.mode !== "cart") {
      return;
    }
    this.mode = current.lines.length > 0 ? "cart" : "idle";
    void this.publish(current).catch(() => {
      // 客显验证或桥接异常不能传播到共享购物车的主交易通知。
    });
  }

  private readCart(): CartSnapshot {
    const current = this.cart.getSnapshot();
    if (current.lines.length > 0) {
      this.lastNonEmptyCart = current;
    }
    return current;
  }

  private transactionCart(): CartSnapshot | null {
    const current = this.readCart();
    return current.lines.length > 0 ? current : this.lastNonEmptyCart;
  }

  private publish(
    cart: CartSnapshot | null,
  ): Promise<CustomerDisplayPublishResult> {
    return this.publisher.publish({
      mode: this.mode,
      cart,
      changeCents: this.changeCents,
      advert: this.advert,
    });
  }

  private assertAlive(): void {
    if (this.destroyed) {
      throw new Error("Customer display coordinator is destroyed.");
    }
  }

  private assertReady(): void {
    this.assertAlive();
    if (!this.initialized) {
      throw new Error("Customer display coordinator is not initialized.");
    }
  }
}
