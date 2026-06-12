import { useTranslation } from 'react-i18next';
import { DatePicker } from 'antd';
import dayjs, { Dayjs } from 'dayjs';
import React from 'react';
import styles from './WeeklyPerformanceCardHeader.module.css';

const { RangePicker } = DatePicker;

interface WeeklyPerformanceCardHeaderProps {
  value: [Dayjs, Dayjs] | null;
  onChange: (dates: [Dayjs, Dayjs] | null) => void;
}

const WeeklyPerformanceCardHeader: React.FC<
  WeeklyPerformanceCardHeaderProps
> = ({ value, onChange }) => {
  const { t } = useTranslation();

  const presetRanges: Record<string, [Dayjs, Dayjs]> = {
    [t('executiveSales.weekly.thisWeek')]: [
      dayjs().startOf('week'),
      dayjs().endOf('week'),
    ],
    [t('executiveSales.weekly.lastWeek')]: [
      dayjs().subtract(1, 'week').startOf('week'),
      dayjs().subtract(1, 'week').endOf('week'),
    ],
    [t('executiveSales.weekly.thisMonth')]: [
      dayjs().startOf('month'),
      dayjs().endOf('month'),
    ],
    [t('executiveSales.weekly.lastMonth')]: [
      dayjs().subtract(1, 'month').startOf('month'),
      dayjs().subtract(1, 'month').endOf('month'),
    ],
  };
  const handleChange = (dates: [Dayjs | null, Dayjs | null] | null) => {
    if (dates && dates[0] && dates[1]) {
      onChange([dates[0], dates[1]]);
    } else {
      onChange(null);
    }
  };

  return (
    <div className={styles.container}>
      <RangePicker
        value={value}
        onChange={handleChange}
        format="YYYY-MM-DD"
        allowClear={false}
        size="small"
        style={{ width: 280 }}
        presets={Object.entries(presetRanges).map(([label, range]) => ({
          label,
          value: range,
        }))}
      />
    </div>
  );
};

export default WeeklyPerformanceCardHeader;
