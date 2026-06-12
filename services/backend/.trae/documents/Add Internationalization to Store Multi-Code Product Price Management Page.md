## Implementation Plan

### 1. Add i18n Keys to Locale Files

**Update `src/locales/zh-CN.ts`:**
Add a new section `storeMultiCodePrice` with keys for:
- Page title and count display
- Table column headers (product image, item number, store name, product name, multi barcode, auto pricing, special product, discount rate, active status, multi-code retail price, purchase price, updated by, updated time)
- Filter labels (store, item number, multi barcode, keyword)
- Buttons (query, reset, batch modify, save changes)
- Batch operation options (retail price, purchase price, discount rate, auto pricing, active status)
- Tags (modified, selected)
- Messages (load failed, no changes, save success/failed, partial save failed, discount rate range error, invalid number, select column/rows warnings)
- Other UI text (set checked, batch modify confirmation, select batch column placeholder)

**Update `src/locales/en-US.ts`:**
Add the corresponding English translations for all the same keys

### 2. Update the Page Component

**Modify `src/pages/PosAdmin/StoreMultiCodePrices/index.tsx`:**
- Import `useIntl` from `@umijs/max`
- Initialize `intl` hook at the component start
- Replace all hardcoded Chinese text with `intl.formatMessage({ id: 'key' })` calls:
  - Page title in Card
  - All table column titles
  - Form labels and placeholders
  - Button texts
  - Tag labels
  - Select options
  - Popconfirm title and messages
  - All success/error messages
  - Validation messages
  - Pagination text

### Expected Outcome
- All UI text in the Store Multi-Code Product Price Management page will be internationalized
- Users can switch between Chinese and English and see all text properly translated
- Consistent with the existing i18n pattern used in other pages like StoreRetailPrices