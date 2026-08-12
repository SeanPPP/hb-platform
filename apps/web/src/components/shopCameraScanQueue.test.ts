import assert from 'node:assert/strict'
import {
  createShopCameraScanQueue,
  SHOP_CAMERA_SCAN_MAX_QUEUE,
  SHOP_CAMERA_SCAN_REPEAT_RELEASE_MS,
} from './shopCameraScanQueue'

const queue = createShopCameraScanQueue()

assert.equal(queue.enqueue(' 930000000001 ', 1000), 'queued', '首个条码应标准化后入队')
assert.equal(queue.enqueue('930000000002', 1010), 'queued', '处理中也应允许不同条码排队')

const firstLease = queue.takeNext()
assert.ok(firstLease, '队首条码应取得处理 lease')
assert.equal(firstLease.value, '930000000001', '队列必须保持 FIFO 顺序')
assert.equal(queue.takeNext(), null, '当前 lease 完成前禁止并发处理下一条')

assert.equal(queue.enqueue('930000000001', 1100), 'duplicate', '同码持续留在画面时不得重复入队')
queue.finish(firstLease)

const secondLease = queue.takeNext()
assert.ok(secondLease, '完成第一条后应继续消费队列')
assert.equal(secondLease.value, '930000000002', '第二条应按入队顺序处理')
queue.finish(secondLease)

assert.equal(
  queue.enqueue('930000000001', 1100 + SHOP_CAMERA_SCAN_REPEAT_RELEASE_MS - 1),
  'duplicate',
  '同码离开不足 1.2 秒仍应拦截',
)
assert.equal(
  queue.enqueue('930000000001', 1100 + SHOP_CAMERA_SCAN_REPEAT_RELEASE_MS * 2),
  'queued',
  '同码离开超过 1.2 秒后应允许再次扫描',
)
queue.reset()

assert.equal(queue.enqueue('QUEUED-BEFORE-PAUSE', 4900), 'queued', '暂停前的条码应进入队列')
queue.setPaused(true)
assert.equal(queue.enqueue('PAUSED-CODE', 5000), 'paused', '候选选择期间不应接收新条码')
assert.equal(queue.takeNext(), null, '暂停期间不应消费已有队列')
queue.noteSighting('PAUSED-CODE', 5000 + SHOP_CAMERA_SCAN_REPEAT_RELEASE_MS * 2)
queue.setPaused(false)
const resumedLease = queue.takeNext()
assert.ok(resumedLease, '恢复后应继续消费暂停前已有队列')
assert.equal(resumedLease.value, 'QUEUED-BEFORE-PAUSE')
queue.finish(resumedLease)
assert.equal(
  queue.enqueue('PAUSED-CODE', 5000 + SHOP_CAMERA_SCAN_REPEAT_RELEASE_MS * 2 + 1),
  'duplicate',
  '选择器长时间暂停后，同码仍留在画面时应继续拦截',
)
assert.equal(
  queue.enqueue('PAUSED-CODE', 5000 + SHOP_CAMERA_SCAN_REPEAT_RELEASE_MS * 3 + 2),
  'queued',
  '移开后应恢复同码接收能力',
)
queue.reset()

for (let index = 0; index < SHOP_CAMERA_SCAN_MAX_QUEUE; index += 1) {
  assert.equal(queue.enqueue(`QUEUE-${index}`, 10_000 + index), 'queued', `第 ${index + 1} 条应进入有界队列`)
}
assert.equal(queue.enqueue('QUEUE-OVERFLOW', 20_000), 'full', '达到 20 条容量后必须拒绝继续堆积')
assert.equal(queue.getSnapshot().pendingCount, SHOP_CAMERA_SCAN_MAX_QUEUE, '快照应报告准确待处理数量')

const staleLease = queue.takeNext()
assert.ok(staleLease, '重置前应能取得旧会话 lease')
assert.equal(
  queue.enqueue('QUEUE-WHILE-PROCESSING', 20_001),
  'full',
  '处理中 1 条加等待 19 条仍应计入 20 条上限',
)
queue.reset()
assert.equal(queue.enqueue('NEW-SESSION', 30_000), 'queued', '重置后新会话应立即可用')
const currentLease = queue.takeNext()
assert.ok(currentLease, '新会话应能取得新 lease')
queue.finish(staleLease)
assert.equal(queue.takeNext(), null, '旧 lease 完成不得释放新会话处理中状态')
queue.finish(currentLease)
assert.equal(queue.getSnapshot().processingValue, undefined, '当前 lease 完成后应释放处理状态')

assert.equal(queue.enqueue('   ', 40_000), 'invalid', '空白条码不得入队')

console.log('shopCameraScanQueue.test.ts: ok')
