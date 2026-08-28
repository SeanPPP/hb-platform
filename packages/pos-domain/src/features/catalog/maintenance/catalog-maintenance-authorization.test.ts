import assert from "node:assert/strict";
import test from "node:test";

import {
  CATALOG_DOWNLOAD_PERMISSION,
  canDownloadCatalog,
} from "./catalog-maintenance-authorization";

test("目录下载只接受 WPF 的精确权限码", () => {
  assert.equal(canDownloadCatalog([]), false);
  assert.equal(
    canDownloadCatalog(["Permissions.PosTerminal.Settings"]),
    false,
  );
  assert.equal(
    canDownloadCatalog([
      "Permissions.PosTerminal.Settings.CatalogReset",
    ]),
    false,
  );
  assert.equal(canDownloadCatalog([CATALOG_DOWNLOAD_PERMISSION]), true);
  assert.equal(
    canDownloadCatalog([`  ${CATALOG_DOWNLOAD_PERMISSION}  `]),
    true,
  );
});
