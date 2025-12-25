import { Fragment, useRef, useEffect, useState } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { XMarkIcon, SpeakerWaveIcon, SpeakerXMarkIcon, ArrowsPointingOutIcon, PlayIcon, PauseIcon, ArrowPathIcon } from '@heroicons/react/24/outline';
import Hls from 'hls.js';
import mpegts from 'mpegts.js';

interface StreamPlayerModalProps {
  isOpen: boolean;
  onClose: () => void;
  streamUrl: string | null;
  channelId?: number;
  channelName: string;
}

type StreamType = 'hls' | 'mpegts' | 'native' | 'unknown';
type PlaybackMode = 'proxy' | 'direct';

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

function log(level: 'info' | 'warn' | 'error' | 'debug', message: string, ...args: unknown[]) {
  const timestamp = new Date().toISOString();
  const prefix = `[StreamPlayer ${timestamp}]`;
  switch (level) {
    case 'info':
      console.log(prefix, message, ...args);
      break;
    case 'warn':
      console.warn(prefix, message, ...args);
      break;
    case 'error':
      console.error(prefix, message, ...args);
      break;
    case 'debug':
      console.debug(prefix, message, ...args);
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

  // Get the stream URL to use (proxy or direct)
  const getStreamUrl = (): string | null => {
    if (!streamUrl) return null;

    if (playbackMode === 'proxy' && channelId) {
      // Use the backend proxy to avoid CORS issues
      return `/api/iptv/stream/${channelId}`;
    }

    return streamUrl;
  };

  // Cleanup function
  const cleanup = () => {
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
    setIsPlaying(false);
    setError(null);
    setErrorDetails(null);
    setIsLoading(true);
    setStreamType('unknown');
  };

  // Retry with different mode
  const handleRetry = () => {
    log('info', 'Retrying stream playback', { currentMode: playbackMode, retryCount });
    cleanup();
    setRetryCount(prev => prev + 1);

    // Toggle between proxy and direct mode on retry
    if (playbackMode === 'proxy') {
      setPlaybackMode('direct');
      log('info', 'Switching to direct mode');
    } else {
      setPlaybackMode('proxy');
      log('info', 'Switching to proxy mode');
    }
  };

  // Initialize player when modal opens
  useEffect(() => {
    if (!isOpen || !streamUrl || !videoRef.current) {
      return;
    }

    const video = videoRef.current;
    const effectiveUrl = getStreamUrl();

    if (!effectiveUrl) {
      setError('No stream URL available');
      setIsLoading(false);
      return;
    }

    const detectedType = detectStreamType(streamUrl);
    setStreamType(detectedType);
    setError(null);
    setErrorDetails(null);
    setIsLoading(true);

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

    const initializePlayer = async () => {
      try {
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
            log('debug', 'Creating MPEG-TS player');
            const player = mpegts.createPlayer({
              type: streamUrl.toLowerCase().includes('.flv') ? 'flv' : 'mpegts',
              url: effectiveUrl,
              isLive: true,
              cors: true,
            }, {
              enableWorker: true,
              enableStashBuffer: false,
              stashInitialSize: 128,
              liveBufferLatencyChasing: true,
            });

            player.on(mpegts.Events.ERROR, (errorType, errorDetail, errorInfo) => {
              log('error', 'MPEG-TS error', { errorType, errorDetail, errorInfo });
              setError(`Stream error: ${errorType}`);
              setErrorDetails(String(errorDetail));
            });

            player.on(mpegts.Events.LOADING_COMPLETE, () => {
              log('debug', 'MPEG-TS: Loading complete');
              setIsLoading(false);
            });

            player.on(mpegts.Events.MEDIA_INFO, (info) => {
              log('info', 'MPEG-TS: Media info received', info);
            });

            player.attachMediaElement(video);
            player.load();
            player.play();
            mpegtsPlayerRef.current = player;
            setIsLoading(false);
          } else {
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
  }, [isOpen, streamUrl, playbackMode, retryCount]);

  const handleClose = () => {
    cleanup();
    setPlaybackMode('proxy');
    setRetryCount(0);
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
                    <span className={`px-2 py-0.5 text-xs rounded ${playbackMode === 'proxy' ? 'bg-green-600/30 text-green-300' : 'bg-yellow-600/30 text-yellow-300'}`}>
                      {playbackMode === 'proxy' ? 'Proxy' : 'Direct'}
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
                            Retry ({playbackMode === 'proxy' ? 'try direct' : 'try proxy'})
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
                    ref={videoRef}
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
                      onClick={toggleFullscreen}
                      disabled={isLoading || !!error}
                      className="p-2 rounded-lg bg-gray-700 hover:bg-gray-600 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                    >
                      <ArrowsPointingOutIcon className="w-6 h-6 text-white" />
                    </button>
                  </div>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}
