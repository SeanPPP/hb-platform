import type { TFunction } from "i18next";

import type { ServerConnectionPanelCopy } from "./server-connection-panel";

export function serverConnectionPanelCopy(
  t: TFunction,
): ServerConnectionPanelCopy {
  return {
    title: t("serverConnection.title"),
    eyebrow: t("serverConnection.eyebrow"),
    currentAddress: t("serverConnection.currentAddress"),
    edit: t("serverConnection.edit"),
    addressLabel: t("serverConnection.addressLabel"),
    addressPlaceholder: t("serverConnection.addressPlaceholder"),
    test: t("serverConnection.test"),
    testing: t("serverConnection.testing"),
    save: t("serverConnection.save"),
    cancel: t("serverConnection.cancel"),
    confirm: t("serverConnection.confirm"),
    confirmationTitle: t("serverConnection.confirmationTitle"),
    confirmationHint: t("serverConnection.confirmationHint"),
    emptyAddress: t("serverConnection.emptyAddress"),
    testPassed: t("serverConnection.testPassed"),
    testFailed: t("serverConnection.testFailed"),
    saveBlocked: t("serverConnection.saveBlocked"),
    saving: t("serverConnection.saving"),
    saved: t("serverConnection.saved"),
    saveFailed: t("serverConnection.saveFailed"),
  };
}
