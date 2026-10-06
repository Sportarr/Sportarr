import { afterEach, describe, expect, it, vi } from 'vitest';
import { SupportApiError, supportRequest } from './supportConnection';

describe('supportRequest', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('reads the server wait time from a rejected issue response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ detail: 'Please wait before creating another issue' }),
      { status: 429, headers: { 'Content-Type': 'application/json', 'Retry-After': '558' } },
    )));

    await expect(supportRequest('/issues', { method: 'POST', body: '{}' }, null))
      .rejects.toMatchObject({ status: 429, retryAfterSeconds: 558 } satisfies Partial<SupportApiError>);
  });

  it('ignores invalid retry headers', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', {
      status: 429, headers: { 'Retry-After': 'garbage' },
    })));
    await expect(supportRequest('/issues', {}, null))
      .rejects.toMatchObject({ status: 429, retryAfterSeconds: null } satisfies Partial<SupportApiError>);
  });
});
