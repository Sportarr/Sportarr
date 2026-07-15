import { useEffect, useState } from 'react';
import { XMarkIcon } from '@heroicons/react/24/outline';
import apiClient from '../api/client';

/**
 * Shared editor component for EventFile metadata. Used in five places with
 * identical look and behavior:
 *   1. Post-import per-row pencil edit (Files panel — per-event)
 *   2. Post-import per-row pencil edit (Files panel — per-league)
 *   3. ManualImportModal pre-import editing
 *   4. LibraryImportPage pre-import editing
 *   5. ActivityPage PendingImport pre-import editing (via ManualImportModal)
 *
 * Controlled component: parent owns `value` and `onChange`. Native <select>
 * elements match Sportarr's existing modal dropdown style (gray-800 with
 * red/blue focus ring). Selects show all canonical options at once; values
 * outside the canonical list are preserved by appending them as a one-off
 * top option so the user's existing data is never lost or hidden.
 *
 * Field set mirrors Sonarr's bulk file editor (Quality, ReleaseGroup, Languages,
 * IndexerFlags, plus Sportarr's Codec/Source/PartName/PartNumber/OriginalTitle).
 */

export interface FileMetadataEditorValues {
  quality?: string;
  source?: string;
  codec?: string;
  releaseGroup?: string;
  originalTitle?: string;
  languages?: string[];
  indexerFlags?: string;
  partName?: string;
  partNumber?: number | null;
}

export interface FileMetadataEditorProps {
  value: FileMetadataEditorValues;
  onChange: (next: FileMetadataEditorValues) => void;
  /** Hide a subset of fields (e.g. PartName when the event is single-part). */
  hideFields?: Array<keyof FileMetadataEditorValues>;
  /** Disable all inputs (read-only mode). */
  disabled?: boolean;
  /** Optional context — drives league-aware part-name and part-number options. */
  leagueId?: number;
  eventId?: number;
}

interface KnownLists {
  qualities: string[];
  sources: string[];
  codecs: string[];
  indexerFlags: string[];
  releaseGroups: string[];
  parts: string[];
  maxPartNumber: number;
}

// Per-context cache so the same editor opened on a different event doesn't
// reuse the previous event's part list.
const knownListsCache = new Map<string, KnownLists>();
const knownListsInflight = new Map<string, Promise<KnownLists>>();

const FALLBACK_LISTS: KnownLists = {
  qualities: [
    'Unknown', 'SDTV', 'DVD',
    'WEBDL-480p', 'WEBRip-480p', 'Bluray-480p',
    'HDTV-720p', 'WEBDL-720p', 'WEBRip-720p', 'Bluray-720p',
    'HDTV-1080p', 'WEBDL-1080p', 'WEBRip-1080p', 'Bluray-1080p', 'Bluray-1080p Remux',
    'HDTV-2160p', 'WEBDL-2160p', 'WEBRip-2160p', 'Bluray-2160p', 'Bluray-2160p Remux',
    'Raw-HD',
  ],
  sources: ['WEBDL', 'WEB-DL', 'WEBRip', 'BLURAY', 'Blu-Ray', 'BDRip', 'HDTV', 'PDTV', 'DVDRIP', 'DVD', 'RAWHD'],
  codecs: ['x264', 'H.264', 'AVC', 'x265', 'H.265', 'HEVC', 'AV1', 'VP9', 'MPEG2', 'XviD', 'DivX'],
  indexerFlags: ['Freeleech', 'Halfleech', 'Internal', 'Scene', 'Nuked', 'DoubleUpload'],
  releaseGroups: [],
  parts: [],
  maxPartNumber: 0,
};

async function fetchKnownLists(leagueId?: number, eventId?: number): Promise<KnownLists> {
  const key = `${leagueId ?? ''}|${eventId ?? ''}`;
  const cached = knownListsCache.get(key);
  if (cached) return cached;
  const inflight = knownListsInflight.get(key);
  if (inflight) return inflight;
  const params: Record<string, number> = {};
  if (leagueId) params.leagueId = leagueId;
  if (eventId) params.eventId = eventId;
  const promise = apiClient
    .get<KnownLists>('/event-files/known-qualities', { params })
    .then((res) => {
      knownListsCache.set(key, res.data);
      return res.data;
    })
    .catch(() => {
      knownListsCache.set(key, FALLBACK_LISTS);
      return FALLBACK_LISTS;
    });
  knownListsInflight.set(key, promise);
  return promise;
}

const COMMON_LANGUAGES = [
  'English', 'Spanish', 'French', 'German', 'Italian', 'Portuguese',
  'Japanese', 'Korean', 'Chinese', 'Russian', 'Arabic', 'Hindi',
  'Dutch', 'Polish', 'Turkish', 'Swedish', 'Norwegian', 'Danish',
];

// Sentinel used by the Quality/Source/Codec selects to switch into custom-text mode.
const CUSTOM_OPTION = '__custom__';
const NO_VALUE = '__none__';

export default function FileMetadataEditor({
  value,
  onChange,
  hideFields = [],
  disabled = false,
  leagueId,
  eventId,
}: FileMetadataEditorProps) {
  const cacheKey = `${leagueId ?? ''}|${eventId ?? ''}`;
  const [lists, setLists] = useState<KnownLists | null>(
    knownListsCache.get(cacheKey) ?? FALLBACK_LISTS,
  );
  const [languageToAdd, setLanguageToAdd] = useState('');
  const [flagsList, setFlagsList] = useState<string[]>(() =>
    splitFlags(value.indexerFlags));

  // "Custom" mode for the closed-list selects. Once enabled, render a free-text
  // input next to the select so the user can type a value the canonical list
  // doesn't include without losing their place.
  const [customQuality, setCustomQuality] = useState(false);
  const [customSource, setCustomSource] = useState(false);
  const [customCodec, setCustomCodec] = useState(false);
  const [customReleaseGroup, setCustomReleaseGroup] = useState(false);
  const [customPartName, setCustomPartName] = useState(false);

  useEffect(() => {
    fetchKnownLists(leagueId, eventId).then(setLists);
  }, [leagueId, eventId]);

  // Keep flagsList in sync if value.indexerFlags changes externally.
  useEffect(() => {
    setFlagsList(splitFlags(value.indexerFlags));
  }, [value.indexerFlags]);

  const hidden = (k: keyof FileMetadataEditorValues) => hideFields.includes(k);

  const update = (patch: Partial<FileMetadataEditorValues>) =>
    onChange({ ...value, ...patch });

  const addLanguage = (lang: string) => {
    const trimmed = lang.trim();
    if (!trimmed) return;
    const current = value.languages ?? [];
    if (current.some((l) => l.toLowerCase() === trimmed.toLowerCase())) return;
    update({ languages: [...current, trimmed] });
    setLanguageToAdd('');
  };

  const removeLanguage = (lang: string) => {
    const current = value.languages ?? [];
    update({ languages: current.filter((l) => l !== lang) });
  };

  const toggleFlag = (flag: string) => {
    const has = flagsList.includes(flag);
    const next = has ? flagsList.filter((f) => f !== flag) : [...flagsList, flag];
    setFlagsList(next);
    update({ indexerFlags: next.length ? next.join(', ') : '' });
  };

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      {!hidden('quality') && (
        <SelectField
          label="Quality"
          value={value.quality}
          options={lists?.qualities ?? FALLBACK_LISTS.qualities}
          onChange={(v) => update({ quality: v })}
          disabled={disabled}
          customMode={customQuality}
          onCustomToggle={setCustomQuality}
          allowEmpty
        />
      )}

      {!hidden('source') && (
        <SelectField
          label="Source"
          value={value.source}
          options={lists?.sources ?? FALLBACK_LISTS.sources}
          onChange={(v) => update({ source: v })}
          disabled={disabled}
          customMode={customSource}
          onCustomToggle={setCustomSource}
          allowEmpty
        />
      )}

      {!hidden('codec') && (
        <SelectField
          label="Video Codec"
          value={value.codec}
          options={lists?.codecs ?? FALLBACK_LISTS.codecs}
          onChange={(v) => update({ codec: v })}
          disabled={disabled}
          customMode={customCodec}
          onCustomToggle={setCustomCodec}
          allowEmpty
        />
      )}

      {!hidden('releaseGroup') && (
        (lists?.releaseGroups?.length ?? 0) > 0 ? (
          <SelectField
            label="Release Group"
            value={value.releaseGroup}
            options={lists?.releaseGroups ?? []}
            onChange={(v) => update({ releaseGroup: v })}
            disabled={disabled}
            customMode={customReleaseGroup}
            onCustomToggle={setCustomReleaseGroup}
            allowEmpty
          />
        ) : (
          // Empty library — no DB-known groups yet, fall back to free text.
          <Field label="Release Group">
            <input
              type="text"
              className={inputClass(disabled)}
              value={value.releaseGroup ?? ''}
              onChange={(e) => update({ releaseGroup: e.target.value })}
              placeholder="GROUP, NTb, FLUX…"
              disabled={disabled}
            />
          </Field>
        )
      )}

      {!hidden('originalTitle') && (
        <Field label="Original Release Title" full>
          <input
            type="text"
            className={inputClass(disabled)}
            value={value.originalTitle ?? ''}
            onChange={(e) => update({ originalTitle: e.target.value })}
            placeholder="EPL.2026.05.02.match.1080p.WEB-DL.x264-GROUP"
            disabled={disabled}
          />
        </Field>
      )}

      {!hidden('languages') && (
        <Field label="Languages" full>
          <div className="flex flex-col gap-2">
            <div className="flex flex-wrap gap-2 min-h-[1.75rem]">
              {(value.languages ?? []).map((l) => (
                <span
                  key={l}
                  className="inline-flex items-center gap-1 rounded bg-emerald-900/40 text-emerald-200 text-xs px-2 py-1 border border-emerald-700/40"
                >
                  {l}
                  {!disabled && (
                    <button
                      type="button"
                      onClick={() => removeLanguage(l)}
                      className="ml-1 hover:text-white"
                      aria-label={`Remove ${l}`}
                    >
                      <XMarkIcon className="w-3 h-3" />
                    </button>
                  )}
                </span>
              ))}
              {(value.languages ?? []).length === 0 && (
                <span className="text-gray-500 text-sm italic">No languages set</span>
              )}
            </div>
            {!disabled && (
              <div className="flex gap-2">
                <select
                  className={inputClass(disabled) + ' flex-1'}
                  value={languageToAdd}
                  onChange={(e) => {
                    if (e.target.value === '') {
                      setLanguageToAdd('');
                    } else if (e.target.value === CUSTOM_OPTION) {
                      setLanguageToAdd('');
                    } else {
                      addLanguage(e.target.value);
                    }
                  }}
                >
                  <option value="">Add a language…</option>
                  {COMMON_LANGUAGES.filter(
                    (l) => !(value.languages ?? []).some((existing) => existing.toLowerCase() === l.toLowerCase()),
                  ).map((l) => (
                    <option key={l} value={l}>{l}</option>
                  ))}
                </select>
                <input
                  type="text"
                  className={inputClass(disabled) + ' flex-1'}
                  value={languageToAdd}
                  placeholder="…or type a custom one"
                  onChange={(e) => setLanguageToAdd(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      e.preventDefault();
                      addLanguage(languageToAdd);
                    }
                  }}
                />
                <button
                  type="button"
                  onClick={() => addLanguage(languageToAdd)}
                  disabled={!languageToAdd.trim()}
                  className="px-4 py-2 rounded-lg bg-blue-700 hover:bg-blue-600 text-white text-sm disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Add
                </button>
              </div>
            )}
          </div>
        </Field>
      )}

      {!hidden('indexerFlags') && (
        <Field label="Indexer Flags" full>
          <div className="flex flex-wrap gap-2">
            {(lists?.indexerFlags ?? FALLBACK_LISTS.indexerFlags).map((flag) => {
              const active = flagsList.includes(flag);
              return (
                <button
                  key={flag}
                  type="button"
                  disabled={disabled}
                  onClick={() => toggleFlag(flag)}
                  className={
                    'px-3 py-1.5 rounded-lg text-xs border transition-colors ' +
                    (active
                      ? 'bg-blue-700 border-blue-500 text-white'
                      : 'bg-gray-800 border-gray-600 text-gray-300 hover:bg-gray-700 hover:border-gray-500') +
                    (disabled ? ' opacity-50 cursor-not-allowed' : '')
                  }
                >
                  {flag}
                </button>
              );
            })}
          </div>
        </Field>
      )}

      {!hidden('partName') && (
        (lists?.parts?.length ?? 0) > 0 ? (
          // League-specific parts (UFC: Early Prelims/Prelims/Main Card,
          // ONE: Lead Card/Main Card, etc). For leagues without defined
          // segments the fall-through renders a plain text input.
          <SelectField
            label="Part Name"
            value={value.partName}
            options={lists?.parts ?? []}
            onChange={(v) => {
              // When the user picks a known part, also auto-set PartNumber
              // from the segment's index in the canonical list (1-based).
              const parts = lists?.parts ?? [];
              const idx = v ? parts.findIndex((p) => p.toLowerCase() === v.toLowerCase()) : -1;
              if (idx >= 0) {
                onChange({ ...value, partName: v, partNumber: idx + 1 });
              } else {
                update({ partName: v });
              }
            }}
            disabled={disabled}
            customMode={customPartName}
            onCustomToggle={setCustomPartName}
            allowEmpty
          />
        ) : (
          <Field label="Part Name">
            <input
              type="text"
              className={inputClass(disabled)}
              value={value.partName ?? ''}
              onChange={(e) => update({ partName: e.target.value })}
              placeholder="No multi-part segments defined for this league"
              disabled={disabled}
            />
          </Field>
        )
      )}

      {!hidden('partNumber') && (
        (lists?.maxPartNumber ?? 0) > 0 ? (
          <Field label="Part Number">
            <select
              className={inputClass(disabled)}
              value={value.partNumber ?? ''}
              onChange={(e) =>
                update({ partNumber: e.target.value === '' ? null : parseInt(e.target.value, 10) })
              }
              disabled={disabled}
            >
              <option value="">— not set —</option>
              {Array.from({ length: lists?.maxPartNumber ?? 0 }, (_, i) => i + 1).map((n) => (
                <option key={n} value={n}>{n}</option>
              ))}
            </select>
          </Field>
        ) : (
          <Field label="Part Number">
            <input
              type="number"
              min={1}
              className={inputClass(disabled)}
              value={value.partNumber ?? ''}
              onChange={(e) =>
                update({ partNumber: e.target.value === '' ? null : parseInt(e.target.value, 10) })
              }
              placeholder="No multi-part segments defined for this league"
              disabled={disabled}
            />
          </Field>
        )
      )}
    </div>
  );
}

// ---------- internal building blocks -----------------------------------------

function Field({
  label,
  children,
  full,
}: {
  label: string;
  children: React.ReactNode;
  full?: boolean;
}) {
  return (
    <div className={full ? 'md:col-span-2' : ''}>
      <label className="block text-xs uppercase tracking-wide text-gray-400 mb-1.5 font-medium">
        {label}
      </label>
      {children}
    </div>
  );
}

function inputClass(disabled: boolean) {
  return (
    'w-full rounded-lg bg-gray-800 border border-gray-600 text-white px-4 py-2 text-sm ' +
    'focus:outline-none focus:ring-2 focus:ring-red-500 focus:border-red-500 ' +
    'placeholder-gray-500 ' +
    (disabled ? 'opacity-60 cursor-not-allowed' : '')
  );
}

/**
 * Closed-list select with a "Custom…" escape hatch.
 *
 * Two display modes:
 * - dropdown (default): native <select> showing canonical options + the
 *   current value (added at the top if non-canonical, e.g. "WEB-DL" when
 *   the canonical list has "WEBDL"). User picks one and that becomes the
 *   stored value verbatim.
 * - custom: free-text input, entered when the user selects "Custom…" from
 *   the dropdown. "List…" button next to it returns to dropdown mode.
 *
 * `customMode` is purely user-controlled: it's only set true when the user
 * clicks "Custom…", and false when the user clicks "List…". The component
 * does NOT auto-flip into custom mode just because the stored value isn't
 * in the canonical list — the user-value-as-extra-option pattern handles
 * that case while keeping the UI predictable.
 */
function SelectField({
  label,
  value,
  options,
  onChange,
  disabled,
  customMode,
  onCustomToggle,
  allowEmpty,
}: {
  label: string;
  value: string | undefined;
  options: string[];
  onChange: (v: string | undefined) => void;
  disabled: boolean;
  customMode: boolean;
  onCustomToggle: (b: boolean) => void;
  allowEmpty?: boolean;
}) {
  // Augment the option list with the current value when it isn't already in
  // the canonical list. Means a file imported with Source="WEB-DL" still has
  // "WEB-DL" as a visible/selectable dropdown option even though the canonical
  // form is "WEBDL". Comparison is case-insensitive to also keep "BluRay" /
  // "BLURAY" / "bluray" from being shown twice.
  const inCanonical = value != null && value !== '' && options.some((o) => o.toLowerCase() === value.toLowerCase());
  const augmentedOptions = (value && !inCanonical) ? [value, ...options] : options;

  return (
    <Field label={label}>
      <div className="flex gap-2">
        {customMode ? (
          <>
            <input
              type="text"
              className={inputClass(disabled)}
              value={value ?? ''}
              onChange={(e) => onChange(e.target.value || undefined)}
              placeholder={`Custom ${label.toLowerCase()}`}
              disabled={disabled}
              autoFocus
            />
            <button
              type="button"
              onClick={() => onCustomToggle(false)}
              disabled={disabled}
              className="px-3 py-2 rounded-lg bg-gray-700 hover:bg-gray-600 text-white text-xs whitespace-nowrap"
              title="Switch back to dropdown"
            >
              List…
            </button>
          </>
        ) : (
          <select
            className={inputClass(disabled)}
            value={value ?? (allowEmpty ? NO_VALUE : '')}
            onChange={(e) => {
              if (e.target.value === CUSTOM_OPTION) {
                onCustomToggle(true);
                return;
              }
              if (e.target.value === NO_VALUE) {
                onChange(undefined);
                return;
              }
              onChange(e.target.value);
            }}
            disabled={disabled}
          >
            {allowEmpty && <option value={NO_VALUE}>— not set —</option>}
            {augmentedOptions.map((o) => (
              <option key={o} value={o}>{o}</option>
            ))}
            <option value={CUSTOM_OPTION}>Custom…</option>
          </select>
        )}
      </div>
    </Field>
  );
}

// ---------- helpers ----------------------------------------------------------

function splitFlags(flags?: string | null): string[] {
  if (!flags) return [];
  return flags
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);
}
