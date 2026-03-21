import { useState } from 'react';

export interface TableSortFilterState {
  sortCol: string;
  sortDir: 'asc' | 'desc';
  colFilters: Record<string, string>;
  activeFilterCol: string | null;
  handleColSort: (col: string) => void;
  onFilterChange: (col: string, value: string) => void;
  onFilterToggle: (col: string) => void;
}

export function applyTableSortFilter<T>(
  items: T[],
  colFilters: Record<string, string>,
  sortCol: string,
  sortDir: 'asc' | 'desc',
  fieldExtractor: (col: string, item: T) => string
): T[] {
  let result = items;

  Object.entries(colFilters).forEach(([col, value]) => {
    if (!value.trim()) {
      return;
    }

    const normalizedValue = value.toLowerCase();
    result = result.filter((item) =>
      fieldExtractor(col, item).toLowerCase().includes(normalizedValue)
    );
  });

  if (!sortCol) {
    return result;
  }

  return [...result].sort((left, right) => {
    const comparison = fieldExtractor(sortCol, left).localeCompare(
      fieldExtractor(sortCol, right),
      undefined,
      { numeric: true }
    );

    return sortDir === 'asc' ? comparison : -comparison;
  });
}

export function useTableSortFilter(initialSortCol: string): TableSortFilterState {
  const [sortCol, setSortCol] = useState(initialSortCol);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
  const [colFilters, setColFilters] = useState<Record<string, string>>({});
  const [activeFilterCol, setActiveFilterCol] = useState<string | null>(null);

  const handleColSort = (col: string) => {
    if (sortCol === col) {
      setSortDir((current) => (current === 'asc' ? 'desc' : 'asc'));
      return;
    }

    setSortCol(col);
    setSortDir('asc');
  };

  const onFilterChange = (col: string, value: string) => {
    setColFilters((current) => ({ ...current, [col]: value }));
  };

  const onFilterToggle = (col: string) => {
    setActiveFilterCol((current) => (current === col ? null : col));
  };

  return {
    sortCol,
    sortDir,
    colFilters,
    activeFilterCol,
    handleColSort,
    onFilterChange,
    onFilterToggle,
  };
}
