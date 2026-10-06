import { describe, expect, it } from 'vitest';
import { ownedIssueStatus } from '../SupportYourIssuesPage';

describe('Your issues status', () => {
  it('does not call a mirroring issue open before Discord confirms its state', () => {
    expect(ownedIssueStatus(412, 'mirroring')).toBe('Published');
    expect(ownedIssueStatus(412, 'open')).toBe('Open');
    expect(ownedIssueStatus(412, 'closed')).toBe('Closed');
    expect(ownedIssueStatus(null, 'failed')).toBe('Failed');
    expect(ownedIssueStatus(null, 'publishing')).toBe('Pending');
  });
});
