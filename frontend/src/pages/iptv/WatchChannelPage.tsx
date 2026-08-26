import { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { ArrowLeftIcon, SignalIcon, GlobeAltIcon } from '@heroicons/react/24/outline';
import StreamPlayerModal from '../../components/StreamPlayerModal';
import { apiGet } from '../../utils/api';
import { createRequestUrl } from '../../utils/request';

interface ChannelDetail {
  id: number;
  name: string;
  streamUrl: string;
  logoUrl?: string;
  group?: string;
  country?: string;
  detectedQuality?: string;
  qualityScore?: number;
  detectedNetwork?: string;
  iptvOrgId?: string;
  source?: { id: number; name: string };
  sourceName?: string;
}

/**
 * Fullscreen-ish "watch this channel" page. The heavy lifting -
 * format detection, HLS/MPEG-TS playback, debug surface - already
 * lives in StreamPlayerModal; this page just fetches the channel
 * record, sets the modal open by default, and renders channel
 * metadata around it so the URL is shareable. Closing the modal
 * navigates back to the channel list.
 */
export default function WatchChannelPage() {
  const { channelId } = useParams<{ channelId: string }>();
  const [channel, setChannel] = useState<ChannelDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [modalOpen, setModalOpen] = useState(true);

  useEffect(() => {
    if (!channelId) return;
    setLoading(true);
    apiGet(`/api/iptv/channels/${channelId}`)
      .then(async (res) => {
        if (!res.ok) {
          setError(`Channel ${channelId} not found.`);
          return;
        }
        const data = (await res.json()) as ChannelDetail;
        // The backend can return either { source: { id, name } }
        // or a flattened sourceName; tolerate both.
        if (!data.source && (data as { sourceName?: string }).sourceName) {
          data.source = { id: 0, name: (data as { sourceName?: string }).sourceName ?? '' };
        }
        // Stream URL - use the proxy endpoint, not the raw upstream
        // URL. The browser cannot reach upstream IPTV servers
        // directly (CORS, auth headers), so we always proxy.
        // Through the same helper every other request uses. A bare path from
        // the site root ignored the configured URL base, so an install hosted
        // under something like /sportarr asked for /api/iptv/... instead of
        // /sportarr/api/iptv/... and playback simply failed.
        data.streamUrl = createRequestUrl(`/api/iptv/stream/${data.id}`);
        setChannel(data);
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load channel'))
      .finally(() => setLoading(false));
  }, [channelId]);

  if (loading) {
    return (
      <div className="max-w-5xl mx-auto p-6">
        <p className="text-gray-400">Loading channel...</p>
      </div>
    );
  }

  if (error || !channel) {
    return (
      <div className="max-w-5xl mx-auto p-6">
        <Link to="/iptv/channels" className="inline-flex items-center text-sm text-gray-400 hover:text-white mb-4">
          <ArrowLeftIcon className="w-4 h-4 mr-1" />
          Back to channels
        </Link>
        <div className="bg-red-900/30 border border-red-700 rounded-lg p-4">
          <p className="text-red-300">{error || 'Channel not found.'}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-5xl mx-auto p-6">
      <Link to="/iptv/channels" className="inline-flex items-center text-sm text-gray-400 hover:text-white mb-4">
        <ArrowLeftIcon className="w-4 h-4 mr-1" />
        Back to channels
      </Link>

      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6">
        <div className="flex items-start gap-4">
          {channel.logoUrl && (
            <img
              src={channel.logoUrl}
              alt={channel.name}
              className="w-20 h-20 object-contain bg-black rounded"
              onError={(e) => { (e.target as HTMLImageElement).style.visibility = 'hidden'; }}
            />
          )}
          <div className="flex-1">
            <h1 className="text-2xl font-bold text-white">{channel.name}</h1>
            <div className="flex flex-wrap items-center gap-3 mt-2 text-sm text-gray-400">
              {channel.source?.name && (
                <span className="flex items-center gap-1">
                  <SignalIcon className="w-4 h-4" />
                  {channel.source.name}
                </span>
              )}
              {channel.country && (
                <span className="flex items-center gap-1">
                  <GlobeAltIcon className="w-4 h-4" />
                  {channel.country}
                </span>
              )}
              {channel.detectedQuality && (
                <span className="px-2 py-0.5 rounded bg-blue-900/30 text-blue-300 text-xs">
                  {channel.detectedQuality}
                </span>
              )}
              {channel.detectedNetwork && (
                <span className="px-2 py-0.5 rounded bg-purple-900/30 text-purple-300 text-xs">
                  {channel.detectedNetwork}
                </span>
              )}
              {channel.iptvOrgId && (
                <span className="px-2 py-0.5 rounded bg-emerald-900/30 text-emerald-300 text-xs font-mono">
                  {channel.iptvOrgId}
                </span>
              )}
            </div>
            {channel.group && (
              <p className="text-xs text-gray-500 mt-2">{channel.group}</p>
            )}
          </div>
          {!modalOpen && (
            <button
              onClick={() => setModalOpen(true)}
              className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 font-semibold"
            >
              Resume
            </button>
          )}
        </div>
      </div>

      <StreamPlayerModal
        isOpen={modalOpen}
        onClose={() => setModalOpen(false)}
        streamUrl={channel.streamUrl}
        channelId={channel.id}
        channelName={channel.name}
      />
    </div>
  );
}
