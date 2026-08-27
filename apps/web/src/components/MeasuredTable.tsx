import { Profiler, forwardRef, useCallback, useLayoutEffect, useRef } from 'react'
import type { ForwardedRef, ProfilerOnRenderCallback, ReactElement, RefAttributes } from 'react'
import { Table as AntdTable } from 'antd'
import type { TableProps } from 'antd'
import type { TableRef } from 'antd/es/table'
import {
  recordWebTableMetric,
  WEB_TABLE_REACT_COMMIT_METRIC,
  WEB_TABLE_RENDER_TO_PAINT_METRIC,
} from '../services/performanceMetricService'

const INITIAL_DATA_SOURCE = Symbol('initial-data-source')

export interface MeasuredTableProps<RecordType = Record<string, unknown>>
  extends TableProps<RecordType> {
  metricId: string
}

type MeasuredTableCallable = <RecordType = Record<string, unknown>>(
  props: MeasuredTableProps<RecordType> & RefAttributes<TableRef>,
) => ReactElement

interface MeasuredTableStatics {
  displayName?: string
  Summary: typeof AntdTable.Summary
  Column: typeof AntdTable.Column
  ColumnGroup: typeof AntdTable.ColumnGroup
  SELECTION_COLUMN: typeof AntdTable.SELECTION_COLUMN
  EXPAND_COLUMN: typeof AntdTable.EXPAND_COLUMN
  SELECTION_ALL: typeof AntdTable.SELECTION_ALL
  SELECTION_INVERT: typeof AntdTable.SELECTION_INVERT
  SELECTION_NONE: typeof AntdTable.SELECTION_NONE
}

function now() {
  return typeof performance !== 'undefined' ? performance.now() : Date.now()
}

function MeasuredTableRender<RecordType>(
  { metricId, dataSource, ...tableProps }: MeasuredTableProps<RecordType>,
  ref: ForwardedRef<TableRef>,
) {
  const previousDataSourceRef = useRef<unknown>(INITIAL_DATA_SOURCE)
  const updateStartedAtRef = useRef<number>()
  const updateSequenceRef = useRef(0)

  if (previousDataSourceRef.current !== dataSource) {
    previousDataSourceRef.current = dataSource
    updateStartedAtRef.current = now()
    updateSequenceRef.current += 1
  }

  const handleRender = useCallback<ProfilerOnRenderCallback>(
    (_id, _phase, actualDuration) => {
      recordWebTableMetric(WEB_TABLE_REACT_COMMIT_METRIC, actualDuration, {
        metricId,
        outcome: 'success',
      })
    },
    [metricId],
  )

  useLayoutEffect(() => {
    if (typeof window === 'undefined' || updateStartedAtRef.current === undefined) {
      return
    }

    const sequence = updateSequenceRef.current
    const startedAt = updateStartedAtRef.current
    let secondFrame = 0
    const firstFrame = window.requestAnimationFrame(() => {
      secondFrame = window.requestAnimationFrame(() => {
        if (sequence !== updateSequenceRef.current) {
          return
        }

        updateStartedAtRef.current = undefined
        recordWebTableMetric(WEB_TABLE_RENDER_TO_PAINT_METRIC, now() - startedAt, {
          metricId,
          outcome: 'success',
        })
      })
    })

    return () => {
      window.cancelAnimationFrame(firstFrame)
      if (secondFrame) {
        window.cancelAnimationFrame(secondFrame)
      }
    }
  }, [dataSource, metricId])

  return (
    <Profiler id={`table:${metricId}`} onRender={handleRender}>
      <AntdTable<RecordType> ref={ref} dataSource={dataSource} {...tableProps} />
    </Profiler>
  )
}

const ForwardMeasuredTable = forwardRef(MeasuredTableRender) as MeasuredTableCallable

export const MeasuredTable = Object.assign(ForwardMeasuredTable, {
  Summary: AntdTable.Summary,
  Column: AntdTable.Column,
  ColumnGroup: AntdTable.ColumnGroup,
  SELECTION_COLUMN: AntdTable.SELECTION_COLUMN,
  EXPAND_COLUMN: AntdTable.EXPAND_COLUMN,
  SELECTION_ALL: AntdTable.SELECTION_ALL,
  SELECTION_INVERT: AntdTable.SELECTION_INVERT,
  SELECTION_NONE: AntdTable.SELECTION_NONE,
}) as MeasuredTableCallable & MeasuredTableStatics

MeasuredTable.displayName = 'MeasuredTable'
