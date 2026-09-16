export type HlsPlaybackProfile = 'low-latency' | 'balanced' | 'resilient';

export type StreamType = 'hls' | 'mpegts' | 'native' | 'unknown';
export type PlaybackMode = 'proxy' | 'direct' | 'ffmpeg';

interface HlsRetryConfig {
  maxNumRetry: number;
  retryDelayMs: number;
  maxRetryDelayMs: number;
}

interface HlsLoadPolicy {
  default: {
    maxTimeToFirstByteMs: number;
    maxLoadTimeMs: number;
    timeoutRetry: HlsRetryConfig;
    errorRetry: HlsRetryConfig;
  };
}

export interface HlsPlaybackConfig {
  backBufferLength: number;
  maxBufferLength: number;
  maxMaxBufferLength: number;
  maxBufferSize: number;
  maxBufferHole: number;
  highBufferWatchdogPeriod: number;
  liveSyncDuration: number;
  liveMaxLatencyDuration: number;
  lowLatencyMode: boolean;
  manifestLoadPolicy: HlsLoadPolicy;
  playlistLoadPolicy: HlsLoadPolicy;
  fragLoadPolicy: HlsLoadPolicy;
}

function createLoadPolicy(timeout: number, maxNumRetry: number, retryDelayMs: number): HlsLoadPolicy {
  const retry = {
    maxNumRetry,
    retryDelayMs,
    maxRetryDelayMs: retryDelayMs * 8,
  };

  return {
    default: {
      maxTimeToFirstByteMs: timeout,
      maxLoadTimeMs: timeout,
      timeoutRetry: { ...retry },
      errorRetry: { ...retry },
    },
  };
}

const HLS_PLAYBACK_CONFIGS: Record<HlsPlaybackProfile, HlsPlaybackConfig> = {
  'low-latency': {
    backBufferLength: 15,
    maxBufferLength: 12,
    maxMaxBufferLength: 30,
    maxBufferSize: 30 * 1000 * 1000,
    maxBufferHole: 0.5,
    highBufferWatchdogPeriod: 2,
    liveSyncDuration: 3,
    liveMaxLatencyDuration: 10,
    lowLatencyMode: true,
    manifestLoadPolicy: createLoadPolicy(10_000, 3, 750),
    playlistLoadPolicy: createLoadPolicy(10_000, 3, 750),
    fragLoadPolicy: createLoadPolicy(15_000, 4, 750),
  },
  balanced: {
    backBufferLength: 30,
    maxBufferLength: 30,
    maxMaxBufferLength: 120,
    maxBufferSize: 60 * 1000 * 1000,
    maxBufferHole: 1.0,
    highBufferWatchdogPeriod: 3,
    liveSyncDuration: 6,
    liveMaxLatencyDuration: 20,
    lowLatencyMode: false,
    manifestLoadPolicy: createLoadPolicy(20_000, 4, 1_000),
    playlistLoadPolicy: createLoadPolicy(20_000, 4, 1_000),
    fragLoadPolicy: createLoadPolicy(30_000, 6, 1_000),
  },
  resilient: {
    backBufferLength: 60,
    maxBufferLength: 60,
    maxMaxBufferLength: 180,
    maxBufferSize: 100 * 1000 * 1000,
    maxBufferHole: 1.5,
    highBufferWatchdogPeriod: 5,
    liveSyncDuration: 12,
    liveMaxLatencyDuration: 40,
    lowLatencyMode: false,
    manifestLoadPolicy: createLoadPolicy(30_000, 6, 1_500),
    playlistLoadPolicy: createLoadPolicy(30_000, 6, 1_500),
    fragLoadPolicy: createLoadPolicy(45_000, 8, 1_500),
  },
};

export function getHlsPlaybackConfig(profile: HlsPlaybackProfile): HlsPlaybackConfig {
  return { ...HLS_PLAYBACK_CONFIGS[profile] };
}

export function isPlaybackGenerationCurrent(
  generation: number,
  currentGeneration: number,
  cancelled: boolean,
): boolean {
  return !cancelled && generation === currentGeneration;
}

export function detectStreamType(url: string): StreamType {
  const lowerUrl = url.toLowerCase();

  if (lowerUrl.includes('.m3u8') || lowerUrl.includes('m3u8')) {
    return 'hls';
  }

  if (lowerUrl.includes('.ts') || lowerUrl.includes('/ts/') || lowerUrl.includes('mpegts')) {
    return 'mpegts';
  }

  if (lowerUrl.includes('.flv')) {
    return 'mpegts';
  }

  if (lowerUrl.includes('.mp4') || lowerUrl.includes('.webm') || lowerUrl.includes('.ogg')) {
    return 'native';
  }

  return 'native';
}

export function getDefaultPlaybackMode(url: string, channelId?: number): PlaybackMode {
  const isExplicitNativeFile = /\.(mp4|webm|ogg)(?:[?#]|$)/i.test(url);

  if (channelId != null && detectStreamType(url) === 'native' && !isExplicitNativeFile) {
    return 'ffmpeg';
  }

  return 'proxy';
}
