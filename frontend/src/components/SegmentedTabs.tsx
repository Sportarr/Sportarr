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
    <div className={`mb-6 ${className}`.trim()}>
      {/* Phones: pills wrap onto extra rows so no label ever clips off-screen.
          sm and up keeps the single row. */}
      <div className="inline-flex flex-wrap gap-1 rounded-lg bg-gray-900 p-1 sm:flex-nowrap sm:min-w-max">
        {items.map((item) => (
          <button
            key={item.key}
            type="button"
            onClick={() => onChange(item.key)}
            className={`whitespace-nowrap rounded-md px-1.5 py-2 text-xs transition-all sm:px-3 sm:text-sm md:px-6 md:text-base ${
              value === item.key
                ? 'bg-red-600 text-white'
                : 'text-gray-400 hover:bg-gray-800 hover:text-white'
            }`}
          >
            {item.label}
            {item.badge != null && item.badge !== '' && (
              <span className="ml-0.5 rounded-full bg-red-700 px-1 py-0.5 text-[10px] text-white sm:ml-1 sm:px-1.5 sm:text-xs md:ml-2 md:px-2">
                {item.badge}
              </span>
            )}
          </button>
        ))}
      </div>
    </div>
  );
}
