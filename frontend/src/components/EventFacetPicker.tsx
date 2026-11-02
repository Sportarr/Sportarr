import { useState } from 'react';
import { CheckIcon } from '@heroicons/react/24/outline';
import {
  cellsToChips,
  cellsToRules,
  chipsToRules,
  gridAxes,
  rulesToCells,
  rulesToChips,
  serializeFacetRules,
  type FacetRule,
  type FacetSchema,
} from '../utils/eventFacets';

interface EventFacetPickerProps {
  schema: FacetSchema;
  value: FacetRule[] | null;
  onChange: (rules: FacetRule[] | null) => void;
  editMode?: boolean;
}

const CHIP_ON = 'bg-red-600/20 border-red-600 text-white';
const CHIP_OFF = 'bg-black/30 border-gray-700 text-gray-300 hover:border-gray-600';

const plural = (label: string) => `${label.toLowerCase()}s`;

// One row of chips per dimension saves one rule. The grid saves exact
// combinations, for example men's speed events plus women's slalom.
export default function EventFacetPicker({ schema, value, onChange, editMode = false }: EventFacetPickerProps) {
  // The chips keep their own state while the user edits. An empty row saves
  // [] (no events), and [] cannot tell which chips the other rows had. The
  // draft is used only while the value is still the one it produced.
  const [draft, setDraft] = useState<{ chips: Record<string, Set<string>>; saved: string | null } | null>(null);
  const valueKey = serializeFacetRules(value);
  const chips = draft && draft.saved === valueKey ? draft.chips : rulesToChips(schema, value);

  const axes = gridAxes(schema);
  const [gridChosen, setGridChosen] = useState(false);
  const [confirmWiden, setConfirmWiden] = useState(false);
  // Rules that chips cannot show always open the grid.
  const showGrid = !!axes && (gridChosen || chips === null);
  const cells = rulesToCells(schema, value);

  const saveChips = (next: Record<string, Set<string>>) => {
    const rules = chipsToRules(schema, next);
    setDraft({ chips: next, saved: serializeFacetRules(rules) });
    onChange(rules);
  };

  const setDimension = (key: string, selected: Set<string>) => {
    if (!chips) return;
    saveChips({ ...chips, [key]: selected });
  };

  const toggleChip = (key: string, chipValue: string) => {
    const selected = new Set(chips?.[key] ?? []);
    if (selected.has(chipValue)) selected.delete(chipValue);
    else selected.add(chipValue);
    setDimension(key, selected);
  };

  const setCells = (keys: string[], on: boolean) => {
    const next = new Set(cells);
    for (const key of keys) {
      if (on) next.add(key);
      else next.delete(key);
    }
    setDraft(null);
    onChange(cellsToRules(schema, next));
  };

  const backToChips = () => {
    if (chips === null && !confirmWiden) {
      setConfirmWiden(true);
      return;
    }
    if (chips === null) saveChips(cellsToChips(schema, cells));
    setConfirmWiden(false);
    setGridChosen(false);
  };

  const nothingSelected = value !== null && value.length === 0;

  return (
    <div className="space-y-4">
      {showGrid && axes ? (
        <div className="space-y-3">
          <div className="relative">
            <div className="overflow-x-auto">
              <table className="border-separate border-spacing-1">
                <thead>
                  <tr>
                    <th />
                    {axes.columns.values.map(column => {
                      const keys = axes.rows.values.map(row => axes.key(row, column));
                      const full = keys.every(key => cells.has(key));
                      return (
                        <th key={column} className="px-1 align-bottom">
                          <button
                            type="button"
                            aria-pressed={full}
                            aria-label={`All ${column}`}
                            onClick={() => setCells(keys, !full)}
                            className="whitespace-nowrap text-xs font-medium text-gray-300 hover:text-white"
                          >
                            {column}
                          </button>
                        </th>
                      );
                    })}
                  </tr>
                </thead>
                <tbody>
                  {axes.rows.values.map(row => {
                    const keys = axes.columns.values.map(column => axes.key(row, column));
                    const full = keys.every(key => cells.has(key));
                    return (
                      <tr key={row}>
                        <th scope="row" className="pr-2 text-left">
                          <button
                            type="button"
                            aria-pressed={full}
                            aria-label={`All ${row}`}
                            onClick={() => setCells(keys, !full)}
                            className="whitespace-nowrap text-sm font-medium text-gray-300 hover:text-white"
                          >
                            {row}
                          </button>
                        </th>
                        {axes.columns.values.map(column => {
                          const key = axes.key(row, column);
                          const on = cells.has(key);
                          return (
                            <td key={column} className="text-center">
                              <button
                                type="button"
                                aria-pressed={on}
                                aria-label={axes.label(row, column)}
                                onClick={() => setCells([key], !on)}
                                className={`mx-auto flex h-9 w-9 min-w-9 items-center justify-center rounded border-2 transition-colors ${
                                  on ? 'bg-red-600 border-red-600' : 'border-gray-600 hover:border-gray-500'
                                }`}
                              >
                                {on && <CheckIcon className="h-4 w-4 text-white" />}
                              </button>
                            </td>
                          );
                        })}
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            {/* A fade on the right shows that a wide grid scrolls. */}
            {axes.columns.values.length > 4 && (
              <div
                data-testid="facet-grid-scroll-cue"
                className="pointer-events-none absolute inset-y-0 right-0 w-6 bg-gradient-to-l from-gray-900 to-transparent sm:hidden"
              />
            )}
          </div>
          {confirmWiden ? (
            <div className="space-y-2 rounded-lg border border-yellow-600/40 bg-yellow-600/10 p-3">
              <p className="text-sm text-yellow-200">
                The simple choice cannot hold these exact picks. Every {schema.dimensions[0].label.toLowerCase()} you
                picked will get every {schema.dimensions[1].label.toLowerCase()} you picked.
              </p>
              <div className="flex gap-4">
                <button type="button" onClick={backToChips} className="text-sm text-red-400 hover:text-red-300">
                  Switch anyway
                </button>
                <button type="button" onClick={() => setConfirmWiden(false)} className="text-sm text-gray-400 hover:text-white">
                  Keep exact picks
                </button>
              </div>
            </div>
          ) : (
            <button type="button" onClick={backToChips} className="text-sm text-red-400 hover:text-red-300">
              Back to simple choice
            </button>
          )}
        </div>
      ) : (
        <div className="space-y-3">
          {schema.dimensions.map(dimension => {
            const selected = chips?.[dimension.key] ?? new Set<string>();
            return (
              <div key={dimension.key} className="flex flex-col gap-2 sm:flex-row sm:items-start">
                <span className="text-sm font-medium text-gray-300 sm:w-24 sm:shrink-0 sm:pt-1.5">{dimension.label}</span>
                <div className="flex flex-1 flex-wrap gap-2">
                  {dimension.values.map(chipValue => {
                    const on = selected.has(chipValue);
                    return (
                      <button
                        key={chipValue}
                        type="button"
                        aria-pressed={on}
                        onClick={() => toggleChip(dimension.key, chipValue)}
                        className={`rounded-full border px-3 py-1.5 text-sm transition-colors ${on ? CHIP_ON : CHIP_OFF}`}
                      >
                        {chipValue}
                      </button>
                    );
                  })}
                </div>
                <div className="flex gap-3 sm:pt-1.5">
                  <button
                    type="button"
                    aria-label={`All ${plural(dimension.label)}`}
                    onClick={() => setDimension(dimension.key, new Set(dimension.values))}
                    className="text-xs text-gray-400 hover:text-white"
                  >
                    All
                  </button>
                  <button
                    type="button"
                    aria-label={`No ${plural(dimension.label)}`}
                    onClick={() => setDimension(dimension.key, new Set())}
                    className="text-xs text-gray-400 hover:text-white"
                  >
                    None
                  </button>
                </div>
              </div>
            );
          })}
          {axes && (
            <button type="button" onClick={() => setGridChosen(true)} className="text-sm text-red-400 hover:text-red-300">
              Pick exact combinations
            </button>
          )}
        </div>
      )}

      <p className={`text-xs ${nothingSelected ? 'text-yellow-500' : 'text-gray-400'}`}>
        {nothingSelected
          ? 'Nothing selected. No events will be monitored.'
          : 'Events that match nothing here are not monitored. New events follow the same choice.'}
        {editMode && ' Changes apply to existing events in this league.'}
      </p>
    </div>
  );
}
