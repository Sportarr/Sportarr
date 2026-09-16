import { describe, expect, it } from 'vitest';
import {
  detectStreamType,
  getDefaultPlaybackMode,
  getHlsPlaybackConfig,
  isPlaybackGenerationCurrent,
} from './streamPlaybackConfig';

describe('detectStreamType', () => {
  it('uses native playback for extensionless provider streams', () => {
    expect(detectStreamType('https://provider.example/live/user/token/200163456'))
      .toBe('native');
  });

  it('starts extensionless channel streams in FFmpeg mode to preserve the live edge', () => {
    expect(getDefaultPlaybackMode(
      'https://provider.example/live/user/token/200163456',
      24,
    )).toBe('ffmpeg');
  });

  it('keeps explicitly finite native files on the proxy path', () => {
    expect(getDefaultPlaybackMode('https://provider.example/live/event.mp4', 24))
      .toBe('proxy');
  });

  it('exposes bounded live playback profiles for runtime tuning', () => {
    const lowLatency = getHlsPlaybackConfig('low-latency');
    expect(lowLatency).toMatchObject({
      lowLatencyMode: true,
      liveSyncDuration: 3,
      liveMaxLatencyDuration: 10,
      maxBufferLength: 12,
      maxMaxBufferLength: 30,
    });
    expect(lowLatency.fragLoadPolicy.default.errorRetry).toMatchObject({
      maxNumRetry: 4,
      retryDelayMs: 750,
    });
    expect(getHlsPlaybackConfig('balanced')).toMatchObject({
      lowLatencyMode: false,
      liveSyncDuration: 6,
      liveMaxLatencyDuration: 20,
      maxBufferLength: 30,
      maxMaxBufferLength: 120,
    });
    expect(getHlsPlaybackConfig('resilient')).toMatchObject({
      liveSyncDuration: 12,
      liveMaxLatencyDuration: 40,
      maxBufferLength: 60,
      maxMaxBufferLength: 180,
    });
  });

  it('rejects stale playback completions after cleanup or a newer generation', () => {
    expect(isPlaybackGenerationCurrent(4, 4, false)).toBe(true);
    expect(isPlaybackGenerationCurrent(4, 5, false)).toBe(false);
    expect(isPlaybackGenerationCurrent(4, 4, true)).toBe(false);
  });
});
