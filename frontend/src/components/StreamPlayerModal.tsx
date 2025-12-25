import { Fragment, useRef, useEffect, useState } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { XMarkIcon, SpeakerWaveIcon, SpeakerXMarkIcon, ArrowsPointingOutIcon, PlayIcon, PauseIcon } from '@heroicons/react/24/outline';
import Hls from 'hls.js';
import mpegts from 'mpegts.js';

interface StreamPlayerModalProps {
  isOpen: boolean;
  onClose: () => void;
  streamUrl: string | null;
  channelName: string;
}

type StreamType = 'hls' | 'mpegts' | 'native' | 'unknown';

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

export default function StreamPlayerModal({
  isOpen,
  onClose,
  streamUrl,
  channelName,
}: StreamPlayerModalProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const hlsRef = useRef<Hls | null>(null);
  const mpegtsPlayerRef = useRef<mpegts.Player | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [isMuted, setIsMuted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [streamType, setStreamType] = useState<StreamType>('unknown');

  // Cleanup function
  const cleanup = () => {
    if (hlsRef.current) {
      hlsRef.current.destroy();
      hlsRef.current = null;
    }
    if (mpegtsPlayerRef.current) {
      mpegtsPlayerRef.current.destroy();
      mpegtsPlayerRef.current = null;
    }
    if (videoRef.current) {
      videoRef.current.src = '';
      videoRef.current.load();
    }
    setIsPlaying(false);
    setError(null);
    setIsLoading(true);
    setStreamType('unknown');
  };

  // Initialize player when modal opens
  useEffect(() => {
    if (!isOpen || !streamUrl || !videoRef.current) {
      return;
    }

    const video = videoRef.current;
    const detectedType = detectStreamType(streamUrl);
    setStreamType(detectedType);
    setError(null);
    setIsLoading(true);

    const initializePlayer = async () => {
      try {
        if (detectedType === 'hls') {
          if (Hls.isSupported()) {
            const hls = new Hls({
              enableWorker: true,
              lowLatencyMode: true,
              backBufferLength: 90,
            });

            hls.on(Hls.Events.MEDIA_ATTACHED, () => {
              hls.loadSource(streamUrl);
            });

            hls.on(Hls.Events.MANIFEST_PARSED, () => {
              setIsLoading(false);
              video.play().catch((e) => {
                console.warn('Autoplay blocked:', e);
                setIsPlaying(false);
              });
            });

            hls.on(Hls.Events.ERROR, (_, data) => {
              if (data.fatal) {
                switch (data.type) {
                  case Hls.ErrorTypes.NETWORK_ERROR:
                    setError('Network error - stream may be offline or blocked');
                    hls.startLoad();
                    break;
                  case Hls.ErrorTypes.MEDIA_ERROR:
                    setError('Media error - trying to recover...');
                    hls.recoverMediaError();
                    break;
                  default:
                    setError(`Fatal error: ${data.details}`);
                    cleanup();
                    break;
                }
              }
            });

            hls.attachMedia(video);
            hlsRef.current = hls;
          } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
            // Native HLS support (Safari)
            video.src = streamUrl;
            video.addEventListener('loadedmetadata', () => {
              setIsLoading(false);
              video.play().catch(() => setIsPlaying(false));
            });
          } else {
            setError('HLS is not supported in this browser');
            setIsLoading(false);
          }
        } else if (detectedType === 'mpegts') {
          if (mpegts.isSupported()) {
            const player = mpegts.createPlayer({
              type: streamUrl.toLowerCase().includes('.flv') ? 'flv' : 'mpegts',
              url: streamUrl,
              isLive: true,
            }, {
              enableWorker: true,
              enableStashBuffer: false,
              stashInitialSize: 128,
              liveBufferLatencyChasing: true,
            });

            player.on(mpegts.Events.ERROR, (errorType, errorDetail) => {
              setError(`Stream error: ${errorType} - ${errorDetail}`);
            });

            player.on(mpegts.Events.LOADING_COMPLETE, () => {
              setIsLoading(false);
            });

            player.attachMediaElement(video);
            player.load();
            player.play();
            mpegtsPlayerRef.current = player;
            setIsLoading(false);
          } else {
            setError('MPEG-TS/FLV is not supported in this browser');
            setIsLoading(false);
          }
        } else if (detectedType === 'native') {
          video.src = streamUrl;
          video.addEventListener('loadedmetadata', () => {
            setIsLoading(false);
            video.play().catch(() => setIsPlaying(false));
          });
          video.addEventListener('error', () => {
            setError('Failed to load video');
            setIsLoading(false);
          });
        } else {
          // Try HLS as default
          if (Hls.isSupported()) {
            const hls = new Hls();
            hls.on(Hls.Events.ERROR, (_, data) => {
              if (data.fatal) {
                setError('Could not play this stream format');
                setIsLoading(false);
              }
            });
            hls.loadSource(streamUrl);
            hls.attachMedia(video);
            hlsRef.current = hls;
          } else {
            video.src = streamUrl;
          }
        }
      } catch (err) {
        console.error('Player initialization error:', err);
        setError(`Failed to initialize player: ${err}`);
        setIsLoading(false);
      }
    };

    initializePlayer();

    // Video event listeners
    const handlePlay = () => setIsPlaying(true);
    const handlePause = () => setIsPlaying(false);
    const handleError = () => {
      if (!error) {
        setError('Failed to play stream');
      }
      setIsLoading(false);
    };
    const handleCanPlay = () => setIsLoading(false);

    video.addEventListener('play', handlePlay);
    video.addEventListener('pause', handlePause);
    video.addEventListener('error', handleError);
    video.addEventListener('canplay', handleCanPlay);

    return () => {
      video.removeEventListener('play', handlePlay);
      video.removeEventListener('pause', handlePause);
      video.removeEventListener('error', handleError);
      video.removeEventListener('canplay', handleCanPlay);
      cleanup();
    };
  }, [isOpen, streamUrl]);

  const handleClose = () => {
    cleanup();
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
                      </div>
                    </div>
                  )}

                  {error && (
                    <div className="absolute inset-0 flex items-center justify-center bg-black/80">
                      <div className="text-center p-6">
                        <div className="text-red-400 text-lg mb-2">Stream Error</div>
                        <div className="text-gray-400 text-sm max-w-md">{error}</div>
                        <div className="mt-4 text-xs text-gray-500">
                          URL: {streamUrl?.substring(0, 50)}...
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
