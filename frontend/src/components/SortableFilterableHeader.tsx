import {
  ChevronDownIcon,
  ChevronUpIcon,
  FunnelIcon,
} from '@heroicons/react/24/outline';
import { TABLE_HEADER_CLS } from '../utils/designTokens';

interface SortableFilterableHeaderProps {
  col: string;
  label: string;
  sortCol: string;
  sortDir: 'asc' | 'desc';
  onSort: (col: string) => void;
  colFilters: Record<string, string>;
  activeFilterCol: string | null;
  onFilterChange: (col: string, value: string) => void;
  onFilterToggle: (col: string) => void;
  className?: string;
  centered?: boolean;
}

export default function SortableFilterableHeader({
  col,
  label,
  sortCol,
  sortDir,
  onSort,
  colFilters,
  activeFilterCol,
  onFilterChange,
  onFilterToggle,
  className = TABLE_HEADER_CLS,
  centered = false,
}: SortableFilterableHeaderProps) {
  const isActive = sortCol === col;
  const hasFilter = Boolean(colFilters[col]);

  return (
    <th
      className={`${className} cursor-pointer select-none hover:bg-gray-800/30`}
      onClick={() => onSort(col)}
    >
      <div className={`flex items-center gap-1${centered ? ' justify-center' : ''}`}>
        <span>{label}</span>
        {isActive ? (
          sortDir === 'asc' ? (
            <ChevronUpIcon className="h-3 w-3 flex-shrink-0" />
          ) : (
            <ChevronDownIcon className="h-3 w-3 flex-shrink-0" />
          )
        ) : (
          <span className="h-3 w-3 flex-shrink-0 opacity-0" />
        )}
        <button
          type="button"
          className={`flex-shrink-0 rounded p-0.5 ${
            hasFilter
              ? 'text-amber-400 hover:bg-gray-700'
              : 'text-gray-600 hover:bg-gray-700 hover:text-gray-400'
          }`}
          onClick={(event) => {
            event.stopPropagation();
            onFilterToggle(col);
          }}
          title="Filter"
        >
          <FunnelIcon className="h-3 w-3" />
        </button>
      </div>
      {activeFilterCol === col && (
        <div className="mt-1" onClick={(event) => event.stopPropagation()}>
          <input
            autoFocus
            type="text"
            value={colFilters[col] || ''}
            onChange={(event) => onFilterChange(col, event.target.value)}
            placeholder="Filter..."
            className="w-full rounded border border-gray-600 bg-gray-900 px-1.5 py-0.5 text-xs font-normal normal-case text-white"
          />
        </div>
      )}
    </th>
  );
}
