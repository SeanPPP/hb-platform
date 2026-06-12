import { BulbOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { message } from 'antd';
import React from 'react';
import styles from '../styles.module.css';

export interface AIFloatingButtonProps {
  onClick?: () => void;
}

const AIFloatingButton: React.FC<AIFloatingButtonProps> = ({ onClick }) => {
  const { t } = useTranslation();

  const handleClick = () => {
    message.info(
      t('salesDetailAnalysisV2.aiButton.comingSoon'),
    );
    onClick?.();
  };

  return (
    <div className={styles.floatingBtn}>
      <button
        type="button"
        className={styles.aiBtn}
        onClick={handleClick}
        title="AI Forecast Report"
      >
        <BulbOutlined style={{ fontSize: 24 }} />
        <span className={styles.aiText}>
          {t('salesDetailAnalysisV2.aiButton.label')}
        </span>
      </button>
    </div>
  );
};

export default AIFloatingButton;
