import assert from 'node:assert/strict'
import test from 'node:test'

import { auditBackendTestInventory, parseCompileIncludes } from './backend-test-inventory.mjs'

test('解析 csproj 的显式 Compile 清单并统一路径分隔符', () => {
  assert.deepEqual(
    parseCompileIncludes(`
      <Compile Include="A.Tests.cs" />
      <Compile Include="nested\\BTests.cs" />
    `),
    new Set(['A.Tests.cs', 'nested/BTests.cs']),
  )
})

test('所有 tracked C# 文件必须编译或以理由显式隔离', () => {
  const result = auditBackendTestInventory({
    projectXml: '<Compile Include="IncludedTests.cs" />',
    trackedFiles: ['IncludedTests.cs', 'ForgottenTests.cs', 'Helper.cs'],
    contentsByFile: new Map([
      ['IncludedTests.cs', '[Fact]\npublic void Included() {}'],
      ['ForgottenTests.cs', '[Theory]\npublic void Forgotten() {}'],
      ['Helper.cs', 'public sealed class Helper {}'],
    ]),
    allowedExclusions: new Map([
      ['Helper.cs', '仅提供测试辅助类型，不应独立编译'],
    ]),
  })

  assert.equal(result.candidateCount, 3)
  assert.equal(result.compiledCount, 1)
  assert.deepEqual(result.errors, ['测试文件未进入 csproj 编译清单: ForgottenTests.cs'])
  assert.deepEqual(result.excluded, ['Helper.cs'])
})

test('自定义 Fact 测试未进入编译清单时失败关闭', () => {
  const result = auditBackendTestInventory({
    projectXml: '',
    trackedFiles: ['CustomSqlTests.cs'],
    contentsByFile: new Map([
      ['CustomSqlTests.cs', '[CustomSqlFact]\npublic void RunsAgainstSqlServer() {}'],
    ]),
    allowedExclusions: new Map(),
  })

  assert.deepEqual(result.errors, ['测试文件未进入 csproj 编译清单: CustomSqlTests.cs'])
})

test('纯 helper 可凭理由隔离，空理由和过期隔离项会失败', () => {
  const allowedExclusions = new Map([
    ['TestHelper.cs', '仅提供测试辅助类型，不应独立编译'],
  ])
  const accepted = auditBackendTestInventory({
    projectXml: '',
    trackedFiles: ['TestHelper.cs'],
    contentsByFile: new Map([
      ['TestHelper.cs', 'public sealed class TestHelper {}'],
    ]),
    allowedExclusions,
  })
  assert.equal(accepted.candidateCount, 1)
  assert.equal(accepted.compiledCount, 0)
  assert.deepEqual(accepted.errors, [])
  assert.deepEqual(accepted.excluded, ['TestHelper.cs'])

  const missingReason = auditBackendTestInventory({
    projectXml: '',
    trackedFiles: ['TestHelper.cs'],
    contentsByFile: new Map([
      ['TestHelper.cs', 'public sealed class TestHelper {}'],
    ]),
    allowedExclusions: new Map([['TestHelper.cs', '   ']]),
  })
  assert.deepEqual(missingReason.errors, ['隔离文件缺少原因: TestHelper.cs'])

  const stale = auditBackendTestInventory({
    projectXml: '',
    trackedFiles: [],
    contentsByFile: new Map(),
    allowedExclusions,
  })
  assert.deepEqual(stale.errors, ['隔离文件已不存在: TestHelper.cs'])
})
