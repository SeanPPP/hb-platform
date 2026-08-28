import type {
  VoucherPaymentContextProvider,
  VoucherProtectedTokenPort,
} from "../../features/payments/voucher/voucher-payment-adapter";
import type {
  InstallmentProviderAttemptStorePort,
  InstallmentVoucherIntentVaultPort,
  InstallmentVoucherMaterialPort,
} from "../runtime/production-installment-payment-adapter";
import type { InstallmentRefundProvenanceVaultPort } from "../runtime/production-installment-refund-provenance";

import { SqliteInstallmentProviderAttemptStore } from "./sqlite-installment-provider-attempt-store";
import { SqliteInstallmentRefundProvenanceVault } from "./sqlite-installment-refund-provenance-vault";
import {
  SqliteInstallmentVoucherContext,
  SqliteInstallmentVoucherIntentVault,
  SqliteInstallmentVoucherMaterialStore,
  SqliteInstallmentVoucherProtectedTokenStore,
} from "./sqlite-installment-voucher-persistence";
import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

/**
 * 第二套分期 provider bootstrap 只取得这六个窄成员；不会取得裸连接或通用
 * payment_attempts/voucher token 仓储。
 */
export class SqliteInstallmentPaymentPersistenceFacade {
  public readonly providerAttempts: InstallmentProviderAttemptStorePort;
  public readonly voucherIntents: InstallmentVoucherIntentVaultPort;
  public readonly voucherProtectedTokens: VoucherProtectedTokenPort;
  public readonly voucherContextForAttempt: VoucherPaymentContextProvider;
  public readonly voucherMaterials: InstallmentVoucherMaterialPort;
  public readonly refundProvenance: InstallmentRefundProvenanceVaultPort;

  public constructor(
    connection: SqliteConnectionPort,
    encryptor: SensitivePayloadEncryptor,
    createProtectedReference: () => string,
    nowIso: () => string,
  ) {
    const providerAttempts = new SqliteInstallmentProviderAttemptStore(
      connection,
      encryptor,
      nowIso,
    );
    const voucherIntents = new SqliteInstallmentVoucherIntentVault(
      connection,
      encryptor,
      nowIso,
    );
    const voucherProtectedTokens =
      new SqliteInstallmentVoucherProtectedTokenStore(
        connection,
        encryptor,
        createProtectedReference,
        nowIso,
      );
    const voucherContext = new SqliteInstallmentVoucherContext(
      providerAttempts,
      voucherIntents,
    );
    const voucherMaterials =
      new SqliteInstallmentVoucherMaterialStore(
        providerAttempts,
        voucherIntents,
        voucherProtectedTokens,
      );

    this.providerAttempts = providerAttempts;
    this.voucherIntents = voucherIntents;
    this.voucherProtectedTokens = voucherProtectedTokens;
    this.voucherContextForAttempt = voucherContext.provide;
    this.voucherMaterials = voucherMaterials;
    this.refundProvenance =
      new SqliteInstallmentRefundProvenanceVault(
        connection,
        encryptor,
        nowIso,
      );
  }
}
