import { beforeEach, describe, expect, it } from 'vitest';
import { readPendingReply, savePendingReply } from '../supportReplyPending';

describe('Pending support replies', () => {
  beforeEach(() => localStorage.clear());

  it('keeps an accepted comment across reloads and isolates issue numbers', () => {
    const commentId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
    savePendingReply(412, { id: commentId, body: 'The import still fails.' });

    expect(readPendingReply(412)).toEqual({ id: commentId, body: 'The import still fails.' });
    expect(readPendingReply(413)).toBeNull();

    savePendingReply(412, null);
    expect(readPendingReply(412)).toBeNull();
  });

  it('keeps an unconfirmed send and its request key across reloads', () => {
    const intent = { id: null, body: 'The import still fails.', key: 'a'.repeat(32), mode: 'github' as const };
    expect(savePendingReply(412, intent)).toBe(true);
    expect(readPendingReply(412)).toEqual(intent);
    expect(readPendingReply(413)).toBeNull();
  });

  it('rejects a saved send without a valid request key', () => {
    localStorage.setItem('sportarr-support-pending-reply:412', JSON.stringify({
      id: null, body: 'The import still fails.', key: '../other', mode: 'github',
    }));
    expect(readPendingReply(412)).toBeNull();
  });

  it('ignores a malformed stored comment identifier', () => {
    localStorage.setItem('sportarr-support-pending-reply:412', JSON.stringify({ id: '../other', body: 'Text' }));

    expect(readPendingReply(412)).toBeNull();
  });
});
