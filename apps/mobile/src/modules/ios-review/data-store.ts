export const IOS_REVIEW_DOMAIN_NAMES = [
  "products",
  "carts",
  "orders",
  "warehouse",
  "domesticPurchase",
  "localInvoices",
  "advertisements",
  "promotions",
  "installmentOrders",
  "vouchers",
  "seasonalCards",
  "attendance",
  "users",
  "devices",
  "reports",
  "settings",
] as const;

export type IosReviewDomainName = (typeof IOS_REVIEW_DOMAIN_NAMES)[number];

export interface IosReviewEntity {
  id: string;
  code: string;
  label: string;
  status: string;
  createdAt: string;
  updatedAt: string;
  [key: string]: unknown;
}

export interface ReviewDataStore {
  getNow(): Date;
  list(domain: IosReviewDomainName): IosReviewEntity[];
  get(domain: IosReviewDomainName, id: string): IosReviewEntity | undefined;
  create(
    domain: IosReviewDomainName,
    values: Record<string, unknown>
  ): IosReviewEntity;
  update(
    domain: IosReviewDomainName,
    id: string,
    values: Record<string, unknown>
  ): IosReviewEntity;
  remove(domain: IosReviewDomainName, id: string): boolean;
  reset(): void;
  subscribe(listener: () => void): () => void;
}

function cloneEntity(entity: IosReviewEntity): IosReviewEntity {
  return { ...entity };
}

function createInitialData(now: Date) {
  const timestamp = now.toISOString();
  return new Map<IosReviewDomainName, IosReviewEntity[]>(
    IOS_REVIEW_DOMAIN_NAMES.map((domain, domainIndex) => [
      domain,
      [
        {
          id: `${domain}-001`,
          code: `REV-${String(domainIndex + 1).padStart(2, "0")}-001`,
          label: `Demo ${domain}`,
          status: domain === "orders" ? "completed" : "active",
          storeCode: domain === "warehouse" ? "REVWH" : "REV001",
          amount: Number((25 + domainIndex * 7.5).toFixed(2)),
          quantity: domainIndex + 1,
          createdAt: timestamp,
          updatedAt: timestamp,
        },
      ],
    ])
  );
}

export function createIosReviewDataStore(now = new Date()): ReviewDataStore {
  const initialData = createInitialData(now);
  let data = new Map<IosReviewDomainName, IosReviewEntity[]>();
  let sequence = 1000;
  const listeners = new Set<() => void>();

  const reset = () => {
    data = new Map(
      Array.from(initialData, ([domain, rows]) => [
        domain,
        rows.map(cloneEntity),
      ])
    );
    sequence = 1000;
    listeners.forEach((listener) => listener());
  };

  const requireRows = (domain: IosReviewDomainName) => {
    const rows = data.get(domain);
    if (!rows) throw new Error(`IOS_REVIEW_UNKNOWN_DOMAIN: ${domain}`);
    return rows;
  };

  reset();

  return {
    // 路由 fixture 与数据仓库必须共享同一时钟，保证审核模式日期筛选可重复。
    getNow: () => new Date(now.getTime()),
    list: (domain) => requireRows(domain).map(cloneEntity),
    get: (domain, id) => {
      const entity = requireRows(domain).find((item) => item.id === id);
      return entity ? cloneEntity(entity) : undefined;
    },
    create: (domain, values) => {
      sequence += 1;
      const timestamp = now.toISOString();
      const entity: IosReviewEntity = {
        ...values,
        id: `${domain}-${sequence}`,
        code: String(values.code ?? `REV-${sequence}`),
        label: String(values.label ?? `Demo ${domain}`),
        status: String(values.status ?? "draft"),
        createdAt: timestamp,
        updatedAt: timestamp,
      };
      requireRows(domain).push(entity);
      listeners.forEach((listener) => listener());
      return cloneEntity(entity);
    },
    update: (domain, id, values) => {
      const rows = requireRows(domain);
      const index = rows.findIndex((item) => item.id === id);
      if (index < 0) {
        throw new Error(`IOS_REVIEW_ENTITY_NOT_FOUND: ${domain}/${id}`);
      }
      rows[index] = {
        ...rows[index],
        ...values,
        id,
        updatedAt: now.toISOString(),
      };
      listeners.forEach((listener) => listener());
      return cloneEntity(rows[index]);
    },
    remove: (domain, id) => {
      const rows = requireRows(domain);
      const index = rows.findIndex((item) => item.id === id);
      if (index < 0) return false;
      rows.splice(index, 1);
      listeners.forEach((listener) => listener());
      return true;
    },
    reset,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
}

export const iosReviewDataStore = createIosReviewDataStore();

export function resetReviewData() {
  iosReviewDataStore.reset();
}
