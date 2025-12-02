interface LeagueProgressBarProps {
  progressPercent: number;
  progressStatus: 'complete' | 'continuing' | 'partial' | 'missing' | 'unmonitored';
  monitoredEventCount: number;
  downloadedMonitoredCount: number;
  showLabel?: boolean;
  className?: string;
}

/**
 * A visual progress bar showing download completion for a league
 *
 * Color coding:
 * - Green (complete): All monitored events downloaded, no future events expected
 * - Blue (continuing): All monitored events downloaded, but more future events expected
 * - Yellow/Orange (partial): Some monitored events downloaded, some missing
 * - Red (missing): No monitored events downloaded yet
 * - Gray (unmonitored): No events being monitored
 */
export default function LeagueProgressBar({
  progressPercent,
  progressStatus,
  monitoredEventCount,
  downloadedMonitoredCount,
  showLabel = false,
  className = '',
}: LeagueProgressBarProps) {
  // Define colors based on status
  const getStatusColors = () => {
    switch (progressStatus) {
      case 'complete':
        return {
          bar: 'bg-gradient-to-r from-green-500 to-green-400',
          glow: 'shadow-green-500/50',
          text: 'text-green-400',
          bg: 'bg-green-900/20',
        };
      case 'continuing':
        return {
          bar: 'bg-gradient-to-r from-blue-500 to-cyan-400',
          glow: 'shadow-blue-500/50',
          text: 'text-blue-400',
          bg: 'bg-blue-900/20',
        };
      case 'partial':
        return {
          bar: 'bg-gradient-to-r from-yellow-500 to-orange-400',
          glow: 'shadow-yellow-500/50',
          text: 'text-yellow-400',
          bg: 'bg-yellow-900/20',
        };
      case 'missing':
        return {
          bar: 'bg-gradient-to-r from-red-600 to-red-400',
          glow: 'shadow-red-500/50',
          text: 'text-red-400',
          bg: 'bg-red-900/20',
        };
      case 'unmonitored':
      default:
        return {
          bar: 'bg-gray-600',
          glow: '',
          text: 'text-gray-500',
          bg: 'bg-gray-800/50',
        };
    }
  };

  const colors = getStatusColors();
  const displayPercent = Math.min(100, Math.max(0, progressPercent));

  // Status label
  const getStatusLabel = () => {
    switch (progressStatus) {
      case 'complete':
        return 'Complete';
      case 'continuing':
        return 'Up to date';
      case 'partial':
        return `${downloadedMonitoredCount}/${monitoredEventCount}`;
      case 'missing':
        return 'Missing';
      case 'unmonitored':
        return 'Not monitored';
    }
  };

  return (
    <div className={`${className}`}>
      {/* Progress bar container */}
      <div className={`relative h-1.5 rounded-full overflow-hidden ${colors.bg}`}>
        {/* Animated background shimmer for active states */}
        {progressStatus !== 'unmonitored' && progressStatus !== 'complete' && (
          <div className="absolute inset-0 bg-gradient-to-r from-transparent via-white/10 to-transparent animate-shimmer" />
        )}

        {/* Progress bar fill */}
        <div
          className={`h-full rounded-full transition-all duration-500 ease-out ${colors.bar} ${colors.glow} shadow-lg`}
          style={{ width: `${displayPercent}%` }}
        />

        {/* Pulse effect for complete status */}
        {progressStatus === 'complete' && (
          <div className="absolute inset-0 bg-green-400/20 animate-pulse" />
        )}
      </div>

      {/* Label */}
      {showLabel && (
        <div className={`flex justify-between items-center mt-1 text-xs ${colors.text}`}>
          <span>{getStatusLabel()}</span>
          {progressStatus !== 'unmonitored' && (
            <span className="font-mono">{displayPercent.toFixed(0)}%</span>
          )}
        </div>
      )}
    </div>
  );
}

/**
 * Compact version for use in card thumbnails
 * Shows as a thin line at the bottom of an image
 */
export function LeagueProgressLine({
  progressPercent,
  progressStatus,
}: Pick<LeagueProgressBarProps, 'progressPercent' | 'progressStatus'>) {
  const getBarColor = () => {
    switch (progressStatus) {
      case 'complete':
        return 'bg-green-500';
      case 'continuing':
        return 'bg-blue-500';
      case 'partial':
        return 'bg-yellow-500';
      case 'missing':
        return 'bg-red-500';
      case 'unmonitored':
      default:
        return 'bg-gray-600';
    }
  };

  const displayPercent = Math.min(100, Math.max(0, progressPercent));

  return (
    <div className="absolute bottom-0 left-0 right-0 h-1 bg-black/50">
      <div
        className={`h-full transition-all duration-300 ${getBarColor()}`}
        style={{ width: `${displayPercent}%` }}
      />
    </div>
  );
}
