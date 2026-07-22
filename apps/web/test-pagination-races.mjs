import { spawnSync } from 'node:child_process'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { build } from 'esbuild'

const tests = [
  'src/utils/latestRequestGuard.test.ts',
  'src/pages/System/listPagination.test.ts',
  'src/pages/System/requestRace.test.ts',
  'src/pages/DomesticPurchase/ChinaSuppliers/paginationRace.test.ts',
  'src/pages/DomesticPurchase/ProductPrefixCodeManagement/paginationRace.test.ts',
  'src/pages/Warehouse/Products/remotePaginationRace.logic.test.ts',
  'src/pages/PosAdmin/Advertisements/remotePaginationRace.test.ts',
  'src/pages/PosAdmin/CashRegisterUsers/remotePaginationRace.test.ts',
  'src/pages/PosAdmin/PricingStrategies/requestRace.test.ts',
  'src/pages/PosAdmin/Promotions/requestRace.test.ts',
  'src/pages/PosAdmin/SupplierManagement/requestRace.test.ts',
  'src/pages/PosAdmin/LocalSupplierInvoices/requestRace.logic.test.ts',
  'src/pages/Warehouse/Containers/requestRace.logic.test.ts',
]

mkdirSync('tmp', { recursive: true })

for (const [index, test] of tests.entries()) {
  const outfile = resolve('tmp', `hbweb_pagination_race_${index}.test.mjs`)
  await build({
    entryPoints: [test],
    bundle: true,
    platform: 'node',
    format: 'esm',
    outfile,
  })

  const result = spawnSync(process.execPath, [outfile], {
    cwd: process.cwd(),
    stdio: 'inherit',
  })
  if (result.status !== 0) {
    process.exit(result.status ?? 1)
  }
}

console.log(`test:pagination-races: ${tests.length} files passed`)
