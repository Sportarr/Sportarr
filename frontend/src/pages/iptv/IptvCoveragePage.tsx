import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  ArrowPathIcon,
  ExclamationTriangleIcon,
  CheckCircleIcon,
  SignalIcon,
  CalendarDaysIcon,
  ChevronDownIcon,
  ChevronRightIcon,
  StarIcon as StarOutlineIcon,
  LockClosedIcon,
  LockOpenIcon,
  XMarkIcon,
  QuestionMarkCircleIcon,
  BeakerIcon,
} from '@heroicons/react/24/outline';
import { StarIcon as StarSolidIcon } from '@heroicons/react/24/solid';
import { toast } from 'sonner';
import apiClient from '../../api/client';
import PageHeader from '../../components/PageHeader';
import PageShell from '../../components/PageShell';

// Matches the response shape from GET /api/iptv/coverage-report.
// Per-league rollup of channel coverage + upcoming-event scheduling
// coverage, plus a totals summary. Surfaces the new scored-mapping
// data so you can see which leagues are well-covered and which have
// zero channels or zero scheduled recordings.
interface CoverageChannelStats {
  total: number;
  preferred: number;
  manual: number;
  averageConfidence: number;
}

interface CoverageEventStats {
  total: number;
  withRecording: number;
  withFallback: number;
  uncovered: number;
}

interface CoverageLeagueRow {
  leagueId: number;
  leagueName: string;
  sport: string | null;
  country: string | null;
  channels: CoverageChannelStats;
  upcomingEvents: CoverageEventStats;
}

interface CoverageTotals {
  leagues: number;
  leaguesWithAnyChannel: number;
  leaguesWithPreferred: number;
  upcomingEvents: number;
  eventsWithRecording: number;
  eventsWithFallback: number;
  eventsUncovered: number;
}

interface CoverageReport {
  generatedAt: string;
  horizonDays: number;
  totals: CoverageTotals;
  leagues: CoverageLeagueRow[];
}

// Shape returned by GET /api/iptv/leagues/{lid}/mappings — joined
// channel + mapping fields. Drives the expand-row inspector.
interface LeagueChannelMapping {
  mappingId: number;
  channelId: number;
  channelName: string;
  sourceName: string | null;
  country: string | null;
  language: string | null;
  detectedQuality: string | null;
  qualityScore: number;
  detectedNetwork: string | null;
  tvgId: string | null;
  status: string | null;
  isEnabled: boolean;
  isPreferred: boolean;
  isManual: boolean;
  confidence: number;
  priority: number;
  lastAutoMapped: string | null;
}

// Shape returned by GET /api/iptv/channels/{cid}/mappings/{lid}/explain
interface MappingExplain {
  channel: { channelId: number; name: string; country: string | null; tvgId: string | null };
  league: { leagueId: number; name: string; sport: string | null; country: string | null };
  confidence: number;
  isManual: boolean;
  isPreferred: boolean;
  lastAutoMapped: string | null;
  signals: Array<{ kind: string; score: number; detail: string | null }>;
}

// Shape returned by GET /api/iptv/leagues/{lid}/test-resolve. The
// `error` branch comes back when the league has zero events; the
// normal branch carries the sample event + signal inventory +
// ranked candidates + diagnostic hints.
interface TestResolveResponse {
  error?: string;
  message?: string;
  evaluatedEvent?: {
    id: number;
    title: string;
    scheduledStart: string;
    league: string | null;
    broadcast: string | null;
    broadcastIsEmpty: boolean;
  };
  signalsAvailable?: {
    mappedChannels: number;
    enabledChannels: number;
    broadcastString: string | null;
    epgProgramsInWindow: number;
  };
  candidates?: Array<{
    channelId: number;
    channelName: string;
    sourceName: string;
    confidence: number;
    source: string;
    detectedQuality: string | null;
    qualityScore: number;
  }>;
  wouldAutoSchedule?: boolean;
  diagnostics?: string[];
}

type SortKey = 'name' | 'channels' | 'coverage' | 'gap';

export default function IptvCoveragePage() {
  const [report, setReport] = useState<CoverageReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [horizonDays, setHorizonDays] = useState(14);
  const [sortKey, setSortKey] = useState<SortKey>('gap');
  const [filter, setFilter] = useState<'all' | 'uncovered' | 'no_channel'>('all');

  // Expand-row state — track which league rows have been expanded
  // and cache the mappings fetched per league. mappingsByLeague:
  // null = not fetched yet, [] = fetched + empty, [...] = data.
  const [expanded, setExpanded] = useState<Set<number>>(new Set());
  const [mappingsByLeague, setMappingsByLeague] = useState<Record<number, LeagueChannelMapping[] | null>>({});
  const [busyMapping, setBusyMapping] = useState<number | null>(null);

  // Modal state for the "Why is this mapped?" explain popover and
  // for the confirm-before-unmap dialog. Both keyed by the
  // (channelId, leagueId) pair the user clicked on.
  const [explainOpen, setExplainOpen] = useState<MappingExplain | null>(null);
  const [explainLoading, setExplainLoading] = useState(false);
  const [confirmUnmap, setConfirmUnmap] = useState<{ leagueId: number; channelId: number; channelName: string } | null>(null);

  // Test-resolve diagnostic state. Shows what the auto-scheduler would
  // see when it tries to pick a channel for a sample event in the
  // league. Surfaces signal inventory + candidate ranking + plain-
  // English diagnostics so you can answer "why isn't this event auto-
  // scheduling?" in one click.
  const [testResolveOpen, setTestResolveOpen] = useState<{ leagueId: number; leagueName: string; result: TestResolveResponse | null } | null>(null);
  const [testResolveLoading, setTestResolveLoading] = useState(false);

  const runTestResolve = useCallback(async (leagueId: number, leagueName: string) => {
    setTestResolveLoading(true);
    setTestResolveOpen({ leagueId, leagueName, result: null });
    try {
      const { data } = await apiClient.get<TestResolveResponse>(`/iptv/leagues/${leagueId}/test-resolve`);
      setTestResolveOpen({ leagueId, leagueName, result: data });
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Test resolve failed');
      setTestResolveOpen(null);
    } finally {
      setTestResolveLoading(false);
    }
  }, []);

  const load = async (days = horizonDays) => {
    setLoading(true);
    setError(null);
    try {
      const { data } = await apiClient.get<CoverageReport>(`/iptv/coverage-report?days=${days}`);
      setReport(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load coverage report');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Fetch mappings for a league on first expand. Re-fetch when the
  // user explicitly toggles back closed + open or after any per-channel
  // action so the UI reflects the new state.
  const fetchMappings = useCallback(async (leagueId: number) => {
    try {
      const { data } = await apiClient.get<LeagueChannelMapping[]>(`/iptv/leagues/${leagueId}/mappings`);
      setMappingsByLeague((prev) => ({ ...prev, [leagueId]: data }));
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Failed to load channel mappings');
      setMappingsByLeague((prev) => ({ ...prev, [leagueId]: [] }));
    }
  }, []);

  const toggleExpanded = useCallback((leagueId: number) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(leagueId)) {
        next.delete(leagueId);
      } else {
        next.add(leagueId);
        // Fire-and-forget fetch if we haven't loaded it yet.
        if (mappingsByLeague[leagueId] === undefined) {
          void fetchMappings(leagueId);
        }
      }
      return next;
    });
  }, [fetchMappings, mappingsByLeague]);

  // Per-channel actions. Each hits a Phase 4 endpoint, refreshes the
  // expanded mapping list for the affected league, and reloads the
  // top-level report so summary counters stay accurate.
  const togglePreferred = useCallback(async (leagueId: number, channelId: number, currentlyPreferred: boolean) => {
    setBusyMapping(channelId);
    try {
      // The set-preferred endpoint takes the channel id (or null to
      // clear). Clicking the star on the current preferred = clear;
      // clicking on a non-preferred = set.
      await apiClient.post(`/iptv/leagues/${leagueId}/preferred-channel`, {
        channelId: currentlyPreferred ? null : channelId,
      });
      await fetchMappings(leagueId);
      await load();
      toast.success(currentlyPreferred ? 'Preferred channel cleared' : 'Preferred channel set');
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Failed to set preferred channel');
    } finally {
      setBusyMapping(null);
    }
  }, [fetchMappings]);

  const toggleManualLock = useCallback(async (leagueId: number, channelId: number, currentlyManual: boolean) => {
    setBusyMapping(channelId);
    try {
      await apiClient.post(`/iptv/channels/${channelId}/mappings/${leagueId}/manual`, {
        isManual: !currentlyManual,
      });
      await fetchMappings(leagueId);
      await load();
      toast.success(currentlyManual ? 'Manual lock removed' : 'Manual lock applied — auto-mapper will leave this row alone');
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Failed to toggle manual lock');
    } finally {
      setBusyMapping(null);
    }
  }, [fetchMappings]);

  const unmapChannel = useCallback(async (leagueId: number, channelId: number) => {
    setBusyMapping(channelId);
    try {
      await apiClient.delete(`/iptv/channels/${channelId}/mappings/${leagueId}`);
      await fetchMappings(leagueId);
      await load();
      toast.success('Channel unmapped from league');
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Failed to unmap channel');
    } finally {
      setBusyMapping(null);
      setConfirmUnmap(null);
    }
  }, [fetchMappings]);

  const openExplain = useCallback(async (leagueId: number, channelId: number) => {
    setExplainLoading(true);
    setExplainOpen(null);
    try {
      const { data } = await apiClient.get<MappingExplain>(`/iptv/channels/${channelId}/mappings/${leagueId}/explain`);
      setExplainOpen(data);
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Failed to load mapping signals');
    } finally {
      setExplainLoading(false);
    }
  }, []);

  const rows = useMemo(() => {
    if (!report) return [];
    let rs = [...report.leagues];
    if (filter === 'uncovered') {
      rs = rs.filter((r) => r.upcomingEvents.uncovered > 0);
    } else if (filter === 'no_channel') {
      rs = rs.filter((r) => r.channels.total === 0);
    }
    switch (sortKey) {
      case 'name':
        rs.sort((a, b) => a.leagueName.localeCompare(b.leagueName));
        break;
      case 'channels':
        rs.sort((a, b) => b.channels.total - a.channels.total || a.leagueName.localeCompare(b.leagueName));
        break;
      case 'coverage':
        rs.sort((a, b) => {
          const ac = a.upcomingEvents.total === 0 ? -1 : a.upcomingEvents.withRecording / a.upcomingEvents.total;
          const bc = b.upcomingEvents.total === 0 ? -1 : b.upcomingEvents.withRecording / b.upcomingEvents.total;
          return bc - ac;
        });
        break;
      case 'gap':
      default:
        rs.sort((a, b) => b.upcomingEvents.uncovered - a.upcomingEvents.uncovered || b.upcomingEvents.total - a.upcomingEvents.total);
        break;
    }
    return rs;
  }, [report, sortKey, filter]);

  return (
    <PageShell>
      <PageHeader
        title="IPTV Coverage Report"
        subtitle="Per-league channel mapping coverage and scheduled-recording coverage for upcoming events. Use this to find leagues that need a channel mapped or events that the DVR auto-scheduler couldn't resolve."
      />

      {/* Horizon + filter + refresh row */}
      <div className="flex flex-wrap items-center gap-3 mb-6">
        <label className="flex items-center gap-2 text-sm text-gray-400">
          <CalendarDaysIcon className="w-4 h-4" />
          Window:
          <select
            value={horizonDays}
            onChange={(e) => {
              const v = parseInt(e.target.value, 10);
              setHorizonDays(v);
              load(v);
            }}
            className="bg-gray-800 border border-gray-700 text-gray-200 text-sm rounded px-2 py-1 focus:outline-none focus:ring-2 focus:ring-red-500"
          >
            <option value={7}>Next 7 days</option>
            <option value={14}>Next 14 days</option>
            <option value={30}>Next 30 days</option>
            <option value={60}>Next 60 days</option>
            <option value={90}>Next 90 days</option>
          </select>
        </label>

        <label className="flex items-center gap-2 text-sm text-gray-400">
          Show:
          <select
            value={filter}
            onChange={(e) => setFilter(e.target.value as typeof filter)}
            className="bg-gray-800 border border-gray-700 text-gray-200 text-sm rounded px-2 py-1 focus:outline-none focus:ring-2 focus:ring-red-500"
          >
            <option value="all">All leagues</option>
            <option value="uncovered">Has uncovered events</option>
            <option value="no_channel">No mapped channels</option>
          </select>
        </label>

        <label className="flex items-center gap-2 text-sm text-gray-400">
          Sort by:
          <select
            value={sortKey}
            onChange={(e) => setSortKey(e.target.value as SortKey)}
            className="bg-gray-800 border border-gray-700 text-gray-200 text-sm rounded px-2 py-1 focus:outline-none focus:ring-2 focus:ring-red-500"
          >
            <option value="gap">Coverage gap (largest first)</option>
            <option value="coverage">Coverage % (highest first)</option>
            <option value="channels">Channel count (most first)</option>
            <option value="name">League name (A-Z)</option>
          </select>
        </label>

        <button
          onClick={() => load()}
          disabled={loading}
          className="ml-auto flex items-center gap-2 px-3 py-1.5 bg-red-600 hover:bg-red-700 disabled:bg-red-600/50 disabled:cursor-not-allowed text-white text-sm rounded transition-colors"
        >
          <ArrowPathIcon className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          Refresh
        </button>
      </div>

      {error && (
        <div className="bg-red-900/30 border border-red-700 rounded-lg p-4 mb-6">
          <p className="text-red-300">{error}</p>
        </div>
      )}

      {report && (
        <>
          {/* Totals summary */}
          <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-6">
            <SummaryTile
              label="Leagues with channels"
              value={`${report.totals.leaguesWithAnyChannel} / ${report.totals.leagues}`}
              icon={<SignalIcon className="w-5 h-5 text-blue-400" />}
              tone={report.totals.leaguesWithAnyChannel === report.totals.leagues ? 'good' : 'warn'}
            />
            <SummaryTile
              label="Leagues with preferred"
              value={`${report.totals.leaguesWithPreferred} / ${report.totals.leagues}`}
              icon={<CheckCircleIcon className="w-5 h-5 text-green-400" />}
              tone={report.totals.leaguesWithPreferred === report.totals.leagues ? 'good' : 'warn'}
            />
            <SummaryTile
              label="Events covered"
              value={`${report.totals.eventsWithRecording} / ${report.totals.upcomingEvents}`}
              icon={<CalendarDaysIcon className="w-5 h-5 text-purple-400" />}
              tone={report.totals.eventsUncovered === 0 ? 'good' : 'warn'}
            />
            <SummaryTile
              label="Uncovered events"
              value={String(report.totals.eventsUncovered)}
              icon={<ExclamationTriangleIcon className="w-5 h-5 text-amber-400" />}
              tone={report.totals.eventsUncovered === 0 ? 'good' : report.totals.eventsUncovered > 10 ? 'bad' : 'warn'}
            />
          </div>

          {/* Per-league table */}
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden">
            <table className="w-full text-sm">
              <thead className="bg-black/40 text-xs text-gray-400 uppercase tracking-wide">
                <tr>
                  <th className="px-2 py-3 w-8"></th>
                  <th className="px-4 py-3 text-left">League</th>
                  <th className="px-4 py-3 text-left">Sport</th>
                  <th className="px-4 py-3 text-right">Channels</th>
                  <th className="px-4 py-3 text-right">Preferred</th>
                  <th className="px-4 py-3 text-right">Manual</th>
                  <th className="px-4 py-3 text-right">Avg conf.</th>
                  <th className="px-4 py-3 text-right">Events ({report.horizonDays}d)</th>
                  <th className="px-4 py-3 text-right">Scheduled</th>
                  <th className="px-4 py-3 text-right">Uncovered</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-800/50">
                {rows.length === 0 && !loading && (
                  <tr>
                    <td colSpan={10} className="px-4 py-8 text-center text-gray-500">
                      {filter === 'uncovered' ? 'No leagues with uncovered events — nice work.' : 'No leagues to display.'}
                    </td>
                  </tr>
                )}
                {rows.map((row) => {
                  const isOpen = expanded.has(row.leagueId);
                  return (
                    <React.Fragment key={row.leagueId}>
                      <tr
                        className={`hover:bg-gray-800/30 cursor-pointer ${isOpen ? 'bg-gray-800/40' : ''}`}
                        onClick={() => toggleExpanded(row.leagueId)}
                      >
                        <td className="px-2 py-2.5 text-center text-gray-500">
                          {isOpen ? <ChevronDownIcon className="w-4 h-4 inline" /> : <ChevronRightIcon className="w-4 h-4 inline" />}
                        </td>
                        <td className="px-4 py-2.5 text-white font-medium">{row.leagueName}</td>
                        <td className="px-4 py-2.5 text-gray-400">
                          {row.sport || '—'}
                          {row.country && <span className="ml-2 text-xs text-gray-600">{row.country}</span>}
                        </td>
                        <td className="px-4 py-2.5 text-right text-gray-300">{row.channels.total}</td>
                        <td className="px-4 py-2.5 text-right">
                          {row.channels.preferred > 0 ? <span className="text-green-400">{row.channels.preferred}</span> : <span className="text-gray-600">0</span>}
                        </td>
                        <td className="px-4 py-2.5 text-right">
                          {row.channels.manual > 0 ? (
                            <span className="text-blue-400" title="Locked mappings (auto-mapper won't touch these)">🔒 {row.channels.manual}</span>
                          ) : (
                            <span className="text-gray-600">0</span>
                          )}
                        </td>
                        <td className="px-4 py-2.5 text-right text-gray-400">
                          {row.channels.total > 0 ? row.channels.averageConfidence.toFixed(0) : '—'}
                        </td>
                        <td className="px-4 py-2.5 text-right text-gray-300">{row.upcomingEvents.total}</td>
                        <td className="px-4 py-2.5 text-right">
                          {row.upcomingEvents.total === 0 ? (
                            <span className="text-gray-600">—</span>
                          ) : row.upcomingEvents.withRecording === row.upcomingEvents.total ? (
                            <span className="text-green-400">{row.upcomingEvents.withRecording}</span>
                          ) : (
                            <span className="text-amber-400">{row.upcomingEvents.withRecording}</span>
                          )}
                        </td>
                        <td className="px-4 py-2.5 text-right">
                          {row.upcomingEvents.uncovered === 0 ? <span className="text-gray-600">0</span> : <span className="text-red-400 font-medium">{row.upcomingEvents.uncovered}</span>}
                        </td>
                      </tr>
                      {isOpen && (
                        <tr className="bg-black/40">
                          <td colSpan={10} className="px-6 py-4">
                            <LeagueChannelInspector
                              leagueId={row.leagueId}
                              leagueName={row.leagueName}
                              mappings={mappingsByLeague[row.leagueId]}
                              busyMapping={busyMapping}
                              onTogglePreferred={(cid, pref) => togglePreferred(row.leagueId, cid, pref)}
                              onToggleManual={(cid, man) => toggleManualLock(row.leagueId, cid, man)}
                              onUnmap={(cid, name) => setConfirmUnmap({ leagueId: row.leagueId, channelId: cid, channelName: name })}
                              onExplain={(cid) => openExplain(row.leagueId, cid)}
                              onTestResolve={() => runTestResolve(row.leagueId, row.leagueName)}
                            />
                          </td>
                        </tr>
                      )}
                    </React.Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>

          {/* Footer note */}
          <p className="text-xs text-gray-500 mt-4">
            Report generated {new Date(report.generatedAt).toLocaleString()}. "Uncovered" means a monitored future event with no scheduled / recording DVR job — usually because no IPTV channel is mapped to its league, or the event-channel resolver couldn't match it to any mapped channel.
          </p>
        </>
      )}

      {!report && loading && (
        <div className="flex items-center justify-center py-12 text-gray-400">
          <ArrowPathIcon className="w-5 h-5 animate-spin mr-2" />
          Loading coverage report...
        </div>
      )}

      {/* Explain popover — shows the JSON-decoded MappingSignals list
          so you can see exactly why the auto-mapper paired a channel
          with a league. Loaded lazily from the explain endpoint when
          you click the "?" button on a channel. */}
      {(explainOpen || explainLoading) && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70"
             onClick={() => { setExplainOpen(null); }}>
          <div
            className="bg-gradient-to-br from-gray-900 to-black border border-red-900/40 rounded-lg shadow-xl max-w-2xl w-full"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="px-5 py-3 border-b border-red-900/40 flex items-center justify-between">
              <div className="text-white font-semibold">Mapping Explanation</div>
              <button onClick={() => setExplainOpen(null)} className="text-gray-400 hover:text-white">
                <XMarkIcon className="w-5 h-5" />
              </button>
            </div>
            <div className="p-5">
              {explainLoading && (
                <div className="flex items-center gap-2 text-gray-400">
                  <ArrowPathIcon className="w-4 h-4 animate-spin" /> Loading signals...
                </div>
              )}
              {explainOpen && (
                <>
                  <div className="mb-4 text-sm text-gray-300">
                    <span className="text-gray-500">Channel:</span> <span className="text-white">{explainOpen.channel.name}</span>
                    <span className="mx-2 text-gray-700">→</span>
                    <span className="text-gray-500">League:</span> <span className="text-white">{explainOpen.league.name}</span>
                  </div>
                  <div className="grid grid-cols-3 gap-3 mb-4 text-sm">
                    <div className="bg-black/40 border border-gray-800 rounded p-2">
                      <div className="text-xs text-gray-500">Confidence</div>
                      <div className="text-xl font-bold text-white">{explainOpen.confidence}</div>
                    </div>
                    <div className="bg-black/40 border border-gray-800 rounded p-2">
                      <div className="text-xs text-gray-500">Manual lock</div>
                      <div className="text-xl font-bold text-white">{explainOpen.isManual ? '🔒 Yes' : 'No'}</div>
                    </div>
                    <div className="bg-black/40 border border-gray-800 rounded p-2">
                      <div className="text-xs text-gray-500">Preferred</div>
                      <div className="text-xl font-bold text-white">{explainOpen.isPreferred ? '⭐ Yes' : 'No'}</div>
                    </div>
                  </div>
                  <div className="text-xs uppercase tracking-wide text-gray-400 mb-2">Contributing signals</div>
                  {explainOpen.signals.length === 0 ? (
                    <p className="text-sm text-gray-500 italic">No signals recorded for this mapping. It was likely created manually before scored mappings shipped, or via the legacy auto-mapper.</p>
                  ) : (
                    <ul className="space-y-1.5">
                      {explainOpen.signals.map((s, i) => (
                        <li key={i} className="flex items-baseline gap-3 text-sm">
                          <span className="text-gray-500 font-mono text-xs w-20 flex-shrink-0">+{s.score} pts</span>
                          <span className="text-white font-medium">{s.kind}</span>
                          {s.detail && <span className="text-gray-400 truncate">{s.detail}</span>}
                        </li>
                      ))}
                    </ul>
                  )}
                  {explainOpen.lastAutoMapped && (
                    <p className="text-xs text-gray-500 mt-4">
                      Last evaluated by the auto-mapper: {new Date(explainOpen.lastAutoMapped).toLocaleString()}
                    </p>
                  )}
                </>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Test-resolve result modal — shows what the channel resolver
          actually saw when scoring candidates for a sample event in
          the league. Signal inventory at the top tells you which
          ingredients are present / missing; candidate table shows
          every channel that scored above the 65-point minimum with
          its source signal ("epg_program" / "broadcast" / "league-
          mapping"). Diagnostics array spells out what's likely
          missing in plain English. */}
      {testResolveOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70"
             onClick={() => setTestResolveOpen(null)}>
          <div
            className="bg-gradient-to-br from-gray-900 to-black border border-purple-900/50 rounded-lg shadow-xl max-w-3xl w-full max-h-[90vh] overflow-y-auto"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="px-5 py-3 border-b border-purple-900/40 flex items-center justify-between sticky top-0 bg-gradient-to-br from-gray-900 to-black z-10">
              <div className="flex items-center gap-2 text-white font-semibold">
                <BeakerIcon className="w-5 h-5 text-purple-300" />
                Test Resolve — {testResolveOpen.leagueName}
              </div>
              <button onClick={() => setTestResolveOpen(null)} className="text-gray-400 hover:text-white">
                <XMarkIcon className="w-5 h-5" />
              </button>
            </div>
            <div className="p-5">
              {testResolveLoading && (
                <div className="flex items-center gap-2 text-gray-400">
                  <ArrowPathIcon className="w-4 h-4 animate-spin" /> Running resolver against a sample event...
                </div>
              )}
              {!testResolveLoading && testResolveOpen.result && (
                <>
                  {testResolveOpen.result.error === 'no_events' && (
                    <div className="bg-amber-900/30 border border-amber-700/50 rounded p-3 text-sm text-amber-200">
                      {testResolveOpen.result.message}
                    </div>
                  )}

                  {testResolveOpen.result.evaluatedEvent && (
                    <div className="mb-4 text-sm">
                      <div className="text-xs uppercase tracking-wide text-gray-400 mb-1">Evaluated event</div>
                      <div className="bg-black/40 border border-gray-800 rounded p-3 space-y-1">
                        <div className="text-white">{testResolveOpen.result.evaluatedEvent.title}</div>
                        <div className="text-xs text-gray-400">
                          {new Date(testResolveOpen.result.evaluatedEvent.scheduledStart).toLocaleString()}
                        </div>
                        {testResolveOpen.result.evaluatedEvent.broadcast && (
                          <div className="text-xs text-gray-400">
                            <span className="text-gray-500">Broadcast string:</span>{' '}
                            <span className="font-mono text-blue-300">{testResolveOpen.result.evaluatedEvent.broadcast}</span>
                          </div>
                        )}
                        {testResolveOpen.result.evaluatedEvent.broadcastIsEmpty && (
                          <div className="text-xs text-amber-400">⚠ No broadcast string on this event — resolver can't use that signal.</div>
                        )}
                      </div>
                    </div>
                  )}

                  {testResolveOpen.result.signalsAvailable && (
                    <div className="mb-4 text-sm">
                      <div className="text-xs uppercase tracking-wide text-gray-400 mb-1">Signal inventory</div>
                      <div className="grid grid-cols-2 md:grid-cols-4 gap-2">
                        <SignalTile label="Mapped channels" value={testResolveOpen.result.signalsAvailable.mappedChannels} ok={testResolveOpen.result.signalsAvailable.mappedChannels > 0} />
                        <SignalTile label="Enabled channels" value={testResolveOpen.result.signalsAvailable.enabledChannels} ok={testResolveOpen.result.signalsAvailable.enabledChannels > 0} />
                        <SignalTile label="Broadcast tokens" value={testResolveOpen.result.signalsAvailable.broadcastString ? 'present' : 'none'} ok={!!testResolveOpen.result.signalsAvailable.broadcastString} />
                        <SignalTile label="EPG programs in ±30m" value={testResolveOpen.result.signalsAvailable.epgProgramsInWindow} ok={testResolveOpen.result.signalsAvailable.epgProgramsInWindow > 0} />
                      </div>
                    </div>
                  )}

                  {testResolveOpen.result.candidates && testResolveOpen.result.candidates.length > 0 ? (
                    <div className="mb-4">
                      <div className="text-xs uppercase tracking-wide text-gray-400 mb-1">
                        {testResolveOpen.result.candidates.length} candidate{testResolveOpen.result.candidates.length === 1 ? '' : 's'}{' '}
                        {testResolveOpen.result.wouldAutoSchedule
                          ? <span className="text-green-400">— would auto-schedule ✓</span>
                          : <span className="text-amber-400">— top candidate below high-confidence threshold (85), would NOT auto-schedule</span>
                        }
                      </div>
                      <table className="w-full text-sm bg-black/40 border border-gray-800 rounded overflow-hidden">
                        <thead className="bg-black/60 text-xs text-gray-400 uppercase">
                          <tr>
                            <th className="px-3 py-2 text-left">Channel</th>
                            <th className="px-3 py-2 text-left">Source</th>
                            <th className="px-3 py-2 text-left">Signal</th>
                            <th className="px-3 py-2 text-right">Confidence</th>
                            <th className="px-3 py-2 text-right">Quality</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-800/50">
                          {testResolveOpen.result.candidates.map((c, i) => (
                            <tr key={c.channelId} className={i === 0 ? 'bg-green-900/15' : ''}>
                              <td className="px-3 py-2 text-white">
                                {i === 0 && <span className="text-green-400 mr-1">★</span>}
                                {c.channelName}
                              </td>
                              <td className="px-3 py-2 text-gray-400">{c.sourceName}</td>
                              <td className="px-3 py-2">
                                <span className={`text-xs px-2 py-0.5 rounded ${
                                  c.source === 'epg_program' ? 'bg-purple-900/40 text-purple-300' :
                                  c.source === 'broadcast' ? 'bg-blue-900/40 text-blue-300' :
                                  'bg-gray-700/50 text-gray-300'
                                }`}>
                                  {c.source}
                                </span>
                              </td>
                              <td className="px-3 py-2 text-right font-mono text-white">{c.confidence}</td>
                              <td className="px-3 py-2 text-right text-gray-400">{c.detectedQuality || '—'}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : testResolveOpen.result.evaluatedEvent && (
                    <div className="mb-4 bg-red-900/20 border border-red-700/40 rounded p-3 text-sm text-red-200">
                      <strong>Zero candidates scored above the 65-point minimum.</strong> The auto-scheduler would not schedule this event.
                    </div>
                  )}

                  {testResolveOpen.result.diagnostics && testResolveOpen.result.diagnostics.length > 0 && (
                    <div>
                      <div className="text-xs uppercase tracking-wide text-gray-400 mb-1">Diagnostics</div>
                      <ul className="space-y-1.5">
                        {testResolveOpen.result.diagnostics.map((d, i) => (
                          <li key={i} className="flex items-start gap-2 text-sm text-amber-200">
                            <ExclamationTriangleIcon className="w-4 h-4 mt-0.5 flex-shrink-0 text-amber-400" />
                            <span>{d}</span>
                          </li>
                        ))}
                      </ul>
                    </div>
                  )}
                </>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Confirm-unmap dialog — DELETE is destructive enough to deserve
          a guard. Auto-mapped rows that get re-deleted will stay gone
          until the next auto-map run re-derives them above threshold;
          the dialog spells this out so the choice is informed. */}
      {confirmUnmap && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70"
             onClick={() => setConfirmUnmap(null)}>
          <div
            className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg shadow-xl max-w-md w-full p-5"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="text-white font-semibold mb-2">Unmap channel?</div>
            <p className="text-sm text-gray-400 mb-4">
              Remove <span className="text-white">{confirmUnmap.channelName}</span> from this league's mapping list?
              Auto-mapped rows can be re-derived on the next auto-mapping sweep if signals still point here.
              For a permanent "do not map" decision, toggle Manual lock + Unmap in succession.
            </p>
            <div className="flex justify-end gap-2">
              <button onClick={() => setConfirmUnmap(null)} className="px-3 py-1.5 text-sm bg-gray-700 hover:bg-gray-600 text-white rounded">
                Cancel
              </button>
              <button
                onClick={() => unmapChannel(confirmUnmap.leagueId, confirmUnmap.channelId)}
                disabled={busyMapping === confirmUnmap.channelId}
                className="px-3 py-1.5 text-sm bg-red-600 hover:bg-red-700 disabled:opacity-60 text-white rounded"
              >
                {busyMapping === confirmUnmap.channelId ? 'Unmapping...' : 'Unmap'}
              </button>
            </div>
          </div>
        </div>
      )}
    </PageShell>
  );
}

/**
 * Inline panel that renders when a league row is expanded. Lists every
 * channel mapped to the league with badges (preferred ⭐ / manual 🔒 /
 * confidence %) and four per-channel actions: preferred toggle, manual
 * lock toggle, unmap (with confirm), and explain ("?" → popover with
 * decoded MappingSignals). Loading + empty + populated states are all
 * handled inline.
 */
function LeagueChannelInspector({
  leagueId,
  leagueName,
  mappings,
  busyMapping,
  onTogglePreferred,
  onToggleManual,
  onUnmap,
  onExplain,
  onTestResolve,
}: {
  leagueId: number;
  leagueName: string;
  mappings: LeagueChannelMapping[] | null | undefined;
  busyMapping: number | null;
  onTogglePreferred: (channelId: number, currentlyPreferred: boolean) => void;
  onToggleManual: (channelId: number, currentlyManual: boolean) => void;
  onUnmap: (channelId: number, channelName: string) => void;
  onExplain: (channelId: number) => void;
  onTestResolve: () => void;
}) {
  if (mappings === undefined || mappings === null) {
    return (
      <div className="text-sm text-gray-400 flex items-center gap-2">
        <ArrowPathIcon className="w-4 h-4 animate-spin" />
        Loading channels mapped to {leagueName}...
      </div>
    );
  }
  if (mappings.length === 0) {
    return (
      <div className="space-y-2">
        <p className="text-sm text-gray-500 italic">
          No channels are mapped to this league. Run auto-mapping from the Channels page or manually map a channel.
        </p>
        <button
          onClick={onTestResolve}
          className="flex items-center gap-1.5 px-2.5 py-1 text-xs bg-purple-900/40 hover:bg-purple-900/60 border border-purple-700/50 text-purple-200 rounded transition-colors"
          title="Run the channel resolver against a sample event in this league to confirm what's missing."
        >
          <BeakerIcon className="w-3.5 h-3.5" />
          Test resolve
        </button>
      </div>
    );
  }
  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between mb-2">
        <div className="text-xs uppercase tracking-wide text-gray-400">
          {mappings.length} channel{mappings.length === 1 ? '' : 's'} mapped to {leagueName} (sorted by preferred → confidence)
        </div>
        <button
          onClick={onTestResolve}
          className="flex items-center gap-1.5 px-2.5 py-1 text-xs bg-purple-900/40 hover:bg-purple-900/60 border border-purple-700/50 text-purple-200 rounded transition-colors"
          title="Run the channel resolver against a sample event in this league and show every signal it considered. Use this when auto-scheduling isn't picking events up and you want to know why."
        >
          <BeakerIcon className="w-3.5 h-3.5" />
          Test resolve
        </button>
      </div>
      <div className="grid grid-cols-1 gap-1.5">
        {mappings.map((m) => {
          const isBusy = busyMapping === m.channelId;
          const offline = m.status === 'Offline' || m.status === 'Error' || !m.isEnabled;
          // Identifier for `key` — leagueId pinned in case a channel
          // accidentally ends up in two leagues during a transitional
          // state, this still produces a unique key per row.
          return (
            <div
              key={`${leagueId}-${m.channelId}`}
              className={`flex items-center gap-3 px-3 py-2 rounded ${m.isPreferred ? 'bg-green-900/15 border border-green-700/30' : 'bg-gray-900/40 border border-gray-800'}`}
            >
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="text-white font-medium truncate">{m.channelName}</span>
                  {offline && (
                    <span className="text-[10px] uppercase px-1.5 py-0.5 bg-red-900/40 text-red-300 rounded">offline</span>
                  )}
                  {m.detectedQuality && (
                    <span className="text-[10px] uppercase px-1.5 py-0.5 bg-blue-900/30 text-blue-300 rounded">{m.detectedQuality}</span>
                  )}
                  {m.isManual && (
                    <span className="text-[10px] uppercase px-1.5 py-0.5 bg-blue-900/40 text-blue-200 rounded flex items-center gap-0.5">
                      🔒 Locked
                    </span>
                  )}
                  <span className="text-[10px] uppercase tracking-wide text-gray-500">conf {m.confidence}</span>
                </div>
                <div className="text-xs text-gray-500 mt-0.5 truncate">
                  {m.sourceName || 'Unknown source'}
                  {m.country && ` · ${m.country}`}
                  {m.detectedNetwork && ` · ${m.detectedNetwork}`}
                </div>
              </div>
              <div className="flex items-center gap-1 flex-shrink-0">
                <button
                  onClick={() => onTogglePreferred(m.channelId, m.isPreferred)}
                  disabled={isBusy}
                  title={m.isPreferred ? 'Clear preferred (auto-select best)' : 'Set as preferred channel for this league'}
                  className={`p-1.5 rounded transition-colors ${m.isPreferred ? 'bg-yellow-600/30 hover:bg-yellow-600/50 text-yellow-300' : 'bg-gray-800 hover:bg-gray-700 text-gray-400 hover:text-yellow-300'} disabled:opacity-50`}
                >
                  {m.isPreferred ? <StarSolidIcon className="w-4 h-4" /> : <StarOutlineIcon className="w-4 h-4" />}
                </button>
                <button
                  onClick={() => onToggleManual(m.channelId, m.isManual)}
                  disabled={isBusy}
                  title={m.isManual ? 'Manual lock — auto-mapper leaves this row alone. Click to unlock.' : 'Lock this mapping — auto-mapper won\'t touch it on future re-runs.'}
                  className={`p-1.5 rounded transition-colors ${m.isManual ? 'bg-blue-600/30 hover:bg-blue-600/50 text-blue-300' : 'bg-gray-800 hover:bg-gray-700 text-gray-400 hover:text-blue-300'} disabled:opacity-50`}
                >
                  {m.isManual ? <LockClosedIcon className="w-4 h-4" /> : <LockOpenIcon className="w-4 h-4" />}
                </button>
                <button
                  onClick={() => onExplain(m.channelId)}
                  disabled={isBusy}
                  title="Why is this channel mapped to this league? (Show contributing signals)"
                  className="p-1.5 rounded bg-gray-800 hover:bg-gray-700 text-gray-400 hover:text-white transition-colors disabled:opacity-50"
                >
                  <QuestionMarkCircleIcon className="w-4 h-4" />
                </button>
                <button
                  onClick={() => onUnmap(m.channelId, m.channelName)}
                  disabled={isBusy}
                  title="Unmap this channel from the league"
                  className="p-1.5 rounded bg-gray-800 hover:bg-red-700/50 text-gray-400 hover:text-red-300 transition-colors disabled:opacity-50"
                >
                  <XMarkIcon className="w-4 h-4" />
                </button>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function SignalTile({ label, value, ok }: { label: string; value: number | string; ok: boolean }) {
  return (
    <div className={`bg-black/40 border ${ok ? 'border-green-700/40' : 'border-amber-700/40'} rounded p-2`}>
      <div className="text-[10px] uppercase tracking-wide text-gray-500">{label}</div>
      <div className={`text-lg font-bold ${ok ? 'text-green-400' : 'text-amber-400'}`}>
        {value}
      </div>
    </div>
  );
}

function SummaryTile({
  label,
  value,
  icon,
  tone,
}: {
  label: string;
  value: string;
  icon: React.ReactNode;
  tone: 'good' | 'warn' | 'bad';
}) {
  const toneClass = tone === 'good' ? 'border-green-700/40' : tone === 'bad' ? 'border-red-700/50' : 'border-amber-700/40';
  return (
    <div className={`bg-gradient-to-br from-gray-900 to-black border ${toneClass} rounded-lg p-4`}>
      <div className="flex items-center gap-2 mb-1">
        {icon}
        <span className="text-xs uppercase tracking-wide text-gray-400">{label}</span>
      </div>
      <div className="text-2xl font-bold text-white">{value}</div>
    </div>
  );
}
