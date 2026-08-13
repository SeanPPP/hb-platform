import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const root = process.cwd()
const componentSource = readFileSync(path.join(root, 'src/components/ShopScanBar.tsx'), 'utf8')
const globalStyles = readFileSync(path.join(root, 'src/styles/global.css'), 'utf8')

assert(
  componentSource.includes('const [scannerVisible, setScannerVisible] = useState(false)'),
  '扫码区域首次进入商城时应默认折叠',
)
assert(
  componentSource.includes('aria-expanded={scannerVisible}') &&
    componentSource.includes('aria-controls="shop-scan-panel"'),
  '扫码区域开关应向辅助技术暴露展开状态和受控面板',
)
assert(
  componentSource.includes('id="shop-scan-panel"') &&
    componentSource.includes("scannerVisible ? ' shop-scan-bar-visible' : ''"),
  '扫码面板只应在用户主动展开后显示',
)
assert(
  globalStyles.includes('.shop-scan-bar {\n  display: none;') &&
    globalStyles.includes('.shop-scan-bar.shop-scan-bar-visible {\n  display: block;'),
  '扫码面板默认隐藏规则应覆盖桌面和移动视口',
)
assert(
  !/@media \(max-width: 1023px\)[\s\S]{0,180}\.shop-scan-toggle-btn\s*\{[\s\S]{0,80}display:\s*none/.test(globalStyles),
  '移动视口必须保留可操作的扫码区域展开入口',
)

console.log('shopScanBarVisibility.test.ts: ok')
