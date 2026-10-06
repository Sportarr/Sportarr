const keyPrefix = 'sportarr-support-pending-reply:';
const commentId = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const requestKey = /^[0-9a-f]{32}$/;
export type PendingReply = { id: string; body: string }
  | { id: null; body: string; key: string; mode: 'github' | 'bot' };

function validReply(value: Partial<PendingReply>): value is PendingReply {
  if (typeof value.body !== 'string' || value.body.length < 2 || value.body.length > 5000) return false;
  if (typeof value.id === 'string') return commentId.test(value.id);
  return value.id === null && 'key' in value && typeof value.key === 'string'
    && requestKey.test(value.key) && 'mode' in value
    && (value.mode === 'github' || value.mode === 'bot');
}

export function readPendingReply(issueNumber: number): PendingReply | null {
  try {
    const value = localStorage.getItem(`${keyPrefix}${issueNumber}`);
    if (!value) return null;
    const parsed = JSON.parse(value) as Partial<PendingReply>;
    if (!validReply(parsed)) return null;
    return parsed.id === null
      ? { id: null, body: parsed.body, key: parsed.key, mode: parsed.mode }
      : { id: parsed.id, body: parsed.body };
  } catch { return null; }
}

export function savePendingReply(issueNumber: number, reply: PendingReply | null): boolean {
  try {
    if (reply && validReply(reply))
      localStorage.setItem(`${keyPrefix}${issueNumber}`, JSON.stringify(reply));
    else if (reply === null) localStorage.removeItem(`${keyPrefix}${issueNumber}`);
    else return false;
    return true;
  } catch { return false; }
}
