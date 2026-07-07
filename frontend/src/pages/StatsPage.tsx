import { useEffect, useState } from 'react';
import { apiGet } from '../utils/api';
import PageHeader from '../components/PageHeader';
import PageShell, { PageErrorState, PageLoadingState } from '../components/PageShell';

interface LeagueSize {
  league: string;
  files: number;
  sizeBytes: number;
}

interface LeagueCoverage {
  league: string;
  events: number;
  monitored: number;
  withFile: number;
}

interface QualityCount {
  quality: string;
  files: number;
  sizeBytes: number;
}

interface RecordingStatusCount {
  status: string;
  count: number;
}

interface LibraryStats {
  totalSizeBytes: number;
  perLeague: LeagueSize[];
  coverage: LeagueCoverage[];
  byQuality: QualityCount[];
  recordingsByStatus: RecordingStatusCount[];
}

function formatBytes(bytes: number): string {
  if (bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  return `${(bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

export default function StatsPage() {
  const [stats, setStats] = useState<LibraryStats | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const response = await apiGet('/api/stats/library');
        if (response.ok) {
          setStats(await response.json());
        } else {
          setError(true);
        }
      } catch {
        setError(true);
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  if (loading) {
    return <PageLoadingState label="Loading statistics..." />;
  }

  if (error || !stats) {
    return <PageErrorState message="Failed to load statistics" />;
  }

  const coverageByLeague = new Map(stats.coverage.map((c) => [c.league, c]));
  const maxLeagueSize = Math.max(1, ...stats.perLeague.map((l) => l.sizeBytes));
  const totalRecordings = stats.recordingsByStatus.reduce((sum, r) => sum + r.count, 0);

  return (
    <PageShell>
      <PageHeader title="Statistics" subtitle={`Library size: ${formatBytes(stats.totalSizeBytes)}`} />

      <div className="grid gap-6 lg:grid-cols-2">
        <div className="rounded-lg border border-gray-800 bg-gray-900/50 p-4">
          <h2 className="mb-3 text-lg font-semibold text-white">Disk Usage by League</h2>
          {stats.perLeague.length === 0 ? (
            <p className="text-sm text-gray-500">No files in the library yet.</p>
          ) : (
            <div className="space-y-3">
              {stats.perLeague.map((l) => (
                <div key={l.league}>
                  <div className="mb-1 flex items-baseline justify-between text-sm">
                    <span className="text-gray-200">{l.league}</span>
                    <span className="text-gray-400">
                      {formatBytes(l.sizeBytes)} <span className="text-gray-600">({l.files} files)</span>
                    </span>
                  </div>
                  <div className="h-2 rounded bg-gray-800">
                    <div
                      className="h-2 rounded bg-red-700"
                      style={{ width: `${Math.max(2, (l.sizeBytes / maxLeagueSize) * 100)}%` }}
                    />
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="rounded-lg border border-gray-800 bg-gray-900/50 p-4">
          <h2 className="mb-3 text-lg font-semibold text-white">Files by Quality</h2>
          {stats.byQuality.length === 0 ? (
            <p className="text-sm text-gray-500">No files in the library yet.</p>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 text-left text-gray-400">
                  <th className="py-2 font-medium">Quality</th>
                  <th className="py-2 text-right font-medium">Files</th>
                  <th className="py-2 text-right font-medium">Size</th>
                </tr>
              </thead>
              <tbody>
                {stats.byQuality.map((q) => (
                  <tr key={q.quality} className="border-b border-gray-800/50">
                    <td className="py-2 text-gray-200">{q.quality}</td>
                    <td className="py-2 text-right text-gray-300">{q.files}</td>
                    <td className="py-2 text-right text-gray-400">{formatBytes(q.sizeBytes)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        <div className="rounded-lg border border-gray-800 bg-gray-900/50 p-4">
          <h2 className="mb-3 text-lg font-semibold text-white">Coverage by League</h2>
          {stats.coverage.length === 0 ? (
            <p className="text-sm text-gray-500">No leagues added yet.</p>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 text-left text-gray-400">
                  <th className="py-2 font-medium">League</th>
                  <th className="py-2 text-right font-medium">Events</th>
                  <th className="py-2 text-right font-medium">Monitored</th>
                  <th className="py-2 text-right font-medium">Downloaded</th>
                </tr>
              </thead>
              <tbody>
                {stats.perLeague.map((l) => {
                  const c = coverageByLeague.get(l.league);
                  if (!c) return null;
                  return (
                    <tr key={l.league} className="border-b border-gray-800/50">
                      <td className="py-2 text-gray-200">{c.league}</td>
                      <td className="py-2 text-right text-gray-300">{c.events}</td>
                      <td className="py-2 text-right text-gray-300">{c.monitored}</td>
                      <td className="py-2 text-right text-gray-300">{c.withFile}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </div>

        <div className="rounded-lg border border-gray-800 bg-gray-900/50 p-4">
          <h2 className="mb-3 text-lg font-semibold text-white">DVR Recordings</h2>
          {totalRecordings === 0 ? (
            <p className="text-sm text-gray-500">No recordings yet.</p>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-800 text-left text-gray-400">
                  <th className="py-2 font-medium">Status</th>
                  <th className="py-2 text-right font-medium">Count</th>
                </tr>
              </thead>
              <tbody>
                {stats.recordingsByStatus.map((r) => (
                  <tr key={r.status} className="border-b border-gray-800/50">
                    <td className="py-2 text-gray-200">{r.status}</td>
                    <td className="py-2 text-right text-gray-300">{r.count}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </PageShell>
  );
}
