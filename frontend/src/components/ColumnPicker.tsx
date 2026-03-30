import { useEffect, useRef, useState } from 'react';
import { AdjustmentsHorizontalIcon } from '@heroicons/react/24/outline';

interface ColumnDef {
  key: string;
  label: string;
  alwaysVisible?: boolean;
}

interface ColumnPickerProps {
  columns: ColumnDef[];
  isVisible: (col: string) => boolean;
  onToggle: (col: string) => void;
}

export default function ColumnPicker({
  columns,
  isVisible,
  onToggle,
}: ColumnPickerProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    const handleMouseDown = (event: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    };

    document.addEventListener('mousedown', handleMouseDown);
    return () => document.removeEventListener('mousedown', handleMouseDown);
  }, [open]);

  return (
    <div className="relative" ref={containerRef}>
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        title="Choose visible columns"
        className={`flex items-center gap-1.5 rounded border px-2.5 py-1.5 text-sm transition-colors ${
          open
            ? 'border-blue-500/50 bg-blue-600/20 text-white'
            : 'border-gray-700 bg-gray-800 text-gray-400 hover:border-gray-600 hover:text-white'
        }`}
      >
        <AdjustmentsHorizontalIcon className="h-4 w-4" />
        <span className="hidden sm:inline">Columns</span>
      </button>

      {open && (
        <div className="absolute right-0 top-full z-50 mt-1 w-52 overflow-hidden rounded-lg border border-gray-700 bg-gray-900 shadow-xl">
          <div className="border-b border-gray-700 px-3 py-2">
            <p className="text-xs font-medium uppercase tracking-wide text-gray-400">
              Visible Columns
            </p>
          </div>
          <div className="max-h-72 overflow-y-auto p-1">
            {columns.map((column) => (
              <label
                key={column.key}
                className={`flex cursor-pointer items-center gap-2.5 rounded px-2 py-1.5 transition-colors ${
                  column.alwaysVisible ? 'cursor-not-allowed opacity-50' : 'hover:bg-gray-800'
                }`}
              >
                <input
                  type="checkbox"
                  checked={isVisible(column.key)}
                  onChange={() => {
                    if (!column.alwaysVisible) {
                      onToggle(column.key);
                    }
                  }}
                  disabled={column.alwaysVisible}
                  className="h-3.5 w-3.5 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
                />
                <span className="text-sm text-gray-300">{column.label}</span>
              </label>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
