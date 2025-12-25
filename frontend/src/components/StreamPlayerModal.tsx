import { Fragment, useRef, useEffect, useState } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { XMarkIcon, SpeakerWaveIcon, SpeakerXMarkIcon, ArrowsPointingOutIcon, PlayIcon, PauseIcon, ArrowPathIcon, BugAntIcon, ClipboardDocumentIcon } from '@heroicons/react/24/outline';
import Hls from 'hls.js';
import mpegts from 'mpegts.js';
import apiClient from '../api/client';

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
  const [isPlaying, setIsPlaying] = useState(false);
  const [isMuted, setIsMuted] = useState(false);
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

  // Get the stream URL to use (proxy, direct, or ffmpeg HLS)
  const getStreamUrl = (): string | null => {
    if (!streamUrl) return null;

    if (playbackMode === 'ffmpeg' && ffmpegSessionId) {
      // Use FFmpeg-generated HLS stream
      return `/api/v1/stream/${ffmpegSessionId}/playlist.m3u8`;
    }

    if (playbackMode === 'proxy' && channelId) {
      // Use the backend proxy to avoid CORS issues
      return `/api/iptv/stream/${channelId}`;
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

          const sessionId = await startFfmpegStream();
          if (!sessionId) {
            setError('Failed to start FFmpeg transcoding');
            setErrorDetails('FFmpeg may not be installed or the stream URL is invalid.');
            setIsLoading(false);
            return;
          }

          // Wait a moment for FFmpeg to generate initial HLS segments
          await new Promise(resolve => setTimeout(resolve, 2000));

          // Use HLS to play the FFmpeg-generated stream
          const hlsUrl = `/api/v1/stream/${sessionId}/playlist.m3u8`;
          log('info', 'Playing FFmpeg HLS stream', { hlsUrl });

          if (Hls.isSupported()) {
            const hls = new Hls({
              enableWorker: true,
              lowLatencyMode: true,
              liveSyncDuration: 3,
              liveMaxLatencyDuration: 10,
              maxBufferLength: 30,
              maxMaxBufferLength: 60,
            });

            hls.on(Hls.Events.MEDIA_ATTACHED, () => {
              log('debug', 'FFmpeg HLS: Media attached');
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
            const hls = new Hls({
              enableWorker: true,
              lowLatencyMode: true,
              backBufferLength: 90,
              maxBufferLength: 30,
              maxMaxBufferLength: 60,
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
                switch (data.type) {
                  case Hls.ErrorTypes.NETWORK_ERROR:
                    if (data.response?.code === 0) {
                      setError('CORS/Network error - trying alternative method...');
                      setErrorDetails(`The stream server blocked the request. This is usually a CORS issue. Response code: ${data.response?.code}`);
                    } else {
                      setError(`Network error (${data.response?.code || 'unknown'})`);
                      setErrorDetails(`Failed to load: ${data.details}. The stream may be offline or unreachable.`);
                    }
                    // Try to recover
                    hls.startLoad();
                    break;
                  case Hls.ErrorTypes.MEDIA_ERROR:
                    setError('Media error - trying to recover...');
                    setErrorDetails(`Media decoding failed: ${data.details}`);
                    hls.recoverMediaError();
                    break;
                  default:
                    setError(`Playback failed: ${data.details}`);
                    setErrorDetails(`Fatal error type: ${data.type}`);
                    setIsLoading(false);
                    break;
                }
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
              stashInitialSize: 512 * 1024,        // 512KB initial buffer
              autoCleanupSourceBuffer: true,
              autoCleanupMaxBackwardDuration: 30,  // Keep 30 seconds of backward buffer
              autoCleanupMinBackwardDuration: 15,  // Minimum 15 seconds before cleanup
              lazyLoad: false,                     // Start loading immediately
              lazyLoadMaxDuration: 0,
              lazyLoadRecoverDuration: 0,
              deferLoadAfterSourceOpen: false,
              // More relaxed latency settings for stable playback
              liveBufferLatencyChasing: false,     // Disable aggressive latency chasing
              liveBufferLatencyMaxLatency: 5.0,    // Allow up to 5 seconds latency
              liveBufferLatencyMinRemain: 1.0,     // Keep at least 1 second buffer
              liveSyncDurationCount: 3,            // Sync to 3 segment durations
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
        log('error', 'Player initialization error', err);
        setError(`Failed to initialize player`);
        setErrorDetails(String(err));
        setIsLoading(false);
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

  const toggleMute = () => {
    if (videoRef.current) {
      videoRef.current.muted = !videoRef.current.muted;
      setIsMuted(!isMuted);
    }
  };

  const toggleFullscreen = () => {
    if (videoRef.current) {
      if (document.fullscreenElement) {
        document.exitFullscreen();
      } else {
        videoRef.current.requestFullscreen().catch(() => {});
      }
    }
  };

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
                  <div className="flex items-center gap-3">
                    <Dialog.Title className="text-lg font-bold text-white">
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
                  <button
                    onClick={handleClose}
                    className="p-1 rounded-lg hover:bg-gray-700 transition-colors"
                  >
                    <XMarkIcon className="w-6 h-6 text-gray-400" />
                  </button>
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
                        setVideoReady(true);
                      }
                    }}
                    className="w-full h-full"
                    controls={false}
                    playsInline
                    autoPlay
                    crossOrigin="anonymous"
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
                    >
                      {isMuted ? (
                        <SpeakerXMarkIcon className="w-6 h-6 text-white" />
                      ) : (
                        <SpeakerWaveIcon className="w-6 h-6 text-white" />
                      )}
                    </button>
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
