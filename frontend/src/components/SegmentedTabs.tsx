interface SegmentedTabItem<T extends string> {
  key: T;
  label: string;
  badge?: string | number | null;
}

interface SegmentedTabsProps<T extends string> {
  items: SegmentedTabItem<T>[];
  value: T;
  onChange: (value: T) => void;
  className?: string;
}

export default function SegmentedTabs<T extends string>({
  items,
  value,
  onChange,
  className = '',
}: SegmentedTabsProps<T>) {
  return (
    <div className={`mb-6 overflow-x-auto -mx-4 px-4 md:mx-0 md:px-0 ${className}`.trim()}>
      <div className="inline-flex min-w-max space-x-1 rounded-lg bg-gray-900 p-1">
        {items.map((item) => (
          <button
            key={item.key}
            type="button"
            onClick={() => onChange(item.key)}
            className={`whitespace-nowrap rounded-md px-3 py-2 text-sm transition-all md:px-6 md:text-base ${
              value === item.key
                ? 'bg-red-600 text-white'
                : 'text-gray-400 hover:bg-gray-800 hover:text-white'
            }`}
          >
            {item.label}
            {item.badge != null && item.badge !== '' && (
              <span className="ml-1 rounded-full bg-red-700 px-1.5 py-0.5 text-xs text-white md:ml-2 md:px-2">
                {item.badge}
              </span>
            )}
          </button>
        ))}
      </div>
    </div>
  );
}
