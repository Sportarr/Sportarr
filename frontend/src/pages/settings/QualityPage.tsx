import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import PageHeader from '../../components/PageHeader';
import QualitySettings from './QualitySettings';
import CustomFormatsSettings from './CustomFormatsSettings';
import TrashGuidesSettings from './TrashGuidesSettings';

/**
 * One "Quality" home that folds the three previously-separate pages - Quality
 * Definitions, Custom Formats, and TRaSH Guides - into tabs, so everything that
 * governs release scoring lives in one place instead of three nav entries.
 */

type QualityTab = 'definitions' | 'formats' | 'trash';

const TABS: { key: QualityTab; label: string }[] = [
  { key: 'definitions', label: 'Quality Definitions' },
  { key: 'formats', label: 'Custom Formats' },
  { key: 'trash', label: 'TRaSH Guides' },
];

export default function QualityPage() {
  const [searchParams] = useSearchParams();
  const requested = searchParams.get('tab');
  const [tab, setTab] = useState<QualityTab>(
    requested === 'customformats' || requested === 'formats'
      ? 'formats'
      : requested === 'trashguides' || requested === 'trash'
        ? 'trash'
        : 'definitions'
  );

  return (
    <div className="px-4 pt-6 sm:px-6">
      <PageHeader
        title="Quality"
        subtitle="File sizes, custom-format scoring, and TRaSH Guides sync - all in one place"
      />

      <div className="mb-6 flex gap-1 overflow-x-auto border-b border-gray-800">
        {TABS.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={`-mb-px whitespace-nowrap border-b-2 px-4 py-2 text-sm font-medium transition-colors ${
              tab === t.key
                ? 'border-red-500 text-white'
                : 'border-transparent text-gray-400 hover:text-gray-200'
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'definitions' && <QualitySettings embedded />}
      {tab === 'formats' && <CustomFormatsSettings embedded />}
      {tab === 'trash' && <TrashGuidesSettings embedded />}
    </div>
  );
}
