# Fix Compilation Error Plan

The user reported a `ReferenceError: DownloadOutlined is not defined` in `InvoicePage`. This is because I used the `DownloadOutlined` icon in the component code but forgot to import it from `@ant-design/icons`.

## Steps
1.  **Modify File**: `d:\Development\cline\blazor\reactumi\my-app\src\pages\StoreOrder\Invoice\index.tsx`
2.  **Action**: Add `DownloadOutlined` to the import statement from `@ant-design/icons`.

## Verification
*   The compilation error should be resolved.
