import assert from 'node:assert/strict'
import { loadCompleteSetCodeDraftRows } from './setCodeDraftLoader'

type Row = { id?: string; value: number }

const allRows = Array.from({ length: 201 }, (_, index): Row => ({
  id: `row-${index + 1}`,
  value: index + 1,
}))
const requests: Array<[number, number]> = []
const completeRows = await loadCompleteSetCodeDraftRows<Row>({
  fetchPage: async (pageIndex, pageSize) => {
    requests.push([pageIndex, pageSize])
    return {
      items: pageSize === 200 ? allRows.slice(0, 200) : allRows,
      total: allRows.length,
    }
  },
  getRowId: (row) => row.id,
})
assert.deepEqual(requests, [[1, 200], [1, 201]], '超过首屏时应按权威总数整页回读，避开不稳定 offset 分页')
assert.equal(completeRows.length, 201)
assert.equal(completeRows[200].id, 'row-201')

await assert.rejects(
  () => loadCompleteSetCodeDraftRows<Row>({
    fetchPage: async () => ({
      items: [
        { id: 'duplicate', value: 1 },
        { id: 'duplicate', value: 2 },
      ],
      total: 2,
    }),
    getRowId: (row) => row.id,
  }),
  /重复行/,
  '完整快照内出现重复 ID 时不得标记草稿就绪',
)

await assert.rejects(
  () => loadCompleteSetCodeDraftRows<Row>({
    fetchPage: async () => ({
      items: [{ id: 'only-one', value: 1 }],
      total: 2,
    }),
    getRowId: (row) => row.id,
  }),
  /总数不一致/,
  '回读行数与权威总数不一致时必须拒绝不完整草稿',
)

await assert.rejects(
  () => loadCompleteSetCodeDraftRows<Row>({
    fetchPage: async () => ({ items: [], total: 20_001 }),
    getRowId: (row) => row.id,
    maxItems: 20_000,
  }),
  /安全上限/,
)
