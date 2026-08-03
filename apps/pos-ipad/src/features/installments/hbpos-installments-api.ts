import type {
  InstallmentAppendPaymentCommand,
  InstallmentCancelCommand,
  InstallmentCreateCommand,
  InstallmentDetails,
  InstallmentHistoryQuery,
  InstallmentLine,
  InstallmentPayment,
  InstallmentPaymentCommand,
  InstallmentPaymentMethod,
  InstallmentPickupCommand,
  InstallmentRefundCommand,
  InstallmentsRemotePort,
  InstallmentVoidCommand,
} from "./installment-models";

import {
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "@/core/api/hbpos-api";
import type { InstallmentStatus, InstallmentSummary } from "@/core/contracts";
import type { components } from "@/generated/hbpos/schema";

type GeneratedAppendRequest =
  components["schemas"]["InstallmentAppendPaymentRequest"];
type GeneratedAppendResponse =
  components["schemas"]["InstallmentAppendPaymentResponse"];
type GeneratedCancelRequest =
  components["schemas"]["InstallmentCancelRequest"];
type GeneratedCancelResponse =
  components["schemas"]["InstallmentCancelResponse"];
type GeneratedCreateRequest =
  components["schemas"]["InstallmentCreateRequest"];
type GeneratedCreateResponse =
  components["schemas"]["InstallmentCreateResponse"];
type GeneratedDetails =
  components["schemas"]["InstallmentDetailsDto"];
type GeneratedHistoryResponse =
  components["schemas"]["InstallmentHistoryQueryResponse"];
type GeneratedLine = components["schemas"]["InstallmentLineDto"];
type GeneratedPayment =
  components["schemas"]["InstallmentPaymentDto"];
type GeneratedPaymentCommand =
  components["schemas"]["InstallmentPaymentCommandDto"];
type GeneratedPickupRequest =
  components["schemas"]["InstallmentConfirmPickupRequest"];
type GeneratedPickupResponse =
  components["schemas"]["InstallmentConfirmPickupResponse"];
type GeneratedRefundCommand =
  components["schemas"]["InstallmentRefundPaymentCommandDto"];
type GeneratedSummary =
  components["schemas"]["InstallmentSummaryDto"];
type GeneratedVoidRequest =
  components["schemas"]["InstallmentVoidRequest"];
type GeneratedVoidResponse =
  components["schemas"]["InstallmentVoidResponse"];

const STATUS_TO_API = Object.freeze({
  Active: 1,
  PaidOff: 2,
  PickedUp: 3,
  Cancelled: 4,
} satisfies Readonly<Record<InstallmentStatus, 1 | 2 | 3 | 4>>);

const METHOD_TO_API = Object.freeze({
  cash: 1,
  card: 2,
  voucher: 3,
} satisfies Readonly<Record<InstallmentPaymentMethod, 1 | 2 | 3>>);

export class HbposInstallmentsApi implements InstallmentsRemotePort {
  private readonly storeCode: string;

  public constructor(
    private readonly transport: HbposTransport,
    trustedStoreCode: string,
  ) {
    this.storeCode = requestIdentity(trustedStoreCode, "storeCode");
  }

  public async list(
    query: InstallmentHistoryQuery,
  ): Promise<readonly InstallmentSummary[]> {
    const params: Record<string, string | number> = {
      storeCode: this.storeCode,
    };
    const deviceCode = optionalRequestText(
      query.deviceCode,
      "deviceCode",
      128,
    );
    const createdFrom = optionalIso(
      query.createdFromIso,
      "createdFromIso",
    );
    const createdTo = optionalIso(
      query.createdToIso,
      "createdToIso",
    );
    const keyword = optionalRequestText(query.keyword, "keyword", 120);
    if (deviceCode) params.deviceCode = deviceCode;
    if (createdFrom) params.createdFrom = createdFrom;
    if (createdTo) params.createdTo = createdTo;
    if (keyword) params.keyword = keyword;
    if (query.status) params.status = STATUS_TO_API[query.status];
    params.skip = requestSkip(query.skip);
    params.take = requestTake(query.take);

    const response = await this.transport.request<
      HbposEnvelope<GeneratedHistoryResponse>
    >({
      method: "GET",
      url: "/api/v1/installments/history",
      params,
    });
    const payload = unwrapHbposEnvelope(response.data);
    const orders = requiredArray(payload.orders, "response.orders");
    return Object.freeze(
      orders.map((order) => this.mapSummary(order)),
    );
  }

  public async getDetails(
    installmentGuid: string,
  ): Promise<InstallmentDetails | null> {
    const guid = requestUuid(installmentGuid, "installmentGuid");
    const response = await this.transport.request<
      HbposEnvelope<GeneratedDetails | null>
    >({
      method: "GET",
      url: `/api/v1/installments/${guid}`,
    });
    const payload = unwrapHbposEnvelope(response.data);
    return payload === null ? null : this.mapDetails(payload);
  }

  public async create(
    command: InstallmentCreateCommand,
  ): Promise<InstallmentDetails> {
    const request: GeneratedCreateRequest = {
      installmentGuid: requestUuid(
        command.installmentGuid,
        "installmentGuid",
      ),
      storeCode: this.storeCode,
      ...mapIdentity(command),
      createdAt: requestIso(command.createdAtIso, "createdAtIso"),
      totalAmount: centsToDollars(command.totalCents, "totalCents", false),
      downPaymentAmount: centsToDollars(
        command.downPaymentCents,
        "downPaymentCents",
        false,
      ),
      lines: command.lines.map(mapLineCommand),
      downPayment: mapPaymentCommand(command.downPayment),
      customerName: requestText(command.customerName, "customerName", 256),
      customerPhone: requestText(
        command.customerPhone,
        "customerPhone",
        128,
      ),
      note: optionalRequestText(command.note, "note", 2_000),
    };
    const response = await this.transport.request<
      HbposEnvelope<GeneratedCreateResponse>
    >({
      method: "POST",
      url: "/api/v1/installments",
      data: request,
    });
    return this.detailsFromWriteResponse(
      unwrapHbposEnvelope(response.data).details,
    );
  }

  public async appendPayment(
    command: InstallmentAppendPaymentCommand,
  ): Promise<InstallmentDetails> {
    const installmentGuid = requestUuid(
      command.installmentGuid,
      "installmentGuid",
    );
    const payment = mapPaymentCommand(command.payment);
    const request: GeneratedAppendRequest = {
      installmentGuid,
      paymentGuid: payment.paymentGuid,
      storeCode: this.storeCode,
      ...mapIdentity(command),
      amount: payment.amount,
      method: payment.method,
      reference: payment.reference,
      reservationToken: payment.reservationToken,
      cardTransactions: payment.cardTransactions,
      idempotencyKey: payment.idempotencyKey,
    };
    const response = await this.transport.request<
      HbposEnvelope<GeneratedAppendResponse>
    >({
      method: "POST",
      url: `/api/v1/installments/${installmentGuid}/payments`,
      data: request,
    });
    return this.detailsFromWriteResponse(
      unwrapHbposEnvelope(response.data).details,
    );
  }

  public async cancelWithRefund(
    command: InstallmentCancelCommand,
  ): Promise<InstallmentDetails> {
    const installmentGuid = requestUuid(
      command.installmentGuid,
      "installmentGuid",
    );
    const request: GeneratedCancelRequest = {
      installmentGuid,
      storeCode: this.storeCode,
      ...mapIdentity(command),
      cancelledAt: requestIso(
        command.cancelledAtIso,
        "cancelledAtIso",
      ),
      refunds: command.refunds.map(mapRefundCommand),
      reason: optionalRequestText(command.reason, "reason", 1_000),
      idempotencyKey: requestText(
        command.idempotencyKey,
        "idempotencyKey",
        256,
      ),
    };
    const response = await this.transport.request<
      HbposEnvelope<GeneratedCancelResponse>
    >({
      method: "POST",
      url: `/api/v1/installments/${installmentGuid}/cancel`,
      data: request,
    });
    return this.detailsFromWriteResponse(
      unwrapHbposEnvelope(response.data).details,
    );
  }

  public async void(
    command: InstallmentVoidCommand,
  ): Promise<InstallmentDetails> {
    const installmentGuid = requestUuid(
      command.installmentGuid,
      "installmentGuid",
    );
    const request: GeneratedVoidRequest = {
      installmentGuid,
      storeCode: this.storeCode,
      ...mapIdentity(command),
      voidedAt: requestIso(command.voidedAtIso, "voidedAtIso"),
      reason: requestText(command.reason, "reason", 1_000),
      idempotencyKey: requestText(
        command.idempotencyKey,
        "idempotencyKey",
        256,
      ),
    };
    const response = await this.transport.request<
      HbposEnvelope<GeneratedVoidResponse>
    >({
      method: "POST",
      url: `/api/v1/installments/${installmentGuid}/void`,
      data: request,
    });
    return this.detailsFromWriteResponse(
      unwrapHbposEnvelope(response.data).details,
    );
  }

  public async confirmPickup(
    command: InstallmentPickupCommand,
  ): Promise<InstallmentDetails> {
    const installmentGuid = requestUuid(
      command.installmentGuid,
      "installmentGuid",
    );
    const request: GeneratedPickupRequest = {
      installmentGuid,
      storeCode: this.storeCode,
      ...mapIdentity(command),
      confirmedAt: requestIso(
        command.confirmedAtIso,
        "confirmedAtIso",
      ),
      note: optionalRequestText(command.note, "note", 1_000),
    };
    const response = await this.transport.request<
      HbposEnvelope<GeneratedPickupResponse>
    >({
      method: "POST",
      url: `/api/v1/installments/${installmentGuid}/pickup`,
      data: request,
    });
    return this.detailsFromWriteResponse(
      unwrapHbposEnvelope(response.data).details,
    );
  }

  private detailsFromWriteResponse(
    details: GeneratedDetails | null | undefined,
  ): InstallmentDetails {
    if (!details) throw invalidResponse("details");
    return this.mapDetails(details);
  }

  private mapSummary(
    summary: GeneratedSummary,
    updatedAt: unknown = summary.updatedAt,
  ): InstallmentSummary {
    const storeCode = responseIdentity(summary.storeCode, "summary.storeCode");
    if (storeCode !== this.storeCode) {
      throw invalidResponse("summary.storeCode");
    }
    return Object.freeze({
      installmentGuid: responseUuid(
        summary.installmentGuid,
        "summary.installmentGuid",
      ),
      installmentNumber: responseText(
        summary.installmentNumber,
        "summary.installmentNumber",
        128,
      ),
      storeCode,
      deviceCode: responseIdentity(
        summary.deviceCode,
        "summary.deviceCode",
      ),
      cashierName: responseText(
        summary.cashierName,
        "summary.cashierName",
        256,
      ),
      customerName: responseText(
        summary.customerName,
        "summary.customerName",
        256,
      ),
      customerPhone: responseOptionalText(
        summary.customerPhone,
        "summary.customerPhone",
        128,
      ),
      createdAtIso: responseIso(
        summary.createdAt,
        "summary.createdAt",
      ),
      totalCents: responseMoneyCents(
        summary.totalAmount,
        "summary.totalAmount",
      ),
      downPaymentCents: responseMoneyCents(
        summary.downPaymentAmount,
        "summary.downPaymentAmount",
      ),
      paidCents: responseMoneyCents(
        summary.paidAmount,
        "summary.paidAmount",
      ),
      balanceCents: responseMoneyCents(
        summary.balanceAmount,
        "summary.balanceAmount",
      ),
      status: responseStatus(summary.status, "summary.status"),
      updatedAtIso: responseIso(
        updatedAt,
        "summary.updatedAt",
      ),
    });
  }

  private mapDetails(details: GeneratedDetails): InstallmentDetails {
    // 详情 DTO 没有 updatedAt；沿用服务端 createdAt，避免伪造本机当前时间。
    const summary = this.mapSummary(details, details.createdAt);
    return Object.freeze({
      ...summary,
      cashierId: responseIdentity(
        details.cashierId,
        "details.cashierId",
      ),
      minimumDownPaymentCents: responseMoneyCents(
        details.minimumDownPayment,
        "details.minimumDownPayment",
      ),
      lines: Object.freeze(
        requiredArray(details.lines, "details.lines").map(mapLine),
      ),
      payments: Object.freeze(
        requiredArray(details.payments, "details.payments").map(mapPayment),
      ),
      pickupInfo: mapPickupInfo(details.pickupInfo),
      cancellationInfo: mapCancellationInfo(details.cancellationInfo),
      note: responseOptionalText(details.note, "details.note", 2_000),
    });
  }
}

function mapIdentity(input: Readonly<{
  deviceCode: string;
  cashierId: string;
  cashierName: string;
}>) {
  return {
    deviceCode: requestIdentity(input.deviceCode, "deviceCode"),
    cashierId: requestIdentity(input.cashierId, "cashierId"),
    cashierName: requestText(input.cashierName, "cashierName", 256),
  };
}

function mapLineCommand(line: InstallmentLine): GeneratedLine {
  return {
    installmentLineGuid: requestUuid(
      line.installmentLineGuid,
      "installmentLineGuid",
    ),
    productCode: requestText(line.productCode, "productCode", 128),
    referenceCode: optionalRequestText(
      line.referenceCode,
      "referenceCode",
      128,
    ),
    displayName: requestText(line.displayName, "displayName", 512),
    lookupCode: requestText(line.lookupCode, "lookupCode", 128),
    quantity: requestQuantity(line.quantity),
    unitPrice: centsToDollars(line.unitPriceCents, "unitPriceCents", true),
    discountAmount: centsToDollars(
      line.discountCents,
      "discountCents",
      true,
    ),
    actualAmount: centsToDollars(
      line.actualAmountCents,
      "actualAmountCents",
      true,
    ),
    itemNumber: optionalRequestText(line.itemNumber, "itemNumber", 128),
  };
}

function mapPaymentCommand(
  payment: InstallmentPaymentCommand,
): Required<GeneratedPaymentCommand> {
  return {
    paymentGuid: requestUuid(payment.paymentGuid, "paymentGuid"),
    method: METHOD_TO_API[payment.method],
    amount: centsToDollars(payment.amountCents, "amountCents", false),
    reference: optionalProtectedText(payment.reference, "reference"),
    reservationToken: optionalProtectedText(
      payment.reservationToken,
      "reservationToken",
    ),
    cardTransactions: [...payment.cardTransactions],
    idempotencyKey: requestText(
      payment.idempotencyKey,
      "idempotencyKey",
      256,
    ),
  };
}

function mapRefundCommand(
  refund: InstallmentRefundCommand,
): GeneratedRefundCommand {
  return {
    paymentGuid: requestUuid(refund.paymentGuid, "refund.paymentGuid"),
    method: METHOD_TO_API[refund.method],
    amount: centsToDollars(
      refund.amountCents,
      "refund.amountCents",
      false,
    ),
    reference: optionalProtectedText(
      refund.reference,
      "refund.reference",
    ),
    cardTransactions: [...refund.cardTransactions],
    idempotencyKey: requestText(
      refund.idempotencyKey,
      "refund.idempotencyKey",
      256,
    ),
  };
}

function mapLine(line: GeneratedLine): InstallmentLine {
  return Object.freeze({
    installmentLineGuid: responseUuid(
      line.installmentLineGuid,
      "line.installmentLineGuid",
    ),
    productCode: responseText(line.productCode, "line.productCode", 128),
    referenceCode: responseOptionalText(
      line.referenceCode,
      "line.referenceCode",
      128,
    ),
    displayName: responseText(
      line.displayName,
      "line.displayName",
      512,
    ),
    lookupCode: responseText(line.lookupCode, "line.lookupCode", 128),
    quantity: responseQuantity(line.quantity, "line.quantity"),
    unitPriceCents: responseMoneyCents(
      line.unitPrice,
      "line.unitPrice",
    ),
    discountCents: responseMoneyCents(
      line.discountAmount,
      "line.discountAmount",
    ),
    actualAmountCents: responseMoneyCents(
      line.actualAmount,
      "line.actualAmount",
    ),
    itemNumber: responseOptionalText(
      line.itemNumber,
      "line.itemNumber",
      128,
    ),
  });
}

function mapPayment(payment: GeneratedPayment): InstallmentPayment {
  const safeCard = safeCardDisplay(payment.cardTransactions?.[0]);
  return Object.freeze({
    paymentGuid: responseUuid(
      payment.paymentGuid,
      "payment.paymentGuid",
    ),
    method: responsePaymentMethod(
      payment.method,
      "payment.method",
    ),
    amountCents: responseMoneyCents(
      payment.amount,
      "payment.amount",
      true,
    ),
    status: responsePaymentStatus(
      payment.status,
      "payment.status",
    ),
    recordedAtIso: responseIso(
      payment.recordedAt,
      "payment.recordedAt",
    ),
    cashierId: responseIdentity(
      payment.cashierId,
      "payment.cashierId",
    ),
    deviceCode: responseIdentity(
      payment.deviceCode,
      "payment.deviceCode",
    ),
    cardType: safeCard.cardType,
    maskedCardNumber: safeCard.maskedCardNumber,
  });
}

function mapPickupInfo(
  pickup:
    | components["schemas"]["InstallmentPickupInfoDto"]
    | null
    | undefined,
) {
  if (!pickup) return null;
  return Object.freeze({
    pickedUpAtIso: responseIso(
      pickup.pickedUpAt,
      "pickup.pickedUpAt",
    ),
    pickedUpBy: responseText(
      pickup.pickedUpBy,
      "pickup.pickedUpBy",
      256,
    ),
    note: responseOptionalText(pickup.note, "pickup.note", 1_000),
  });
}

function mapCancellationInfo(
  cancellation:
    | components["schemas"]["InstallmentCancellationInfoDto"]
    | null
    | undefined,
) {
  if (!cancellation) return null;
  return Object.freeze({
    kind:
      cancellation.kind === 1
        ? ("RefundCancel" as const)
        : cancellation.kind === 2
          ? ("VoidCancel" as const)
          : (() => {
              throw invalidResponse("cancellation.kind");
            })(),
    cancelledAtIso: responseIso(
      cancellation.cancelledAt,
      "cancellation.cancelledAt",
    ),
    cancelledBy: responseText(
      cancellation.cancelledBy,
      "cancellation.cancelledBy",
      256,
    ),
    reason: responseOptionalText(
      cancellation.reason,
      "cancellation.reason",
      1_000,
    ),
  });
}

function responseStatus(
  value: unknown,
  field: string,
): InstallmentStatus {
  if (value === 1) return "Active";
  if (value === 2) return "PaidOff";
  if (value === 3) return "PickedUp";
  if (value === 4) return "Cancelled";
  throw invalidResponse(field);
}

function responsePaymentMethod(
  value: unknown,
  field: string,
): InstallmentPaymentMethod {
  if (value === 1) return "cash";
  if (value === 2) return "card";
  if (value === 3) return "voucher";
  throw invalidResponse(field);
}

function responsePaymentStatus(
  value: unknown,
  field: string,
): "Recorded" | "Voided" {
  if (value === 1) return "Recorded";
  if (value === 2) return "Voided";
  throw invalidResponse(field);
}

function safeCardDisplay(value: unknown): Readonly<{
  cardType: string | null;
  maskedCardNumber: string | null;
}> {
  if (!isRecord(value)) {
    return { cardType: null, maskedCardNumber: null };
  }
  const cardType = safeDisplayText(value.cardType, 64);
  const maskedCandidate = safeDisplayText(value.maskedCardNumber, 64);
  const maskedCardNumber =
    maskedCandidate &&
    /[*xX•]/u.test(maskedCandidate) &&
    !/\d{12,19}/u.test(maskedCandidate.replace(/[\s-]/gu, ""))
      ? maskedCandidate
      : null;
  return { cardType, maskedCardNumber };
}

function safeDisplayText(value: unknown, maxLength: number): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maxLength ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    return null;
  }
  return normalized;
}

function requestTake(value: unknown): 20 | 50 | 51 | 100 | 200 {
  if (
    value === 20 ||
    value === 50 ||
    value === 51 ||
    value === 100 ||
    value === 200
  ) {
    return value;
  }
  throw invalidRequest("take");
}

function requestSkip(value: unknown): number {
  if (Number.isSafeInteger(value) && Number(value) >= 0) {
    return Number(value);
  }
  throw invalidRequest("skip");
}

function requestQuantity(value: unknown): number {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw invalidRequest("quantity");
  }
  const quantity = Number(value);
  if (!Number.isFinite(quantity) || quantity <= 0) {
    throw invalidRequest("quantity");
  }
  return quantity;
}

function centsToDollars(
  value: unknown,
  field: string,
  allowZero: boolean,
): number {
  if (
    !Number.isSafeInteger(value) ||
    Number(value) < (allowZero ? 0 : 1)
  ) {
    throw invalidRequest(field);
  }
  return Number(value) / 100;
}

function responseMoneyCents(
  value: unknown,
  field: string,
  allowNegative = false,
): number {
  if (
    typeof value !== "number" ||
    !Number.isFinite(value) ||
    (!allowNegative && value < 0)
  ) {
    throw invalidResponse(`${field}.money`);
  }
  const scaled = value * 100;
  const cents = Math.round(scaled);
  if (
    !Number.isSafeInteger(cents) ||
    Math.abs(scaled - cents) > 1e-7
  ) {
    throw invalidResponse(`${field}.money`);
  }
  return cents;
}

function responseQuantity(value: unknown, field: string): string {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) {
    throw invalidResponse(field);
  }
  return decimalString(value);
}

function decimalString(value: number): string {
  const text = value.toString();
  if (!/[eE]/u.test(text)) return text;
  const [coefficient, exponentText] = text.toLowerCase().split("e");
  const exponent = Number(exponentText);
  const negative = coefficient?.startsWith("-") ?? false;
  const unsigned = negative ? coefficient!.slice(1) : coefficient!;
  const [whole, fraction = ""] = unsigned.split(".");
  const digits = `${whole}${fraction}`;
  const decimalIndex = whole!.length + exponent;
  const expanded =
    decimalIndex <= 0
      ? `0.${"0".repeat(-decimalIndex)}${digits}`
      : decimalIndex >= digits.length
        ? `${digits}${"0".repeat(decimalIndex - digits.length)}`
        : `${digits.slice(0, decimalIndex)}.${digits.slice(decimalIndex)}`;
  return `${negative ? "-" : ""}${expanded}`;
}

function requestIdentity(value: unknown, field: string): string {
  return requestText(value, field, 128);
}

function requestText(
  value: unknown,
  field: string,
  maxLength: number,
): string {
  if (typeof value !== "string") throw invalidRequest(field);
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maxLength ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw invalidRequest(field);
  }
  return normalized;
}

function optionalRequestText(
  value: unknown,
  field: string,
  maxLength: number,
): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value !== "string") throw invalidRequest(field);
  if (value.trim().length === 0) return null;
  return requestText(value, field, maxLength);
}

function optionalProtectedText(
  value: unknown,
  field: string,
): string | null {
  if (value === null || value === undefined) return null;
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > 4_096 ||
    /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(value)
  ) {
    throw invalidRequest(field);
  }
  return value;
}

function requestIso(value: unknown, field: string): string {
  if (typeof value !== "string") throw invalidRequest(field);
  const parsed = new Date(value);
  if (!Number.isFinite(parsed.getTime())) throw invalidRequest(field);
  return parsed.toISOString();
}

function optionalIso(value: unknown, field: string): string | null {
  if (value === null || value === undefined || value === "") return null;
  return requestIso(value, field);
}

function requestUuid(value: unknown, field: string): string {
  if (
    typeof value !== "string" ||
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(
      value,
    )
  ) {
    throw invalidRequest(field);
  }
  return value.toLowerCase();
}

function responseUuid(value: unknown, field: string): string {
  try {
    return requestUuid(value, field);
  } catch {
    throw invalidResponse(field);
  }
}

function responseIdentity(value: unknown, field: string): string {
  return responseText(value, field, 128);
}

function responseText(
  value: unknown,
  field: string,
  maxLength: number,
): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > maxLength ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw invalidResponse(field);
  }
  return value;
}

function responseOptionalText(
  value: unknown,
  field: string,
  maxLength: number,
): string | null {
  if (value === null || value === undefined || value === "") return null;
  return responseText(value, field, maxLength);
}

function responseIso(value: unknown, field: string): string {
  if (typeof value !== "string") throw invalidResponse(field);
  const parsed = new Date(value);
  if (!Number.isFinite(parsed.getTime())) throw invalidResponse(field);
  return parsed.toISOString();
}

function requiredArray<T>(
  value: readonly T[] | null | undefined,
  field: string,
): readonly T[] {
  if (!Array.isArray(value)) throw invalidResponse(field);
  return value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function invalidRequest(field: string): Error {
  return new Error(`Invalid installment request field: ${field}.`);
}

function invalidResponse(field: string): Error {
  return new Error(`Invalid installment response field: ${field}.`);
}
