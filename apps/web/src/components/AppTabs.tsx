import {
  CloseOutlined,
  HolderOutlined,
  PushpinFilled,
  PushpinOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  type DragEndEvent,
  useSensor,
  useSensors,
} from '@dnd-kit/core'
import {
  SortableContext,
  horizontalListSortingStrategy,
  sortableKeyboardCoordinates,
  useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { Button, Dropdown, Space, Tabs } from 'antd'
import type { MenuProps } from 'antd'
import type { CSSProperties, ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import type { TabItem } from '../types/router'
import { useTabsStore } from '../store/tabs'
import { useTranslation } from 'react-i18next'

interface AppTabsProps {
  onRefreshCurrent: () => void
  onRemoveTab: (key: string) => void
  onRemoveOtherTabs: (key: string) => void
  onRemoveLeftTabs: (key: string) => void
  onRemoveRightTabs: (key: string) => void
}

interface DraggableTabNodeProps {
  tabKey: string
  tabTitle: string
  children: ReactNode
}

function DraggableTabNode({ tabKey, tabTitle, children }: DraggableTabNodeProps) {
  const { t } = useTranslation()
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({
    id: tabKey,
  })

  const style: CSSProperties = {
    transform: CSS.Translate.toString(transform),
    transition,
    zIndex: isDragging ? 1 : undefined,
  }

  return (
    <div
      ref={setNodeRef}
      className="app-tab-sortable-node"
      style={style}
      data-dnd-kit-dragging={isDragging}
    >
      {children}
      <button
        ref={setActivatorNodeRef}
        type="button"
        className="app-tab-drag-handle"
        aria-label={t('common.reorderTab', { title: tabTitle })}
        title={t('common.reorderTab', { title: tabTitle })}
        {...attributes}
        {...listeners}
        onClick={(event) => event.stopPropagation()}
      >
        <HolderOutlined />
      </button>
    </div>
  )
}

export default function AppTabs({
  onRefreshCurrent,
  onRemoveTab,
  onRemoveOtherTabs,
  onRemoveLeftTabs,
  onRemoveRightTabs,
}: AppTabsProps) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { tabs, activeKey, pinTabsBar, setPinTabsBar, moveTab } = useTabsStore()
  const sensors = useSensors(
    useSensor(PointerSensor, {
      activationConstraint: {
        distance: 6,
      },
    }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  )
  const sortableTabKeys = tabs.filter((tab) => !tab.affix).map((tab) => tab.key)

  const currentTab = tabs.find((item) => item.key === activeKey)

  const handleDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) {
      return
    }

    moveTab(String(active.id), String(over.id))
  }

  const menuItems: MenuProps['items'] = [
    {
      key: 'refresh',
      label: t('common.refreshCurrentTab'),
      icon: <ReloadOutlined />,
      onClick: onRefreshCurrent,
    },
    {
      key: 'togglePinTabsBar',
      label: pinTabsBar ? t('common.pinTabsBar') : t('common.unpinTabsBar'),
      icon: pinTabsBar ? <PushpinFilled /> : <PushpinOutlined />,
      onClick: () => setPinTabsBar(!pinTabsBar),
    },
    {
      type: 'divider',
    },
    {
      key: 'closeOthers',
      label: t('common.closeOtherTabs'),
      icon: <CloseOutlined />,
      disabled: tabs.length <= 1 || !currentTab,
      onClick: () => currentTab && onRemoveOtherTabs(currentTab.key),
    },
    {
      key: 'closeLeft',
      label: t('common.closeLeftTabs'),
      disabled: tabs.length <= 1 || !currentTab,
      onClick: () => currentTab && onRemoveLeftTabs(currentTab.key),
    },
    {
      key: 'closeRight',
      label: t('common.closeRightTabs'),
      disabled: tabs.length <= 1 || !currentTab,
      onClick: () => currentTab && onRemoveRightTabs(currentTab.key),
    },
  ]

  const orderedMenuItems: MenuProps['items'] = [
    menuItems[1],
    menuItems[2],
    menuItems[0],
    menuItems[3],
    menuItems[4],
  ]

  return (
    <div className="app-tabs">
      <Tabs
        hideAdd
        type="editable-card"
        activeKey={activeKey}
        renderTabBar={(tabBarProps, DefaultTabBar) => (
          <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
            <SortableContext items={sortableTabKeys} strategy={horizontalListSortingStrategy}>
              <DefaultTabBar {...tabBarProps}>
                {(node) => {
                  const tabKey = String(node.key)
                  const tab = tabs.find((item) => item.key === tabKey)

                  if (!tab || tab.affix) {
                    return node
                  }

                  return (
                    <DraggableTabNode key={tabKey} tabKey={tabKey} tabTitle={tab.title}>
                      {node}
                    </DraggableTabNode>
                  )
                }}
              </DefaultTabBar>
            </SortableContext>
          </DndContext>
        )}
        items={tabs.map((tab: TabItem) => ({
          key: tab.key,
          label: tab.title,
          closable: tab.closable !== false,
        }))}
        onChange={(key) => {
          navigate(key)
        }}
        onEdit={(targetKey, action) => {
          if (action === 'remove') {
            onRemoveTab(targetKey as string)
          }
        }}
      />
      <Space size={8} className="app-tabs-actions">
        <Button icon={<ReloadOutlined />} onClick={onRefreshCurrent}>
              {t('common.refresh')}
        </Button>
        <Dropdown menu={{ items: orderedMenuItems }} placement="bottomRight" trigger={['click']}>
          <Button
            type={pinTabsBar ? 'primary' : 'default'}
            icon={pinTabsBar ? <PushpinFilled /> : <PushpinOutlined />}
          >
              {t('common.more')}
          </Button>
        </Dropdown>
      </Space>
    </div>
  )
}
