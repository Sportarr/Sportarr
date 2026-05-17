import { Fragment, useRef, useEffect, useState, useCallback } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import {
  XMarkIcon,
  SpeakerWaveIcon,
  SpeakerXMarkIcon,
  ArrowsPointingOutIcon,
  PlayIcon,
  PauseIcon,
  ArrowPathIcon,
  BugAntIcon,
  ClipboardDocumentIcon,
  ArrowTopRightOnSquareIcon,
  RectangleStackIcon,
} from '@heroicons/react/24/outline';
import Hls from 'hls.js';
import mpegts from 'mpegts.js';
import apiClient from '../api/client';

// LocalStorage key for the user's preferred volume + mute state.
// Persisted across modal opens so they don't have to re-set volume
// every time they preview a channel.
const VOLUME_STORAGE_KEY = 'sportarr.streamPlayer.volume';
const MUTED_STORAGE_KEY = 'sportarr.streamPlayer.muted';

function loadStoredVolume(): { volume: number; muted: boolean } {
  if (typeof window === 'undefined') return { volume: 1, muted: false };
  try {
    const rawVol = window.localStorage.getItem(VOLUME_STORAGE_KEY);
    const rawMute = window.localStorage.getItem(MUTED_STORAGE_KEY);
    const volume = rawVol != null ? Math.max(0, Math.min(1, parseFloat(rawVol))) : 1;
    const muted = rawMute === 'true';
    return { volume: Number.isFinite(volume) ? volume : 1, muted };
  } catch {
    return { volume: 1, muted: false };
  }
}

interface StreamPlayerModalProps {
  isOpen: boolean;
  onClose: () => void;
  streamUrl: string | null;
  channelId?: number;
  channelName: string;
}

interface StreamDebugInfo {
  channelId: number;
  channelName: string;
  streamUrl: string;
  userAgent: string;
  headRequest?: {
    success: boolean;
    statusCode?: number;
    statusReason?: string;
    responseTimeMs: number;
    contentType?: string;
    error?: string;
  };
  getRequest?: {
    success: boolean;
    statusCode?: number;
    responseTimeMs: number;
    contentType?: string;
    bytesReceived: number;
    detectedFormat?: string;
    error?: string;
  };
  streamType?: {
    fromUrl: string;
    fromContent?: string;
    contentTypeHeader?: string;
  };
  playability?: {
    canPlay: boolean;
    issues: string[];
    recommendation: string;
  };
  error?: string;
}

type StreamType = 'hls' | 'mpegts' | 'native' | 'unknown';
type PlaybackMode = 'proxy' | 'direct' | 'ffmpeg';

function detectStreamType(url: string): StreamType {
  const lowerUrl = url.toLowerCase();

  // HLS streams
  if (lowerUrl.includes('.m3u8') || lowerUrl.includes('m3u8')) {
    return 'hls';
  }

  // MPEG-TS streams
  if (lowerUrl.includes('.ts') || lowerUrl.includes('/ts/') || lowerUrl.includes('mpegts')) {
    return 'mpegts';
  }

  // FLV streams (also handled by mpegts.js)
  if (lowerUrl.includes('.flv')) {
    return 'mpegts';
  }

  // MP4 and other native formats
  if (lowerUrl.includes('.mp4') || lowerUrl.includes('.webm') || lowerUrl.includes('.ogg')) {
    return 'native';
  }

  // Default to HLS as it's most common for IPTV
  return 'hls';
}

// Global log array for debugging (outside component)
let globalLogs: string[] = [];

function log(level: 'info' | 'warn' | 'error' | 'debug', message: string, ...args: unknown[]) {
  const timestamp = new Date().toISOString().split('T')[1].slice(0, 12);
  const prefix = `[${timestamp}][${level.toUpperCase()}]`;
  const logMessage = `${prefix} ${message} ${args.length > 0 ? JSON.stringify(args) : ''}`;

  globalLogs.push(logMessage);
  if (globalLogs.length > 100) globalLogs = globalLogs.slice(-100); // Keep last 100 logs

  switch (level) {
    case 'info':
      console.log(`[StreamPlayer ${timestamp}]`, message, ...args);
      break;
    case 'warn':
      console.warn(`[StreamPlayer ${timestamp}]`, message, ...args);
      break;
    case 'error':
      console.error(`[StreamPlayer ${timestamp}]`, message, ...args);
      break;
    case 'debug':
      console.debug(`[StreamPlayer ${timestamp}]`, message, ...args);
      break;
  }
}

export default function StreamPlayerModal({
  isOpen,
  onClose,
  streamUrl,
  channelId,
  channelName,
}: StreamPlayerModalProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const hlsRef = useRef<Hls | null>(null);
  const mpegtsPlayerRef = useRef<mpegts.Player | null>(null);
  const stored = loadStoredVolume();
  const [isPlaying, setIsPlaying] = useState(false);
  const [isMuted, setIsMuted] = useState(stored.muted);
  const [volume, setVolume] = useState(stored.volume);
  const [error, setError] = useState<string | null>(null);
  const [errorDetails, setErrorDetails] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [streamType, setStreamType] = useState<StreamType>('unknown');
  const [playbackMode, setPlaybackMode] = useState<PlaybackMode>('proxy');
  const [retryCount, setRetryCount] = useState(0);
  const [showDebug, setShowDebug] = useState(false);
  const [debugInfo, setDebugInfo] = useState<StreamDebugInfo | null>(null);
  const [loadingDebug, setLoadingDebug] = useState(false);
  const [logs, setLogs] = useState<string[]>([]);
  const [ffmpegSessionId, setFfmpegSessionId] = useState<string | null>(null);
  const [videoReady, setVideoReady] = useState(false);
  const [isPip, setIsPip] = useState(false);
  const ffmpegInitializingRef = useRef(false);
  // Auto-retry counter: HLS streams occasionally throw fatal errors on
  // transient upstream blips (segment fetch failure, brief connection
  // reset). One silent retry recovers most of these without showing the
  // error UI; we only surface the error if retries are exhausted.
  const autoRetryCountRef = useRef(0);
  const MAX_AUTO_RETRIES = 2;

  // Reset state when modal opens or channel changes
  useEffect(() => {
    if (isOpen) {
      // Reset videoReady so the callback ref can trigger player initialization
      setVideoReady(false);
      setShowDebug(false);
      setDebugInfo(null);
      setLoadingDebug(false);
      setLogs([]);
      setError(null);
      setErrorDetails(null);
      setIsLoading(true);
      autoRetryCountRef.current = 0;
      ffmpegInitializingRef.current = false;
      // Restore the user's last-used audio state from localStorage so
      // they don't have to re-set volume + mute every time they preview
      // a channel. Defaults to volume=1, muted=false on first run.
      const persisted = loadStoredVolume();
      setVolume(persisted.volume);
      setIsMuted(persisted.muted);
    }
  }, [isOpen, channelId]);

  // Log when modal opens to verify player support
  useEffect(() => {
    if (isOpen && streamUrl) {
      log('info', 'Modal opened', {
        channelId,
        channelName,
        streamUrl: streamUrl.substring(0, 50) + '...',
        hlsSupported: Hls.isSupported(),
        mpegtsSupported: mpegts.isSupported(),
        hasVideoRef: !!videoRef.current,
      });
    }
  }, [isOpen, streamUrl, channelId, channelName]);

  // Fetch debug info from backend
  const fetchDebugInfo = async () => {
    if (!channelId) {
      log('warn', 'Cannot fetch debug info without channelId');
      return;
    }

    setLoadingDebug(true);
    setLogs([...globalLogs]); // Capture current logs
    log('info', 'Fetching stream debug info', { channelId });

    try {
      const response = await apiClient.get(`/iptv/stream/${channelId}/debug`);
      setDebugInfo(response.data);
      log('info', 'Debug info received', response.data);
    } catch (err: unknown) {
      log('error', 'Failed to fetch debug info', err);
      const errorMessage = err instanceof Error ? err.message : 'Unknown error';
      setDebugInfo({
        channelId: channelId,
        channelName: channelName,
        streamUrl: streamUrl || '',
        userAgent: 'N/A',
        error: `Failed to fetch debug info: ${errorMessage}`
      });
    } finally {
      setLoadingDebug(false);
      setLogs([...globalLogs]); // Update logs after fetch
    }
  };

  // Copy debug info to clipboard
  const copyDebugInfo = async () => {
    const info = {
      debugInfo,
      logs: globalLogs.slice(-50),
      browser: navigator.userAgent,
      timestamp: new Date().toISOString(),
    };
    try {
      await navigator.clipboard.writeText(JSON.stringify(info, null, 2));
      log('info', 'Debug info copied to clipboard');
    } catch {
      log('error', 'Failed to copy to clipboard');
    }
  };

  // Convert relative URL to absolute URL (needed for Web Workers like mpegts.js)
  const toAbsoluteUrl = (url: string): string => {
    if (url.startsWith('http://') || url.startsWith('https://')) {
      return url;
    }
    // For relative URLs, prepend the current origin
    return `${window.location.origin}${url.startsWith('/') ? '' : '/'}${url}`;
  };

  // Get the stream URL to use (proxy, direct, or ffmpeg HLS)
  const getStreamUrl = (): string | null => {
    if (!streamUrl) return null;

    if (playbackMode === 'ffmpeg' && ffmpegSessionId) {
      // Use FFmpeg-generated HLS stream (convert to absolute URL for HLS.js)
      return toAbsoluteUrl(`/api/v1/stream/${ffmpegSessionId}/playlist.m3u8`);
    }

    if (playbackMode === 'proxy' && channelId) {
      // Use the backend proxy to avoid CORS issues (convert to absolute URL for mpegts.js worker)
      return toAbsoluteUrl(`/api/iptv/stream/${channelId}`);
    }

    return streamUrl;
  };

  // Start FFmpeg HLS stream
  const startFfmpegStream = async (): Promise<string | null> => {
    if (!channelId) return null;

    try {
      log('info', 'Starting FFmpeg HLS stream', { channelId });
      const response = await apiClient.post(`/v1/stream/${channelId}/start`);

      if (response.data.success) {
        log('info', 'FFmpeg stream started', { sessionId: response.data.sessionId, playlistUrl: response.data.playlistUrl });
        setFfmpegSessionId(response.data.sessionId);
        return response.data.sessionId;
      } else {
        log('error', 'FFmpeg stream failed', response.data.error);
        return null;
      }
    } catch (err) {
      log('error', 'Failed to start FFmpeg stream', err);
      return null;
    }
  };

  // Stop FFmpeg stream
  const stopFfmpegStream = async () => {
    if (channelId && ffmpegSessionId) {
      try {
        log('info', 'Stopping FFmpeg stream', { channelId });
        await apiClient.post(`/v1/stream/${channelId}/stop`);
        setFfmpegSessionId(null);
      } catch (err) {
        log('warn', 'Failed to stop FFmpeg stream', err);
      }
    }
  };

  // Cleanup function
  const cleanup = async () => {
    log('debug', 'Cleaning up player resources');
    if (hlsRef.current) {
      hlsRef.current.destroy();
      hlsRef.current = null;
    }
    if (mpegtsPlayerRef.current) {
      try {
        mpegtsPlayerRef.current.destroy();
      } catch {
        // Ignore destroy errors
      }
      mpegtsPlayerRef.current = null;
    }
    if (videoRef.current) {
      videoRef.current.src = '';
      videoRef.current.load();
    }
    // Stop FFmpeg stream if active
    await stopFfmpegStream();
    setIsPlaying(false);
    setError(null);
    setErrorDetails(null);
    setIsLoading(true);
    setStreamType('unknown');
    autoRetryCountRef.current = 0;
  };

  // Retry with different mode
  const handleRetry = async () => {
    log('info', 'Retrying stream playback', { currentMode: playbackMode, retryCount });
    await cleanup();
    setRetryCount(prev => prev + 1);

    // Cycle through modes: proxy -> ffmpeg -> direct -> proxy
    if (playbackMode === 'proxy') {
      setPlaybackMode('ffmpeg');
      log('info', 'Switching to FFmpeg HLS mode');
    } else if (playbackMode === 'ffmpeg') {
      setPlaybackMode('direct');
      log('info', 'Switching to direct mode');
    } else {
      setPlaybackMode('proxy');
      log('info', 'Switching to proxy mode');
    }
  };

  // Get next mode label for retry button
  const getNextModeLabel = () => {
    if (playbackMode === 'proxy') return 'FFmpeg';
    if (playbackMode === 'ffmpeg') return 'Direct';
    return 'Proxy';
  };

  // Initialize player when modal opens and video element is ready
  useEffect(() => {
    if (!isOpen || !streamUrl || !videoReady || !videoRef.current) {
      return;
    }

    const video = videoRef.current;
    setError(null);
    setErrorDetails(null);
    setIsLoading(true);

    log('info', 'Initializing player', { isOpen, streamUrl: streamUrl.substring(0, 50), videoReady, playbackMode });

    const initializePlayer = async () => {
      try {
        // For FFmpeg mode, start the FFmpeg HLS stream first
        if (playbackMode === 'ffmpeg') {
          log('info', 'Starting FFmpeg HLS transcoding session', { channelId, channelName });
          ffmpegInitializingRef.current = true;

          // Ensure video element is clean before FFmpeg mode
          video.removeAttribute('src');
          video.load();

          const sessionId = await startFfmpegStream();
          if (!sessionId) {
            ffmpegInitializingRef.current = false;
            setError('Failed to start FFmpeg transcoding');
            setErrorDetails('FFmpeg may not be installed or the stream URL is invalid.');
            setIsLoading(false);
            return;
          }

          // Wait a moment for FFmpeg to generate initial HLS segments
          await new Promise(resolve => setTimeout(resolve, 2000));

          // Re-check video element is still available after wait
          if (!videoRef.current) {
            log('warn', 'Video element no longer available after FFmpeg startup');
            ffmpegInitializingRef.current = false;
            return;
          }

          // Use HLS to play the FFmpeg-generated stream
          const hlsUrl = `/api/v1/stream/${sessionId}/playlist.m3u8`;
          log('info', 'Playing FFmpeg HLS stream', { hlsUrl, hlsSupported: Hls.isSupported(), videoElement: !!video });

          if (Hls.isSupported()) {
            log('debug', 'Creating HLS.js instance for FFmpeg stream');
            const hls = new Hls({
              enableWorker: true,
              lowLatencyMode: false,           // Disable for more stable playback
              liveSyncDuration: 6,             // Target 6 seconds behind live edge
              liveMaxLatencyDuration: 15,      // Allow up to 15 seconds latency
              // Note: Don't use liveSyncDurationCount with liveSyncDuration - they conflict
              maxBufferLength: 60,             // Buffer up to 60 seconds
              maxMaxBufferLength: 120,         // Max buffer 2 minutes
              maxBufferSize: 60 * 1000 * 1000, // 60MB buffer size
              maxBufferHole: 1.0,              // Allow 1 second gaps in buffer
              highBufferWatchdogPeriod: 3,     // Check buffer every 3 seconds
            });

            hls.on(Hls.Events.MEDIA_ATTACHED, () => {
              log('debug', 'FFmpeg HLS: Media attached');
              ffmpegInitializingRef.current = false;
              hls.loadSource(hlsUrl);
            });

            hls.on(Hls.Events.MANIFEST_PARSED, (_, data) => {
              log('info', 'FFmpeg HLS: Manifest parsed', { levels: data.levels.length });
              setStreamType('hls');
              setIsLoading(false);
              video.play().catch((e) => {
                log('warn', 'Autoplay blocked', e);
                setIsPlaying(false);
              });
            });

            hls.on(Hls.Events.ERROR, (_, data) => {
              log('error', 'FFmpeg HLS error', { type: data.type, details: data.details, fatal: data.fatal });
              if (data.fatal) {
                setError(`FFmpeg playback error: ${data.details}`);
                setErrorDetails('Try a different playback mode.');
                setIsLoading(false);
              }
            });

            hls.attachMedia(video);
            hlsRef.current = hls;
          } else {
            // Native HLS (Safari)
            ffmpegInitializingRef.current = false;
            video.src = hlsUrl;
            video.addEventListener('loadedmetadata', () => {
              setStreamType('hls');
              setIsLoading(false);
              video.play().catch(() => setIsPlaying(false));
            });
          }
          return;
        }

        // Non-FFmpeg modes: proxy or direct
        const effectiveUrl = getStreamUrl();
        if (!effectiveUrl) {
          setError('No stream URL available');
          setIsLoading(false);
          return;
        }

        const detectedType = detectStreamType(streamUrl);
        setStreamType(detectedType);

        log('info', 'Initializing stream player', {
          channelName,
          channelId,
          originalUrl: streamUrl,
          effectiveUrl,
          detectedType,
          playbackMode,
          hlsSupported: Hls.isSupported(),
          mpegtsSupported: mpegts.isSupported(),
        });

        if (detectedType === 'hls') {
          if (Hls.isSupported()) {
            log('debug', 'Creating HLS player');
            // Tuned for IPTV stability over absolute live-edge latency.
            // Real-world IPTV streams come from a chain of relays + CDNs
            // where individual segment fetches can blip; aggressive
            // low-latency mode chases the live edge so hard that any
            // delayed segment turns into a stall + reload cycle. The
            // settings below keep ~30-60s of buffer headroom so the
            // player rides over transient delays instead of stalling.
            const hls = new Hls({
              enableWorker: true,
              lowLatencyMode: false,           // Stability over live-edge chasing
              backBufferLength: 30,            // 30s rewind buffer is plenty for IPTV
              maxBufferLength: 30,             // Keep ~30s ahead
              maxMaxBufferLength: 120,         // Allow growing to 2 min for slow networks
              maxBufferSize: 60 * 1000 * 1000, // 60 MB hard cap
              maxBufferHole: 1.0,              // Tolerate 1s segment gaps
              highBufferWatchdogPeriod: 3,     // Watchdog every 3s
              // Live-stream-specific knobs.
              liveSyncDuration: 6,             // Sit ~6s behind live edge
              liveMaxLatencyDuration: 20,      // Drift up to 20s before forcing a sync
              // Network resilience — retry transient segment / manifest
              // failures before HLS.js declares them fatal.
              manifestLoadingTimeOut: 20_000,
              manifestLoadingMaxRetry: 4,
              manifestLoadingRetryDelay: 1_000,
              levelLoadingTimeOut: 20_000,
              levelLoadingMaxRetry: 4,
              levelLoadingRetryDelay: 1_000,
              fragLoadingTimeOut: 30_000,
              fragLoadingMaxRetry: 6,
              fragLoadingRetryDelay: 1_000,
              // Enable CORS for cross-origin requests
              xhrSetup: (xhr) => {
                xhr.withCredentials = false;
              },
            });

            hls.on(Hls.Events.MEDIA_ATTACHED, () => {
              log('debug', 'HLS: Media attached, loading source');
              hls.loadSource(effectiveUrl);
            });

            hls.on(Hls.Events.MANIFEST_PARSED, (_, data) => {
              log('info', 'HLS: Manifest parsed', { levels: data.levels.length });
              setIsLoading(false);
              video.play().catch((e) => {
                log('warn', 'Autoplay blocked', e);
                setIsPlaying(false);
              });
            });

            hls.on(Hls.Events.MANIFEST_LOADING, () => {
              log('debug', 'HLS: Loading manifest from', effectiveUrl);
            });

            hls.on(Hls.Events.LEVEL_LOADED, (_, data) => {
              log('debug', 'HLS: Level loaded', { duration: data.details.totalduration });
            });

            hls.on(Hls.Events.ERROR, (_, data) => {
              log('error', 'HLS error', { type: data.type, details: data.details, fatal: data.fatal, response: data.response });

              if (data.fatal) {
                // Silent auto-retry for transient network errors. Most
                // fatal-flagged network errors on IPTV streams are a
                // single blipped segment — startLoad() recovers in <1s
                // and the viewer never sees the error overlay. Only
                // after MAX_AUTO_RETRIES do we surface the error to the
                // user so they can pick a different playback mode.
                switch (data.type) {
                  case Hls.ErrorTypes.NETWORK_ERROR:
                    if (autoRetryCountRef.current < MAX_AUTO_RETRIES) {
                      autoRetryCountRef.current += 1;
                      log('info', 'HLS auto-retry on network error', {
                        attempt: autoRetryCountRef.current,
                        details: data.details,
                      });
                      hls.startLoad();
                      break;
                    }
                    if (data.response?.code === 0) {
                      setError('Stream server blocked the request');
                      setErrorDetails(`Possible CORS / network issue. Response code: ${data.response?.code}. Try the Retry button to switch to FFmpeg or Direct mode.`);
                    } else {
                      setError(`Network error (${data.response?.code || 'unknown'})`);
                      setErrorDetails(`Failed to load: ${data.details}. The stream may be offline or unreachable.`);
                    }
                    setIsLoading(false);
                    break;
                  case Hls.ErrorTypes.MEDIA_ERROR:
                    if (autoRetryCountRef.current < MAX_AUTO_RETRIES) {
                      autoRetryCountRef.current += 1;
                      log('info', 'HLS auto-retry on media error', {
                        attempt: autoRetryCountRef.current,
                        details: data.details,
                      });
                      hls.recoverMediaError();
                      break;
                    }
                    setError(`Media decoding failed`);
                    setErrorDetails(`${data.details}. Try a different playback mode.`);
                    setIsLoading(false);
                    break;
                  default:
                    setError(`Playback failed: ${data.details}`);
                    setErrorDetails(`Fatal error type: ${data.type}`);
                    setIsLoading(false);
                    break;
                }
              }
            });

            // Reset auto-retry counter on every successful fragment load.
            // A stream that ran clean for a minute then blips deserves a
            // fresh retry budget instead of stacking on top of an old run.
            hls.on(Hls.Events.FRAG_LOADED, () => {
              if (autoRetryCountRef.current > 0) {
                autoRetryCountRef.current = 0;
              }
            });

            hls.attachMedia(video);
            hlsRef.current = hls;
          } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
            // Native HLS support (Safari)
            log('info', 'Using native HLS support (Safari)');
            video.src = effectiveUrl;
            video.addEventListener('loadedmetadata', () => {
              log('debug', 'Native HLS: Metadata loaded');
              setIsLoading(false);
              video.play().catch(() => setIsPlaying(false));
            });
          } else {
            setError('HLS is not supported in this browser');
            setErrorDetails('Your browser does not support HLS playback. Try Chrome, Firefox, or Safari.');
            setIsLoading(false);
          }
        } else if (detectedType === 'mpegts') {
          if (mpegts.isSupported()) {
            log('info', 'Creating MPEG-TS player', { url: effectiveUrl, originalUrl: streamUrl });
            const player = mpegts.createPlayer({
              type: streamUrl.toLowerCase().includes('.flv') ? 'flv' : 'mpegts',
              url: effectiveUrl,
              isLive: true,
              cors: true,
            }, {
              enableWorker: true,
              enableStashBuffer: true,
              stashInitialSize: 1024 * 1024,       // 1MB initial stash buffer for smoother start
              autoCleanupSourceBuffer: true,
              autoCleanupMaxBackwardDuration: 60,  // Keep 60 seconds of backward buffer
              autoCleanupMinBackwardDuration: 30,  // Minimum 30 seconds before cleanup
              lazyLoad: false,                     // Start loading immediately
              lazyLoadMaxDuration: 0,
              lazyLoadRecoverDuration: 0,
              deferLoadAfterSourceOpen: false,
              // Relaxed latency settings for stable playback without stuttering
              liveBufferLatencyChasing: false,     // Disable aggressive latency chasing
              liveBufferLatencyMaxLatency: 10.0,   // Allow up to 10 seconds latency for buffer headroom
              liveBufferLatencyMinRemain: 3.0,     // Keep at least 3 seconds buffer before chasing
            });

            player.on(mpegts.Events.ERROR, (errorType, errorDetail, errorInfo) => {
              log('error', 'MPEG-TS error', { errorType, errorDetail, errorInfo });
              setError(`Stream error: ${errorType}`);
              setErrorDetails(String(errorDetail));
            });

            player.on(mpegts.Events.LOADING_COMPLETE, () => {
              log('debug', 'MPEG-TS: Loading complete');
            });

            player.on(mpegts.Events.MEDIA_INFO, (info) => {
              log('info', 'MPEG-TS: Media info received', info);
              setIsLoading(false);
            });

            player.on(mpegts.Events.STATISTICS_INFO, (stats) => {
              if (stats.speed !== undefined && stats.speed > 0) {
                log('debug', 'MPEG-TS: Receiving data', { speed: stats.speed, decodedFrames: stats.decodedFrames });
              }
            });

            player.on(mpegts.Events.METADATA_ARRIVED, (metadata) => {
              log('info', 'MPEG-TS: Metadata arrived', metadata);
            });

            player.attachMediaElement(video);
            player.load();
            try {
              player.play();
            } catch (e) {
              log('warn', 'MPEG-TS autoplay blocked', e);
            }
            mpegtsPlayerRef.current = player;

            // Set loading to false after a short delay if no media info arrives
            setTimeout(() => {
              if (mpegtsPlayerRef.current) {
                setIsLoading(false);
              }
            }, 5000);
          } else {
            log('error', 'MPEG-TS not supported in this browser');
            setError('MPEG-TS/FLV is not supported in this browser');
            setErrorDetails('Your browser does not support MPEG-TS playback. Try Chrome or Edge.');
            setIsLoading(false);
          }
        } else if (detectedType === 'native') {
          log('debug', 'Using native video playback');
          video.src = effectiveUrl;
          video.addEventListener('loadedmetadata', () => {
            log('debug', 'Native: Metadata loaded');
            setIsLoading(false);
            video.play().catch(() => setIsPlaying(false));
          });
          video.addEventListener('error', (e) => {
            log('error', 'Native video error', e);
            setError('Failed to load video');
            setErrorDetails(`Video element error: ${video.error?.message || 'Unknown error'}`);
            setIsLoading(false);
          });
        } else {
          // Try HLS as default
          log('info', 'Unknown stream type, trying HLS');
          if (Hls.isSupported()) {
            const hls = new Hls();
            hls.on(Hls.Events.ERROR, (_, data) => {
              if (data.fatal) {
                log('error', 'Default HLS error', data);
                setError('Could not play this stream format');
                setErrorDetails(`HLS error: ${data.details}`);
                setIsLoading(false);
              }
            });
            hls.loadSource(effectiveUrl);
            hls.attachMedia(video);
            hlsRef.current = hls;
          } else {
            video.src = effectiveUrl;
          }
        }
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : (typeof err === 'string' ? err : JSON.stringify(err));
        log('error', 'Player initialization error', { message: errorMessage, type: typeof err, err });
        setError(`Failed to initialize player`);
        setErrorDetails(errorMessage || 'Unknown initialization error');
        setIsLoading(false);
        ffmpegInitializingRef.current = false;
      }
    };

    initializePlayer();

    // Video event listeners
    const handlePlay = () => {
      log('debug', 'Video playing');
      setIsPlaying(true);
    };
    const handlePause = () => {
      log('debug', 'Video paused');
      setIsPlaying(false);
    };
    const handleError = (e: Event) => {
      const videoEl = e.target as HTMLVideoElement;
      // Ignore errors during FFmpeg initialization (video has no src yet)
      if (ffmpegInitializingRef.current) {
        log('debug', 'Ignoring video error during FFmpeg initialization');
        return;
      }
      log('error', 'Video element error', { error: videoEl.error });
      if (!error) {
        setError('Failed to play stream');
        setErrorDetails(`Error code: ${videoEl.error?.code}, Message: ${videoEl.error?.message}`);
      }
      setIsLoading(false);
    };
    const handleCanPlay = () => {
      log('debug', 'Video can play');
      setIsLoading(false);
      // Clear any previous errors when video can play
      setError(null);
      setErrorDetails(null);
    };
    const handleWaiting = () => {
      log('debug', 'Video waiting/buffering');
    };
    const handleStalled = () => {
      log('warn', 'Video stalled');
    };

    video.addEventListener('play', handlePlay);
    video.addEventListener('pause', handlePause);
    video.addEventListener('error', handleError);
    video.addEventListener('canplay', handleCanPlay);
    video.addEventListener('waiting', handleWaiting);
    video.addEventListener('stalled', handleStalled);

    return () => {
      video.removeEventListener('play', handlePlay);
      video.removeEventListener('pause', handlePause);
      video.removeEventListener('error', handleError);
      video.removeEventListener('canplay', handleCanPlay);
      video.removeEventListener('waiting', handleWaiting);
      video.removeEventListener('stalled', handleStalled);
      cleanup();
    };
  }, [isOpen, streamUrl, playbackMode, retryCount, videoReady, channelId]);

  const handleClose = async () => {
    await cleanup();
    setPlaybackMode('proxy');
    setRetryCount(0);
    setFfmpegSessionId(null);
    setVideoReady(false);
    onClose();
  };

  const togglePlay = () => {
    if (videoRef.current) {
      if (isPlaying) {
        videoRef.current.pause();
      } else {
        videoRef.current.play().catch(() => {});
      }
    }
  };

  const toggleMute = useCallback(() => {
    if (videoRef.current) {
      const newMuted = !isMuted;
      videoRef.current.muted = newMuted;
      setIsMuted(newMuted);
      try {
        window.localStorage.setItem(MUTED_STORAGE_KEY, String(newMuted));
      } catch { /* localStorage may be disabled */ }
    }
  }, [isMuted]);

  const handleVolumeChange = useCallback((newVolume: number) => {
    const clampedVolume = Math.max(0, Math.min(1, newVolume));
    setVolume(clampedVolume);
    try {
      window.localStorage.setItem(VOLUME_STORAGE_KEY, String(clampedVolume));
    } catch { /* localStorage may be disabled */ }
    if (videoRef.current) {
      videoRef.current.volume = clampedVolume;
      // If volume is set above 0 and was muted, unmute
      if (clampedVolume > 0 && isMuted) {
        videoRef.current.muted = false;
        setIsMuted(false);
        try { window.localStorage.setItem(MUTED_STORAGE_KEY, 'false'); } catch { /* ignore */ }
      }
      // If volume is set to 0, mute
      if (clampedVolume === 0) {
        videoRef.current.muted = true;
        setIsMuted(true);
        try { window.localStorage.setItem(MUTED_STORAGE_KEY, 'true'); } catch { /* ignore */ }
      }
    }
  }, [isMuted]);

  const handleVolumeWheel = (e: React.WheelEvent) => {
    e.preventDefault();
    // Scroll up = volume up, scroll down = volume down
    const delta = e.deltaY > 0 ? -0.05 : 0.05;
    handleVolumeChange(volume + delta);
  };

  const toggleFullscreen = useCallback(() => {
    if (videoRef.current) {
      if (document.fullscreenElement) {
        document.exitFullscreen();
      } else {
        videoRef.current.requestFullscreen().catch(() => {});
      }
    }
  }, []);

  // Picture-in-picture toggle. PiP is the IPTV viewer's superpower:
  // detach the player into a floating window so you can keep watching
  // while bookmarking the next channel, replying to chat, etc. Modern
  // browsers gate this behind a one-shot user gesture so the click is
  // the gesture.
  const togglePip = useCallback(async () => {
    const video = videoRef.current;
    if (!video) return;
    try {
      if (document.pictureInPictureElement === video) {
        await document.exitPictureInPicture();
      } else if (document.pictureInPictureEnabled && !video.disablePictureInPicture) {
        await video.requestPictureInPicture();
      }
    } catch (e) {
      log('warn', 'Picture-in-picture toggle failed', e);
    }
  }, []);

  // Pop out to a dedicated /iptv/watch/:id tab. Same StreamPlayerModal
  // re-mounts there, but the URL is now shareable / bookmarkable — pin
  // it as a tab, drop it in chat, etc. Closes the in-place modal once
  // the new tab is open since two players against the same proxy stream
  // would double the upstream load.
  const popOut = useCallback(() => {
    if (channelId == null) return;
    window.open(`/iptv/watch/${channelId}`, '_blank', 'noopener,noreferrer');
    onClose();
  }, [channelId, onClose]);

  // Track PiP state so the button reflects current mode.
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const onEnter = () => setIsPip(true);
    const onLeave = () => setIsPip(false);
    video.addEventListener('enterpictureinpicture', onEnter);
    video.addEventListener('leavepictureinpicture', onLeave);
    return () => {
      video.removeEventListener('enterpictureinpicture', onEnter);
      video.removeEventListener('leavepictureinpicture', onLeave);
    };
  }, [videoReady]);

  // Keyboard shortcuts. Standard set found across every web video
  // player so muscle memory works out of the box: space = play/pause,
  // m = mute, f = fullscreen, p = pop-out, i = picture-in-picture,
  // arrows = volume / seek (live streams aren't seekable; the arrows
  // adjust volume in 5% steps instead). Listener is on the modal so
  // it only fires while the player is open.
  useEffect(() => {
    if (!isOpen) return;
    const handler = (e: KeyboardEvent) => {
      // Don't hijack typing in input/textarea/contenteditable surfaces.
      const target = e.target as HTMLElement | null;
      const tag = target?.tagName?.toLowerCase();
      if (tag === 'input' || tag === 'textarea' || target?.isContentEditable) return;
      switch (e.key) {
        case ' ':
        case 'k':
          e.preventDefault();
          if (videoRef.current) {
            if (videoRef.current.paused) videoRef.current.play().catch(() => {});
            else videoRef.current.pause();
          }
          break;
        case 'm':
        case 'M':
          e.preventDefault();
          toggleMute();
          break;
        case 'f':
        case 'F':
          e.preventDefault();
          toggleFullscreen();
          break;
        case 'p':
        case 'P':
          e.preventDefault();
          popOut();
          break;
        case 'i':
        case 'I':
          e.preventDefault();
          togglePip();
          break;
        case 'ArrowUp':
          e.preventDefault();
          handleVolumeChange(volume + 0.05);
          break;
        case 'ArrowDown':
          e.preventDefault();
          handleVolumeChange(volume - 0.05);
          break;
        case 'Escape':
          // Headless UI handles Escape via Dialog.onClose already, but
          // exit fullscreen first so the user doesn't lose the player.
          if (document.fullscreenElement) {
            e.preventDefault();
            document.exitFullscreen().catch(() => {});
          }
          break;
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [isOpen, volume, toggleMute, toggleFullscreen, togglePip, popOut, handleVolumeChange]);

  const getStreamTypeLabel = () => {
    switch (streamType) {
      case 'hls': return 'HLS';
      case 'mpegts': return 'MPEG-TS';
      case 'native': return 'Native';
      default: return 'Unknown';
    }
  };

  return (
    <Transition
      appear
      show={isOpen && !!streamUrl}
      as={Fragment}
      afterLeave={() => {
        document.querySelectorAll('[inert]').forEach((el) => {
          el.removeAttribute('inert');
        });
      }}
    >
      <Dialog as="div" className="relative z-50" onClose={handleClose}>
        <Transition.Child
          as={Fragment}
          enter="ease-out duration-300"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-200"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black/90" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-y-auto">
          <div className="flex min-h-full items-center justify-center p-4">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-5xl transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 shadow-xl transition-all">
                {/* Header */}
                <div className="flex items-center justify-between p-4 border-b border-red-900/30">
                  <div className="flex items-center gap-3 min-w-0">
                    <Dialog.Title className="text-lg font-bold text-white truncate">
                      {channelName}
                    </Dialog.Title>
                    <span className="px-2 py-0.5 text-xs rounded bg-blue-600/30 text-blue-300 border border-blue-500/30">
                      {getStreamTypeLabel()}
                    </span>
                    <span className={`px-2 py-0.5 text-xs rounded ${
                      playbackMode === 'proxy' ? 'bg-green-600/30 text-green-300' :
                      playbackMode === 'ffmpeg' ? 'bg-purple-600/30 text-purple-300' :
                      'bg-yellow-600/30 text-yellow-300'
                    }`}>
                      {playbackMode === 'proxy' ? 'Proxy' : playbackMode === 'ffmpeg' ? 'FFmpeg' : 'Direct'}
                    </span>
                  </div>
                  <div className="flex items-center gap-1 flex-shrink-0">
                    {/* Picture-in-picture button — pops the video into a
                        floating mini-window so you can keep watching
                        while doing other things. Hidden when the browser
                        doesn't support PiP (Firefox on some setups,
                        anything that flags disablePictureInPicture). */}
                    {typeof document !== 'undefined' && document.pictureInPictureEnabled && (
                      <button
                        onClick={togglePip}
                        className={`p-1.5 rounded-lg transition-colors ${
                          isPip ? 'bg-blue-600 hover:bg-blue-700' : 'hover:bg-gray-700'
                        }`}
                        title={isPip ? 'Exit picture-in-picture (i)' : 'Picture-in-picture (i)'}
                      >
                        <RectangleStackIcon className={`w-5 h-5 ${isPip ? 'text-white' : 'text-gray-400'}`} />
                      </button>
                    )}
                    {/* Pop-out: opens /iptv/watch/:id in a new tab so the
                        viewer has a bookmarkable, shareable, dedicated
                        URL for this channel. Only shown when a channelId
                        is known AND we're not already on the watch page
                        — popping out to the page we're on is a no-op the
                        user shouldn't be offered. */}
                    {channelId != null &&
                     typeof window !== 'undefined' &&
                     !window.location.pathname.startsWith('/iptv/watch/') && (
                      <button
                        onClick={popOut}
                        className="p-1.5 rounded-lg hover:bg-gray-700 transition-colors"
                        title="Open in new tab (p)"
                      >
                        <ArrowTopRightOnSquareIcon className="w-5 h-5 text-gray-400" />
                      </button>
                    )}
                    <button
                      onClick={handleClose}
                      className="p-1.5 rounded-lg hover:bg-gray-700 transition-colors"
                      title="Close (Esc)"
                    >
                      <XMarkIcon className="w-5 h-5 text-gray-400" />
                    </button>
                  </div>
                </div>

                {/* Video Player */}
                <div className="relative bg-black aspect-video">
                  {isLoading && !error && (
                    <div className="absolute inset-0 flex items-center justify-center bg-black/50">
                      <div className="flex flex-col items-center gap-3">
                        <div className="w-12 h-12 border-4 border-red-500 border-t-transparent rounded-full animate-spin" />
                        <span className="text-gray-400">Loading stream...</span>
                        <span className="text-gray-500 text-xs">Mode: {playbackMode}</span>
                      </div>
                    </div>
                  )}

                  {error && (
                    <div className="absolute inset-0 flex items-center justify-center bg-black/80">
                      <div className="text-center p-6 max-w-lg">
                        <div className="text-red-400 text-lg mb-2">Stream Error</div>
                        <div className="text-gray-400 text-sm mb-2">{error}</div>
                        {errorDetails && (
                          <div className="text-gray-500 text-xs mb-4 p-2 bg-gray-900 rounded">
                            {errorDetails}
                          </div>
                        )}
                        <div className="mt-4 space-y-2">
                          <button
                            onClick={handleRetry}
                            className="flex items-center justify-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors mx-auto"
                          >
                            <ArrowPathIcon className="w-4 h-4" />
                            Retry (try {getNextModeLabel()})
                          </button>
                          <div className="text-xs text-gray-600">
                            Current: {playbackMode} | Retries: {retryCount}
                          </div>
                        </div>
                        <div className="mt-4 text-xs text-gray-600 break-all">
                          URL: {streamUrl?.substring(0, 80)}...
                        </div>
                      </div>
                    </div>
                  )}

                  <video
                    key={`video-${channelId}`}
                    ref={(el) => {
                      (videoRef as React.MutableRefObject<HTMLVideoElement | null>).current = el;
                      if (el && !videoReady) {
                        log('debug', 'Video element mounted', { channelId });
                        // Apply persisted volume + mute so the user's
                        // preference carries between channels and reloads.
                        el.volume = volume;
                        el.muted = isMuted;
                        setVideoReady(true);
                      }
                    }}
                    className="w-full h-full"
                    controls={false}
                    playsInline
                    autoPlay
                    crossOrigin="anonymous"
                    onWheel={handleVolumeWheel}
                    onDoubleClick={toggleFullscreen}
                  />
                </div>

                {/* Controls */}
                <div className="flex items-center justify-between p-4 border-t border-red-900/30 bg-black/30">
                  <div className="flex items-center gap-2">
                    <button
                      onClick={togglePlay}
                      disabled={isLoading || !!error}
                      className="p-2 rounded-lg bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                    >
                      {isPlaying ? (
                        <PauseIcon className="w-6 h-6 text-white" />
                      ) : (
                        <PlayIcon className="w-6 h-6 text-white" />
                      )}
                    </button>
                    <button
                      onClick={toggleMute}
                      disabled={isLoading || !!error}
                      className="p-2 rounded-lg bg-gray-700 hover:bg-gray-600 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                      title={isMuted ? 'Unmute' : 'Mute'}
                    >
                      {isMuted || volume === 0 ? (
                        <SpeakerXMarkIcon className="w-6 h-6 text-white" />
                      ) : (
                        <SpeakerWaveIcon className="w-6 h-6 text-white" />
                      )}
                    </button>
                    {/* Volume Slider */}
                    <div
                      className="flex items-center gap-2 group"
                      onWheel={handleVolumeWheel}
                      title={`Volume: ${Math.round(volume * 100)}%`}
                    >
                      <input
                        type="range"
                        min="0"
                        max="1"
                        step="0.01"
                        value={isMuted ? 0 : volume}
                        onChange={(e) => handleVolumeChange(parseFloat(e.target.value))}
                        disabled={isLoading || !!error}
                        className="w-20 h-1 bg-gray-600 rounded-lg appearance-none cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed
                          [&::-webkit-slider-thumb]:appearance-none [&::-webkit-slider-thumb]:w-3 [&::-webkit-slider-thumb]:h-3
                          [&::-webkit-slider-thumb]:rounded-full [&::-webkit-slider-thumb]:bg-white [&::-webkit-slider-thumb]:cursor-pointer
                          [&::-webkit-slider-thumb]:hover:bg-red-400 [&::-webkit-slider-thumb]:transition-colors
                          [&::-moz-range-thumb]:w-3 [&::-moz-range-thumb]:h-3 [&::-moz-range-thumb]:rounded-full
                          [&::-moz-range-thumb]:bg-white [&::-moz-range-thumb]:border-0 [&::-moz-range-thumb]:cursor-pointer"
                      />
                      <span className="text-xs text-gray-400 w-8 text-right">
                        {Math.round((isMuted ? 0 : volume) * 100)}%
                      </span>
                    </div>
                    <button
                      onClick={handleRetry}
                      disabled={isLoading}
                      className="p-2 rounded-lg bg-gray-700 hover:bg-gray-600 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                      title="Retry stream"
                    >
                      <ArrowPathIcon className="w-6 h-6 text-white" />
                    </button>
                  </div>

                  <div className="flex items-center gap-2">
                    <span className={`px-2 py-1 text-xs rounded ${isPlaying ? 'bg-green-600/30 text-green-300' : 'bg-yellow-600/30 text-yellow-300'}`}>
                      {isLoading ? 'Loading...' : isPlaying ? 'Playing' : 'Paused'}
                    </span>
                    <button
                      onClick={() => {
                        setShowDebug(!showDebug);
                        if (!showDebug && !debugInfo) {
                          fetchDebugInfo();
                        } else {
                          setLogs([...globalLogs]);
                        }
                      }}
                      className={`p-2 rounded-lg transition-colors ${showDebug ? 'bg-orange-600 hover:bg-orange-700' : 'bg-gray-700 hover:bg-gray-600'}`}
                      title="Debug stream"
                    >
                      <BugAntIcon className="w-6 h-6 text-white" />
                    </button>
                    <button
                      onClick={toggleFullscreen}
                      disabled={isLoading || !!error}
                      className="p-2 rounded-lg bg-gray-700 hover:bg-gray-600 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                    >
                      <ArrowsPointingOutIcon className="w-6 h-6 text-white" />
                    </button>
                  </div>
                </div>

                {/* Debug Panel */}
                {showDebug && (
                  <div className="p-4 border-t border-red-900/30 bg-gray-900/50 max-h-96 overflow-y-auto">
                    <div className="flex items-center justify-between mb-3">
                      <h3 className="text-sm font-semibold text-white flex items-center gap-2">
                        <BugAntIcon className="w-4 h-4" />
                        Stream Diagnostics
                      </h3>
                      <div className="flex gap-2">
                        <button
                          onClick={fetchDebugInfo}
                          disabled={loadingDebug}
                          className="px-3 py-1 text-xs bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white rounded flex items-center gap-1"
                        >
                          <ArrowPathIcon className="w-3 h-3" />
                          Refresh
                        </button>
                        <button
                          onClick={copyDebugInfo}
                          className="px-3 py-1 text-xs bg-gray-600 hover:bg-gray-700 text-white rounded flex items-center gap-1"
                        >
                          <ClipboardDocumentIcon className="w-3 h-3" />
                          Copy
                        </button>
                      </div>
                    </div>

                    {loadingDebug && (
                      <div className="text-center py-4 text-gray-400">
                        <div className="w-6 h-6 border-2 border-blue-500 border-t-transparent rounded-full animate-spin mx-auto mb-2" />
                        Testing stream connectivity...
                      </div>
                    )}

                    {debugInfo && !loadingDebug && (
                      <div className="space-y-3 text-xs">
                        {/* Stream Info */}
                        <div className="p-2 bg-gray-800 rounded">
                          <div className="font-semibold text-gray-300 mb-1">Stream Info</div>
                          <div className="text-gray-400 break-all">
                            <div><span className="text-gray-500">URL:</span> {debugInfo.streamUrl}</div>
                            <div><span className="text-gray-500">Channel:</span> {debugInfo.channelName} (ID: {debugInfo.channelId})</div>
                            {debugInfo.streamType && (
                              <>
                                <div><span className="text-gray-500">Type (URL):</span> {debugInfo.streamType.fromUrl}</div>
                                {debugInfo.streamType.fromContent && (
                                  <div><span className="text-gray-500">Type (Content):</span> {debugInfo.streamType.fromContent}</div>
                                )}
                                {debugInfo.streamType.contentTypeHeader && (
                                  <div><span className="text-gray-500">Content-Type:</span> {debugInfo.streamType.contentTypeHeader}</div>
                                )}
                              </>
                            )}
                          </div>
                        </div>

                        {/* HEAD Request */}
                        {debugInfo.headRequest && (
                          <div className="p-2 bg-gray-800 rounded">
                            <div className="font-semibold text-gray-300 mb-1">HEAD Request</div>
                            <div className={`${debugInfo.headRequest.success ? 'text-green-400' : 'text-red-400'}`}>
                              {debugInfo.headRequest.success ? '✓' : '✗'} {debugInfo.headRequest.statusCode} {debugInfo.headRequest.statusReason}
                              <span className="text-gray-500 ml-2">({debugInfo.headRequest.responseTimeMs}ms)</span>
                            </div>
                            {debugInfo.headRequest.contentType && (
                              <div className="text-gray-400">Content-Type: {debugInfo.headRequest.contentType}</div>
                            )}
                            {debugInfo.headRequest.error && (
                              <div className="text-red-400">Error: {debugInfo.headRequest.error}</div>
                            )}
                          </div>
                        )}

                        {/* GET Request */}
                        {debugInfo.getRequest && (
                          <div className="p-2 bg-gray-800 rounded">
                            <div className="font-semibold text-gray-300 mb-1">GET Request (first 1KB)</div>
                            <div className={`${debugInfo.getRequest.success ? 'text-green-400' : 'text-red-400'}`}>
                              {debugInfo.getRequest.success ? '✓' : '✗'} {debugInfo.getRequest.statusCode}
                              <span className="text-gray-500 ml-2">({debugInfo.getRequest.responseTimeMs}ms, {debugInfo.getRequest.bytesReceived} bytes)</span>
                            </div>
                            {debugInfo.getRequest.detectedFormat && (
                              <div className="text-blue-400">Detected: {debugInfo.getRequest.detectedFormat}</div>
                            )}
                            {debugInfo.getRequest.error && (
                              <div className="text-red-400">Error: {debugInfo.getRequest.error}</div>
                            )}
                          </div>
                        )}

                        {/* Playability */}
                        {debugInfo.playability && (
                          <div className={`p-2 rounded ${debugInfo.playability.canPlay ? 'bg-green-900/30' : 'bg-red-900/30'}`}>
                            <div className="font-semibold text-gray-300 mb-1">Playability Assessment</div>
                            <div className={`${debugInfo.playability.canPlay ? 'text-green-400' : 'text-red-400'} font-semibold`}>
                              {debugInfo.playability.canPlay ? '✓ Stream appears playable' : '✗ Stream may have issues'}
                            </div>
                            {debugInfo.playability.issues.length > 0 && (
                              <ul className="text-yellow-400 mt-1 list-disc list-inside">
                                {debugInfo.playability.issues.map((issue, i) => (
                                  <li key={i}>{issue}</li>
                                ))}
                              </ul>
                            )}
                            <div className="text-gray-400 mt-1">
                              <span className="text-gray-500">Recommendation:</span> {debugInfo.playability.recommendation}
                            </div>
                          </div>
                        )}

                        {debugInfo.error && (
                          <div className="p-2 bg-red-900/30 rounded text-red-400">
                            Error: {debugInfo.error}
                          </div>
                        )}
                      </div>
                    )}

                    {/* Player Logs */}
                    <div className="mt-3 p-2 bg-gray-800 rounded">
                      <div className="font-semibold text-gray-300 mb-1 text-xs">Player Logs (last 20)</div>
                      <div className="font-mono text-[10px] text-gray-400 max-h-32 overflow-y-auto space-y-0.5">
                        {logs.slice(-20).map((logLine, i) => (
                          <div key={i} className={`${
                            logLine.includes('[ERROR]') ? 'text-red-400' :
                            logLine.includes('[WARN]') ? 'text-yellow-400' :
                            logLine.includes('[INFO]') ? 'text-blue-400' :
                            'text-gray-500'
                          }`}>
                            {logLine}
                          </div>
                        ))}
                        {logs.length === 0 && <div className="text-gray-500 italic">No logs yet</div>}
                      </div>
                    </div>
                  </div>
                )}
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}
