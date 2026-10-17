import { describe, expect, it } from 'vitest';
import { replyFitsRequest } from '../SupportIssuePage';

describe('Support reply size', () => {
  it('checks the encoded request size rather than character count', () => {
    expect(replyFitsRequest('This is a short reply.', 'github', 'a'.repeat(32))).toBe(true);
    expect(replyFitsRequest('界'.repeat(3000), 'github', 'a'.repeat(32))).toBe(false);
  });
});
