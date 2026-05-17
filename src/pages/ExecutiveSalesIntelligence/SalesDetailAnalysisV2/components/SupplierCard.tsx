import { useTranslation } from 'react-i18next';
import { Spin, Table } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import React, { useMemo } from 'react';
import styles from '../styles.module.css';

export interface SupplierSalesData {
  rank: number;
  supplierCode?: string;
  supplierName: string;
  revenue: number;
  revenueLY: number;
  quantity?: number;
  quantityLY?: number;
  orderCount?: number;
  orderCountLY?: number;
  sharePercent: number;
  sharePercentLY: number;
}

export interface SupplierCardProps {
  title: string;
  icon: 'local' | 'china';
  data: SupplierSalesData[];
  loading?: boolean;
  onRowClick?: (supplierCode: string) => void;
  selectedRow?: string | null;
  filterBranchName?: string | null;
}

const formatCurrency = (value: number): string => {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  }).format(value);
};

const formatNumber = (value: number): string => {
  return new Intl.NumberFormat('en-US').format(value);
};

const formatPercent = (value: number): string => {
  return `${value.toFixed(1)}%`;
};

const calculateGrowth = (
  current: number,
  previous: number,
): { value: string; isPositive: boolean } => {
  if (previous === 0) {
    return { value: '+100%', isPositive: true };
  }
  const growth = ((current - previous) / previous) * 100;
  const isPositive = growth >= 0;
  return {
    value: `${isPositive ? '+' : ''}${growth.toFixed(1)}%`,
    isPositive,
  };
};

const supplierColors = [
  '#3B82F6',
  '#10B981',
  '#F59E0B',
  '#EF4444',
  '#8B5CF6',
  '#EC4899',
  '#06B6D4',
  '#84CC16',
  '#F97316',
  '#6366F1',
  '#14B8A6',
  '#A855F7',
  '#F472B6',
  '#22D3EE',
  '#4ADE80',
  '#FB923C',
  '#818CF8',
  '#2DD4BF',
  '#FBBF24',
  '#E879F9',
];

const SupplierCard: React.FC<SupplierCardProps> = ({
  title,
  icon,
  data,
  loading = false,
  onRowClick,
  selectedRow,
  filterBranchName,
}) => {
  const { t } = useTranslation();
  const displayTitle = filterBranchName
    ? `${title} - ${filterBranchName}`
    : title;
  const isLocal = icon === 'local';

  const columns: ColumnsType<SupplierSalesData> = useMemo(() => {
    const baseColumns: ColumnsType<SupplierSalesData> = [
      {
        title: '#',
        dataIndex: 'rank',
        key: 'rank',
        width: 32,
        align: 'center',
        render: (rank: number) => (
          <span className={styles.rankNumber}>{rank}</span>
        ),
      },
      {
        title: isLocal
          ? t('salesDetailAnalysisV2.supplier.name')
          : t('salesDetailAnalysisV2.supplier.nameId'),
        dataIndex: 'supplierName',
        key: 'supplierName',
        width: 110,
        fixed: 'left',
        align: 'left',
        render: (name: string, record: SupplierSalesData) => (
          <div className={styles.supplierCell}>
            <span
              className={styles.supplierName}
              style={{
                color:
                  supplierColors[(record.rank - 1) % supplierColors.length],
              }}
            >
              {name}
            </span>
            {record.supplierCode && (
              <span className={styles.supplierCode}>
                #{record.supplierCode}
              </span>
            )}
          </div>
        ),
      },
      {
        title: t('salesDetailAnalysisV2.supplier.revenue'),
        key: 'revenue',
        align: 'center',
        className: styles.columnGroupHeader,
        children: [
          {
            title: t('executiveSales.table.current'),
            dataIndex: 'revenue',
            key: 'revenueCurrent',
            width: 90,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number, record: SupplierSalesData) => (
              <span
                className={styles.mainValue}
                style={{
                  color:
                    supplierColors[(record.rank - 1) % supplierColors.length],
                }}
              >
                {formatCurrency(value)}
              </span>
            ),
          },
          {
            title: t('executiveSales.table.ly'),
            dataIndex: 'revenueLY',
            key: 'revenueLY',
            width: 80,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.lyValue}>{formatCurrency(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.yoy'),
            key: 'revenueYoY',
            width: 65,
            align: 'right',
            className: styles.columnSubHeader,
            render: (_: unknown, record: SupplierSalesData) => {
              const yoyChange = calculateGrowth(
                record.revenue,
                record.revenueLY,
              );
              return (
                <span
                  className={`${styles.trendBadge} ${
                    yoyChange.isPositive
                      ? styles.trendPositive
                      : styles.trendNegative
                  }`}
                >
                  {yoyChange.value}
                </span>
              );
            },
          },
        ],
      },
    ];

    if (isLocal) {
      baseColumns.push({
        title: t('salesDetailAnalysisV2.supplier.orders'),
        key: 'orderCount',
        align: 'center',
        className: styles.columnGroupHeader,
        children: [
          {
            title: t('executiveSales.table.current'),
            dataIndex: 'orderCount',
            key: 'orderCountCurrent',
            width: 65,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number | undefined) =>
              value !== undefined ? (
                <span className={styles.mainValue}>{formatNumber(value)}</span>
              ) : null,
          },
          {
            title: t('executiveSales.table.ly'),
            dataIndex: 'orderCountLY',
            key: 'orderCountLY',
            width: 60,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number | undefined) =>
              value !== undefined ? (
                <span className={styles.lyValue}>{formatNumber(value)}</span>
              ) : null,
          },
          {
            title: t('executiveSales.table.yoy'),
            key: 'orderCountYoY',
            width: 55,
            align: 'right',
            className: styles.columnSubHeader,
            render: (_: unknown, record: SupplierSalesData) => {
              if (
                record.orderCount === undefined ||
                record.orderCountLY === undefined
              ) {
                return null;
              }
              const yoyChange = calculateGrowth(
                record.orderCount,
                record.orderCountLY,
              );
              return (
                <span
                  className={`${styles.trendBadge} ${
                    yoyChange.isPositive
                      ? styles.trendPositive
                      : styles.trendNegative
                  }`}
                >
                  {yoyChange.value}
                </span>
              );
            },
          },
        ],
      });
    }

    baseColumns.push({
      title: t('salesDetailAnalysisV2.supplier.qty'),
      key: 'quantity',
      align: 'center',
      className: styles.columnGroupHeader,
      children: [
        {
          title: t('executiveSales.table.current'),
          dataIndex: 'quantity',
          key: 'quantityCurrent',
          width: 65,
          align: 'right',
          className: styles.columnSubHeader,
          render: (value: number | undefined) =>
            value !== undefined ? (
              <span className={styles.mainValue}>{formatNumber(value)}</span>
            ) : null,
        },
        {
          title: t('executiveSales.table.ly'),
          dataIndex: 'quantityLY',
          key: 'quantityLY',
          width: 60,
          align: 'right',
          className: styles.columnSubHeader,
          render: (value: number | undefined) =>
            value !== undefined ? (
              <span className={styles.lyValue}>{formatNumber(value)}</span>
            ) : null,
        },
        {
          title: t('executiveSales.table.yoy'),
          key: 'quantityYoY',
          width: 55,
          align: 'right',
          className: styles.columnSubHeader,
          render: (_: unknown, record: SupplierSalesData) => {
            if (
              record.quantity === undefined ||
              record.quantityLY === undefined
            ) {
              return null;
            }
            const yoyChange = calculateGrowth(
              record.quantity,
              record.quantityLY,
            );
            return (
              <span
                className={`${styles.trendBadge} ${
                  yoyChange.isPositive
                    ? styles.trendPositive
                    : styles.trendNegative
                }`}
              >
                {yoyChange.value}
              </span>
            );
          },
        },
      ],
    });

    if (!isLocal) {
      baseColumns.push({
        title: t('salesDetailAnalysisV2.supplier.orders'),
        key: 'orderCount',
        align: 'center',
        className: styles.columnGroupHeader,
        children: [
          {
            title: t('executiveSales.table.current'),
            dataIndex: 'orderCount',
            key: 'orderCountCurrent',
            width: 65,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number | undefined) =>
              value !== undefined ? (
                <span className={styles.mainValue}>{formatNumber(value)}</span>
              ) : null,
          },
          {
            title: t('executiveSales.table.ly'),
            dataIndex: 'orderCountLY',
            key: 'orderCountLY',
            width: 60,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number | undefined) =>
              value !== undefined ? (
                <span className={styles.lyValue}>{formatNumber(value)}</span>
              ) : null,
          },
          {
            title: t('executiveSales.table.yoy'),
            key: 'orderCountYoY',
            width: 55,
            align: 'right',
            className: styles.columnSubHeader,
            render: (_: unknown, record: SupplierSalesData) => {
              if (
                record.orderCount === undefined ||
                record.orderCountLY === undefined
              ) {
                return null;
              }
              const yoyChange = calculateGrowth(
                record.orderCount,
                record.orderCountLY,
              );
              return (
                <span
                  className={`${styles.trendBadge} ${
                    yoyChange.isPositive
                      ? styles.trendPositive
                      : styles.trendNegative
                  }`}
                >
                  {yoyChange.value}
                </span>
              );
            },
          },
        ],
      });
    }

    baseColumns.push({
      title: t('salesDetailAnalysisV2.supplier.share'),
      key: 'share',
      align: 'center',
      className: styles.columnGroupHeader,
      children: [
        {
          title: t('executiveSales.table.current'),
          dataIndex: 'sharePercent',
          key: 'sharePercentCurrent',
          width: 70,
          align: 'right',
          className: styles.columnSubHeader,
          render: (value: number, record: SupplierSalesData) => (
            <span
              className={styles.mainValue}
              style={{
                color:
                  supplierColors[(record.rank - 1) % supplierColors.length],
              }}
            >
              {formatPercent(value)}
            </span>
          ),
        },
        {
          title: t('executiveSales.table.ly'),
          dataIndex: 'sharePercentLY',
          key: 'sharePercentLY',
          width: 65,
          align: 'right',
          className: styles.columnSubHeader,
          render: (value: number) => (
            <span className={styles.lyValue}>{formatPercent(value)}</span>
          ),
        },
        {
          title: t('executiveSales.table.yoy'),
          key: 'sharePercentYoY',
          width: 60,
          align: 'right',
          className: styles.columnSubHeader,
          render: (_: unknown, record: SupplierSalesData) => {
            const yoyChange = calculateGrowth(
              record.sharePercent,
              record.sharePercentLY,
            );
            return (
              <span
                className={`${styles.trendBadge} ${
                  yoyChange.isPositive
                    ? styles.trendPositive
                    : styles.trendNegative
                }`}
              >
                {yoyChange.value}
              </span>
            );
          },
        },
      ],
    });

    return baseColumns;
  }, [t, isLocal]);

  const iconEmoji = isLocal ? '🚂' : '🦘';
  const badgeText = isLocal
    ? t('salesDetailAnalysisV2.supplier.localBadge')
    : t('salesDetailAnalysisV2.supplier.chinaBadge');

  return (
    <div className={styles.cardBase}>
      <div className={styles.cardHeader}>
        <h2 className={styles.cardTitle}>
          <span style={{ fontSize: '18px' }}>{iconEmoji}</span>
          {displayTitle}
        </h2>
        <span className={styles.cardBadge}>{badgeText}</span>
      </div>

      <div className={styles.cardContent}>
        {loading ? (
          <div className={styles.loadingOverlay}>
            <Spin />
          </div>
        ) : data.length === 0 ? (
          <div className={styles.emptyState}>
            <span className={styles.emptyIcon}>📦</span>
            <span className={styles.emptyText}>
              {t('salesDetailAnalysisV2.supplier.noData')}
            </span>
          </div>
        ) : (
          <Table
            columns={columns}
            dataSource={data}
            rowKey="rank"
            size="small"
            pagination={false}
            virtual
            scroll={{ y: 480 }}
            className={styles.virtualTable}
            onRow={(record) => ({
              onClick: () =>
                record.supplierCode && onRowClick?.(record.supplierCode),
              className: `${styles.tableRowClickable} ${
                selectedRow === record.supplierCode ? styles.selectedRow : ''
              }`,
            })}
          />
        )}
      </div>
    </div>
  );
};

export default SupplierCard;
