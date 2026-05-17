import { LoadingOutlined, ShopOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { Spin, Table } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import React, { useMemo } from 'react';
import styles from '../styles.module.css';

export interface BranchDistributionData {
  rank: number;
  branchCode: string;
  branchName: string;
  revenue: number;
  revenueLY: number;
  quantity: number;
  quantityLY: number;
  orderCount: number;
  orderCountLY: number;
  sharePercent: number;
  sharePercentLY: number;
}

export interface BranchCardProps {
  title: string;
  data: BranchDistributionData[];
  loading?: boolean;
  onRowClick?: (branchCode: string) => void;
  selectedRow?: string | null;
  filterSupplierName?: string | null;
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

const branchColors = [
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

const BranchCard: React.FC<BranchCardProps> = ({
  title,
  data,
  loading = false,
  onRowClick,
  selectedRow,
  filterSupplierName,
}) => {
  const { t } = useTranslation();
  const displayTitle = filterSupplierName
    ? `${title} - ${filterSupplierName}`
    : title;

  const columns: ColumnsType<BranchDistributionData> = useMemo(
    () => [
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
        title: t('salesDetailAnalysisV2.branch.name'),
        dataIndex: 'branchName',
        key: 'branchName',
        width: 100,
        fixed: 'left',
        align: 'left',
        render: (name: string, record: BranchDistributionData) => (
          <div className={styles.branchCell}>
            <span
              className={styles.branchName}
              style={{
                color: branchColors[(record.rank - 1) % branchColors.length],
              }}
            >
              {name}
            </span>
            <span className={styles.branchCode}>#{record.branchCode}</span>
          </div>
        ),
      },
      {
        title: t('salesDetailAnalysisV2.branch.revenue'),
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
            render: (value: number, record: BranchDistributionData) => (
              <span
                className={styles.mainValue}
                style={{
                  color: branchColors[(record.rank - 1) % branchColors.length],
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
            render: (_: unknown, record: BranchDistributionData) => {
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
      {
        title: t('salesDetailAnalysisV2.branch.share'),
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
            render: (value: number, record: BranchDistributionData) => (
              <span
                className={styles.mainValue}
                style={{
                  color: branchColors[(record.rank - 1) % branchColors.length],
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
            render: (_: unknown, record: BranchDistributionData) => {
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
      },
      {
        title: t('salesDetailAnalysisV2.branch.qty'),
        key: 'quantity',
        align: 'center',
        className: styles.columnGroupHeader,
        children: [
          {
            title: t('executiveSales.table.current'),
            dataIndex: 'quantity',
            key: 'quantityCurrent',
            width: 70,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.mainValue}>{formatNumber(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.ly'),
            dataIndex: 'quantityLY',
            key: 'quantityLY',
            width: 65,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.lyValue}>{formatNumber(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.yoy'),
            key: 'quantityYoY',
            width: 60,
            align: 'right',
            className: styles.columnSubHeader,
            render: (_: unknown, record: BranchDistributionData) => {
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
      },
      {
        title: t('salesDetailAnalysisV2.branch.orders'),
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
            render: (value: number) => (
              <span className={styles.mainValue}>{formatNumber(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.ly'),
            dataIndex: 'orderCountLY',
            key: 'orderCountLY',
            width: 60,
            align: 'right',
            className: styles.columnSubHeader,
            render: (value: number) => (
              <span className={styles.lyValue}>{formatNumber(value)}</span>
            ),
          },
          {
            title: t('executiveSales.table.yoy'),
            key: 'orderCountYoY',
            width: 55,
            align: 'right',
            className: styles.columnSubHeader,
            render: (_: unknown, record: BranchDistributionData) => {
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
      },
    ],
    [t],
  );

  return (
    <div className={styles.cardBase}>
      <div className={styles.cardHeader}>
        <h2 className={styles.cardTitle}>
          <ShopOutlined className={styles.cardIcon} />
          {displayTitle}
        </h2>
        <span className={styles.cardBadge}>
          {t('salesDetailAnalysisV2.branch.badge')}
        </span>
      </div>

      <div className={styles.cardContent}>
        {loading ? (
          <div className={styles.loadingOverlay}>
            <Spin indicator={<LoadingOutlined spin />} />
          </div>
        ) : data.length === 0 ? (
          <div className={styles.emptyState}>
            <span className={styles.emptyIcon}>🏪</span>
            <span className={styles.emptyText}>
              {t('salesDetailAnalysisV2.branch.noData')}
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
              onClick: () => onRowClick?.(record.branchCode),
              className: `${styles.tableRowClickable} ${
                selectedRow === record.branchCode ? styles.selectedRow : ''
              }`,
            })}
          />
        )}
      </div>
    </div>
  );
};

export default BranchCard;
