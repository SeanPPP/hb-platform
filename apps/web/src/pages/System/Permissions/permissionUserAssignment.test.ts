import assert from 'node:assert/strict'

import {
  buildPermissionUserDelta,
  buildPermissionUserOptions,
  isPermissionUserDeltaEmpty,
  loadAllUserPages,
  matchesPermissionUserKeyword,
} from './permissionUserAssignment'

const alice = { userGUID: 'u-alice', username: 'alice', email: 'alice@example.test', fullName: 'Alice Wang', isActive: true }
const bob = { userGUID: 'u-bob', username: 'bob', email: 'bob@example.test', isActive: false }
const carol = { userGUID: 'u-carol', username: 'carol', email: 'carol@example.test', isActive: true }

// 已授权用户不在已加载列表中时也必须出现在数据源里，否则穿梭框右侧会丢失该用户。
const options = buildPermissionUserOptions([bob, alice], [alice, carol])
assert.deepEqual(options.map((item) => item.key), ['u-alice', 'u-bob', 'u-carol'])
assert.equal(options[0]?.title, 'alice / Alice Wang')
assert.equal(options[1]?.isActive, false)

assert.equal(matchesPermissionUserKeyword(options[0]!, ' WANG '), true, '按姓名忽略大小写匹配')
assert.equal(matchesPermissionUserKeyword(options[1]!, 'bob@example'), true, '按邮箱匹配')
assert.equal(matchesPermissionUserKeyword(options[2]!, 'alice'), false)
assert.equal(matchesPermissionUserKeyword(options[2]!, '  '), true, '空关键字不过滤')

const delta = buildPermissionUserDelta(['u-alice', 'u-bob'], ['u-bob', 'u-carol'])
assert.deepEqual(delta, { addUserGuids: ['u-carol'], removeUserGuids: ['u-alice'] })
assert.equal(isPermissionUserDeltaEmpty(delta), false)
assert.equal(isPermissionUserDeltaEmpty(buildPermissionUserDelta(['u-a', 'u-b'], ['u-b', 'u-a'])), true, '顺序变化不产生增量')

async function run() {
  const requestedPages: number[] = []
  const all = await loadAllUserPages(async (page) => {
    requestedPages.push(page)
    return { items: [`user-${page}`], total: 3, page, pageSize: 1, totalPages: 3 }
  })
  assert.deepEqual(all, ['user-1', 'user-2', 'user-3'])
  assert.deepEqual(requestedPages, [1, 2, 3])

  // 缺少 totalPages 时按 total/pageSize 推算页数。
  const derived = await loadAllUserPages(async (page) => ({ items: [page], total: 2, page, pageSize: 1 }))
  assert.deepEqual(derived, [1, 2])

  // 分页元数据异常（总页数持续增长）时受页数上限保护，不会无限请求。
  let calls = 0
  await loadAllUserPages(async (page) => {
    calls += 1
    return { items: [page], total: 999_999, page, pageSize: 1, totalPages: 999_999 }
  }, 5)
  assert.equal(calls, 5)

  // 空页立即停止，避免服务端 totalPages 偏大时空转。
  let emptyCalls = 0
  await loadAllUserPages(async (page) => {
    emptyCalls += 1
    return { items: [], total: 10, page, pageSize: 5, totalPages: 2 }
  })
  assert.equal(emptyCalls, 1)

  console.log('permissionUserAssignment.test: ok')
}

void run().catch((error) => {
  console.error(error)
  process.exitCode = 1
})
