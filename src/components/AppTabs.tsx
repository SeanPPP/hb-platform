import {
  CloseOutlined,
  PushpinFilled,
  PushpinOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import {
  DndContext,
  PointerSensor,
  closestCenter,
  type DragEndEvent,
  useSensor,
  useSensors,
} from '@dnd-kit/core'
import {
  SortableContext,
  horizontalListSortingStrategy,
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
  children: ReactNode
}

function DraggableTabNode({ tabKey, children }: DraggableTabNodeProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: tabKey,
  })

  const style: CSSProperties = {
    transform: CSS.Translate.toString(transform),
    transition,
    cursor: 'move',
    zIndex: isDragging ? 1 : undefined,
  }

  return (
    <div
      ref={setNodeRef}
      className="app-tab-sortable-node"
      style={style}
      {...attributes}
      {...listeners}
    >
      {children}
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
  const { tabs, activeKey, pinTabsBar, setActiveKey, setPinTabsBar, moveTab } = useTabsStore()
  const sensors = useSensors(
    useSensor(PointerSensor, {
      activationConstraint: {
        distance: 6,
      },
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
      label: t('common.refreshCurrentTab', '刷新当前页签'),
      icon: <ReloadOutlined />,
      onClick: onRefreshCurrent,
    },
    {
      key: 'togglePinTabsBar',
      label: pinTabsBar ? t('common.pinTabsBar', '页签栏：已固定') : t('common.unpinTabsBar', '页签栏：未固定'),
      icon: pinTabsBar ? <PushpinFilled /> : <PushpinOutlined />,
      onClick: () => setPinTabsBar(!pinTabsBar),
    },
    {
      type: 'divider',
    },
    {
      key: 'closeOthers',
      label: t('common.closeOtherTabs', '关闭其他页签'),
      icon: <CloseOutlined />,
      disabled: tabs.length <= 1 || !currentTab,
      onClick: () => currentTab && onRemoveOtherTabs(currentTab.key),
    },
    {
      key: 'closeLeft',
      label: t('common.closeLeftTabs', '关闭左侧页签'),
      disabled: tabs.length <= 1 || !currentTab,
      onClick: () => currentTab && onRemoveLeftTabs(currentTab.key),
    },
    {
      key: 'closeRight',
      label: t('common.closeRightTabs', '关闭右侧页签'),
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
                    <DraggableTabNode key={tabKey} tabKey={tabKey}>
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
          setActiveKey(key)
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
              {t('common.refresh', '刷新')}
        </Button>
        <Dropdown menu={{ items: orderedMenuItems }} placement="bottomRight" trigger={['click']}>
          <Button
            type={pinTabsBar ? 'primary' : 'default'}
            icon={pinTabsBar ? <PushpinFilled /> : <PushpinOutlined />}
          >
              {t('common.more', '更多')}
          </Button>
        </Dropdown>
      </Space>
    </div>
  )
}
