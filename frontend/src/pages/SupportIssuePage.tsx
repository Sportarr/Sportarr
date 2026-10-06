import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Link, useParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowTopRightOnSquareIcon, ChatBubbleLeftRightIcon, HandThumbUpIcon } from '@heroicons/react/24/outline';
import { readSupportIssue } from '../api/support';
import { readPendingReply, savePendingReply, type PendingReply } from './supportReplyPending';
import { useDialogFocus } from '../hooks/useDialogFocus';
import { clearSupportToken, getSupportToken, SupportApiError, supportRequest, type SupportIdentity } from '../api/supportConnection';
import SupportConnection from '../components/SupportConnection';
import SupportIssueContent, { safeLink } from '../components/SupportIssueContent';
import GitHubMark from '../components/GitHubMark';
import PageHeader from '../components/PageHeader';
import PageShell, { PageErrorState, PageLoadingState } from '../components/PageShell';
import { BADGE_AMBER, BADGE_BLUE, BADGE_GRAY, BADGE_GREEN, BADGE_RED, BUTTON_PRIMARY, BUTTON_SECONDARY } from '../utils/designTokens';

function newKey() {
  return Array.from(crypto.getRandomValues(new Uint8Array(16)), (byte) => byte.toString(16).padStart(2, '0')).join('');
}

function samePendingReply(left: PendingReply | null, right: PendingReply | null): boolean {
  if (!left || !right) return left === right;
  if (left.id !== right.id) return false;
  return left.id !== null || (right.id === null && left.key === right.key
    && left.body === right.body && left.mode === right.mode);
}

export function replyFitsRequest(body: string, mode: 'github' | 'bot', key: string): boolean {
  return new TextEncoder().encode(JSON.stringify({ body, author_mode: mode, idempotency_key: key })).length <= 8000;
}

function issueType(labels: string[]) {
  if (labels.includes('bug')) return { name: 'Bug', className: BADGE_RED };
  if (labels.includes('enhancement')) return { name: 'Feature request', className: BADGE_BLUE };
  if (labels.includes('question')) return { name: 'Question', className: BADGE_AMBER };
  return null;
}

export default function SupportIssuePage() {
  const queryClient = useQueryClient();
  const { number: parameter } = useParams();
  const number = Number(parameter);
  const issueNumberRef = useRef(number);
  const replyViewEpoch = useRef(0);
  issueNumberRef.current = number;
  const validNumber = Number.isSafeInteger(number) && number > 0;
  const [identity, setIdentity] = useState<SupportIdentity | null>(null);
  const [connectionVersion, setConnectionVersion] = useState(0);
  const [pendingReply, setPendingReply] = useState<PendingReply | null>(() => readPendingReply(number));
  const pendingReplyRef = useRef(pendingReply);
  const pendingComment = pendingReply?.id ?? null;
  const [reply, setReply] = useState(() => pendingReply?.body || '');
  const replyRef = useRef(reply);
  const externalDraft = useRef<string | null>(null);
  pendingReplyRef.current = pendingReply;
  replyRef.current = reply;
  const [replyKey, setReplyKey] = useState(() => pendingReply?.id === null ? pendingReply.key : newKey());
  const [pendingUnavailable, setPendingUnavailable] = useState(false);
  const [pendingMessage, setPendingMessage] = useState<string | null>(null);
  const [pendingFailed, setPendingFailed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [postingIssues, setPostingIssues] = useState<Set<number>>(() => new Set());
  const [error, setError] = useState<string | null>(null);
  const [confirmBot, setConfirmBot] = useState(false);
  const dialogRef = useDialogFocus(confirmBot, () => setConfirmBot(false));
  const [confirmNewReply, setConfirmNewReply] = useState(false);
  const newReplyDialogRef = useDialogFocus(confirmNewReply, () => setConfirmNewReply(false));
  const [reactionBusy, setReactionBusy] = useState(false);
  const [reactionAdded, setReactionAdded] = useState(false);
  const [reactionError, setReactionError] = useState(false);
  const [reactionMessage, setReactionMessage] = useState<string | null>(null);
  const [commentPage, setCommentPage] = useState(1);
  const handleIdentity = useCallback((value: SupportIdentity | null) => {
    setIdentity(value);
    if (value) setPendingUnavailable(false);
  }, []);
  const expireConnection = useCallback(() => {
    clearSupportToken();
    setIdentity(null);
    setConnectionVersion((current) => current + 1);
  }, []);
  const syncSavedReply = useCallback((saved: PendingReply | null) => {
    const currentPending = pendingReplyRef.current;
    if (saved && !currentPending && externalDraft.current === null && replyRef.current.trim())
      externalDraft.current = replyRef.current;
    const text = externalDraft.current ?? saved?.body ?? (currentPending ? '' : replyRef.current);
    pendingReplyRef.current = saved;
    replyRef.current = text;
    setPendingReply(saved);
    setReply(text);
    setReplyKey(saved?.id === null ? saved.key : newKey());
    setPendingMessage(null);
    setPendingFailed(false);
    if (!saved) externalDraft.current = null;
  }, []);
  const restoreDraftAfterPublication = useCallback((postedBody: string) => {
    const draft = externalDraft.current;
    externalDraft.current = null;
    setReply((current) => draft ?? (current === postedBody ? '' : current));
  }, []);
  useEffect(() => {
    replyViewEpoch.current += 1;
    setBusy(false);
    setError(null);
    setConfirmBot(false);
    setConfirmNewReply(false);
    setPendingMessage(null);
    setPendingFailed(false);
    setCommentPage(1);
    setReactionBusy(false);
    setReactionAdded(false);
    setReactionError(false);
    setReactionMessage(null);
    const saved = readPendingReply(number);
    externalDraft.current = null;
    setPendingReply(saved);
    setReply(saved?.body || '');
    setReplyKey(saved?.id === null ? saved.key : newKey());
    setPendingUnavailable(false);
    return () => { replyViewEpoch.current += 1; };
  }, [number]);
  useEffect(() => {
    const onStorage = (event: StorageEvent) => {
      if (event.key !== `sportarr-support-pending-reply:${number}`) return;
      syncSavedReply(readPendingReply(number));
      setConfirmNewReply(false);
    };
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, [number, syncSavedReply]);
  const result = useQuery({
    queryKey: ['support-issue', number, commentPage],
    queryFn: () => readSupportIssue(number, commentPage),
    enabled: validNumber,
    staleTime: 30_000,
    placeholderData: (previous, previousQuery) => previousQuery?.queryKey[1] === number ? previous : undefined,
  });

  useEffect(() => {
    if (!pendingComment || !identity || pendingUnavailable) return;
    let active = true;
    let failures = 0;
    const timer = window.setInterval(() => {
      if (!active) return;
      supportRequest<{ github_comment_id: number | null; status: 'published' | 'pending' | 'failed'; error?: string }>(`/issues/comments/${pendingComment}/status`).then((status) => {
        if (!active) return;
        failures = 0;
        if (status.github_comment_id) {
          const stored = readPendingReply(number);
          if (samePendingReply(stored, pendingReply)) {
            savePendingReply(number, null);
            setPendingReply(null);
            setPendingFailed(false);
            restoreDraftAfterPublication(pendingReply?.body || '');
            setReplyKey(newKey());
          } else {
            syncSavedReply(stored);
          }
          void queryClient.invalidateQueries({ queryKey: ['support-issue', number] });
        } else if (status.status === 'failed') {
          const stored = readPendingReply(number);
          if (samePendingReply(stored, pendingReply)) {
            setPendingFailed(true);
            setPendingMessage(status.error || 'GitHub could not confirm this reply. Check GitHub before trying again.');
          } else {
            syncSavedReply(stored);
          }
        } else setPendingMessage(status.error || null);
      }).catch((failure) => {
        if (!active) return;
        failures += 1;
        if (failure instanceof SupportApiError && failure.status === 404) {
          setPendingUnavailable(true);
          setError('This account cannot check the saved reply. Sign out of Support here and reconnect the account that posted it. The reply may still publish.');
        } else if (failure instanceof SupportApiError && failure.status === 401) {
          expireConnection();
          setError('Your account connection expired. Reconnect before checking this saved reply.');
        } else if (failures >= 3) {
          setError('Sportarr cannot check this saved reply right now. It will keep trying to post. Check GitHub before trying another reply.');
        }
      });
    }, 10000);
    return () => { active = false; window.clearInterval(timer); };
  }, [pendingComment, pendingReply?.body, pendingUnavailable, identity, number, queryClient, expireConnection, syncSavedReply, restoreDraftAfterPublication]);

  async function sendReply(mode: 'github' | 'bot', retryIntent?: Extract<PendingReply, { id: null }>) {
    const requestedNumber = number;
    const requestedEpoch = replyViewEpoch.current;
    const submittedBody = retryIntent?.body || reply.trim();
    const requestKey = retryIntent?.key || replyKey;
    if (postingIssues.has(requestedNumber)) return;
    if (pendingReply && !retryIntent) return;
    if (retryIntent && (pendingReply?.id !== null || pendingReply.key !== retryIntent.key)) return;
    const storedBeforeSend = readPendingReply(requestedNumber);
    if (!samePendingReply(storedBeforeSend, retryIntent || null)) {
      syncSavedReply(storedBeforeSend);
      setError('This issue has a saved reply from another tab. Check it before posting again.');
      return;
    }
    if (!replyFitsRequest(submittedBody, mode, requestKey)) {
      setError('This reply is too long to send. Shorten it and try again.');
      return;
    }
    const intent = { id: null, body: submittedBody, key: requestKey, mode } as const;
    if (!retryIntent && !savePendingReply(requestedNumber, intent)) {
      setError('This browser could not save your reply. Make room in browser storage and try again.');
      return;
    }
    setPendingReply(intent);
    setPendingFailed(false);
    setBusy(true);
    setPostingIssues((current) => new Set(current).add(requestedNumber));
    setError(null);
    setConfirmBot(false);
    try {
      const created = await supportRequest<{ comment_id: string; github_comment_id: number | null; status: 'published' | 'pending' | 'failed'; error?: string }>(
        `/issues/${requestedNumber}/comments`, { method: 'POST', body: JSON.stringify({
          body: submittedBody, author_mode: mode, idempotency_key: requestKey,
        }) },
      );
      if (replyViewEpoch.current !== requestedEpoch) {
        const stored = readPendingReply(requestedNumber);
        if (created.status === 'pending' && samePendingReply(stored, intent)) {
          const saved = { id: created.comment_id, body: submittedBody };
          savePendingReply(requestedNumber, saved);
        } else if (created.github_comment_id) {
          void queryClient.invalidateQueries({ queryKey: ['support-issue', requestedNumber] });
        }
        return;
      }
      const stored = readPendingReply(requestedNumber);
      if (!samePendingReply(stored, intent)) {
        syncSavedReply(stored);
        setError('The saved reply changed in another tab. Check it before posting again.');
        return;
      }
      if (created.status === 'failed') {
        const saved = { id: created.comment_id, body: submittedBody };
        savePendingReply(requestedNumber, saved);
        setPendingReply(saved);
        setPendingFailed(true);
        setPendingMessage(created.error || 'GitHub could not confirm this reply. Check GitHub before trying again.');
      } else if (created.github_comment_id) {
        savePendingReply(requestedNumber, null);
        setPendingReply(null);
        setPendingFailed(false);
        restoreDraftAfterPublication(submittedBody);
        setReplyKey(newKey());
        await queryClient.invalidateQueries({ queryKey: ['support-issue', requestedNumber] });
      } else {
        const saved = { id: created.comment_id, body: submittedBody };
        savePendingReply(requestedNumber, saved);
        setPendingReply(saved);
        setPendingFailed(false);
        setPendingMessage(created.error || null);
      }
    } catch (failure) {
      if (replyViewEpoch.current !== requestedEpoch) return;
      if (retryIntent) {
        if (failure instanceof SupportApiError && failure.status === 401) expireConnection();
        if (failure instanceof SupportApiError && [400, 403, 404, 409, 422, 423].includes(failure.status)) setPendingFailed(true);
        setError(failure instanceof Error ? failure.message : 'Could not check the saved reply.');
        return;
      }
      const rejected = failure instanceof SupportApiError && (
        [400, 401, 403, 404, 409, 422, 423, 429].includes(failure.status));
      if (rejected && samePendingReply(readPendingReply(requestedNumber), intent)) {
        savePendingReply(requestedNumber, null);
        setPendingReply(null);
        setReply(submittedBody);
        setReplyKey(newKey());
      }
      if (failure instanceof SupportApiError && failure.status === 401) expireConnection();
      if (failure instanceof SupportApiError && failure.status === 409
        && failure.message.includes('GitHub posting') && mode === 'github') setConfirmBot(true);
      setError(rejected
        ? failure.message
        : 'Sportarr could not confirm this reply. Use Check saved reply before posting another.');
    } finally {
      setPostingIssues((current) => {
        const next = new Set(current);
        next.delete(requestedNumber);
        return next;
      });
      if (replyViewEpoch.current === requestedEpoch) setBusy(false);
    }
  }

  function startAnotherReply() {
    if (!pendingReply || !samePendingReply(readPendingReply(number), pendingReply)) {
      syncSavedReply(readPendingReply(number));
      setError('The saved reply changed in another tab. Check it before posting again.');
      setConfirmNewReply(false);
      return;
    }
    if (!savePendingReply(number, null)) {
      setError('This browser could not clear the saved reply. Try again.');
      return;
    }
    setPendingReply(null);
    externalDraft.current = null;
    setPendingMessage(null);
    setPendingFailed(false);
    setReplyKey(newKey());
    setConfirmNewReply(false);
  }

  async function addReaction() {
    const requestedNumber = number;
    const requestedToken = getSupportToken();
    setReactionBusy(true);
    setReactionError(false);
    setReactionMessage(null);
    try {
      await supportRequest(`/issues/${requestedNumber}/reactions`, {
        method: 'POST', body: JSON.stringify({ content: '+1' }),
      });
      if (issueNumberRef.current === requestedNumber) {
        setReactionAdded(true);
        setReactionMessage('Your thumbs-up was added.');
      }
      await queryClient.invalidateQueries({ queryKey: ['support-issue', requestedNumber] });
    } catch (failure) {
      if (failure instanceof SupportApiError && failure.status === 401 && getSupportToken() === requestedToken) expireConnection();
      if (issueNumberRef.current === requestedNumber) {
        setReactionError(true);
        setReactionMessage(failure instanceof Error ? failure.message : 'Could not add your thumbs-up.');
      }
    } finally {
      if (issueNumberRef.current === requestedNumber) setReactionBusy(false);
    }
  }

  if (!validNumber) return <PageErrorState title="Issue not found" message="This issue number is not valid." />;
  if (result.isLoading) return <PageLoadingState label="Loading issue..." />;
  if (result.isError || !result.data) {
    return <PageErrorState title="Could not load issue" message="Issues are unavailable right now. Try again shortly." action={<Link to="/support" className={BUTTON_SECONDARY}>Browse issues</Link>} />;
  }

  const { issue, comments } = result.data;
  const kind = issueType(issue.labels);
  const reactionTitle = kind?.name === 'Bug' ? 'Add a GitHub thumbs-up to show this affects you too'
    : kind?.name === 'Feature request' ? 'Add a GitHub thumbs-up to support this request'
    : kind?.name === 'Question' ? 'Add a GitHub thumbs-up if you have this question too'
    : 'Add a GitHub thumbs-up';
  const githubUrl = safeLink(issue.url);

  return (
    <PageShell>
      <div className="mx-auto max-w-4xl">
        <Link to="/support" className="mb-4 inline-flex min-h-11 items-center text-sm text-gray-400 hover:text-white">Back to issues</Link>
        <PageHeader
          title={issue.title}
          subtitle={`#${issue.number} opened by ${issue.author}`}
          icon={GitHubMark}
          actions={githubUrl &&
            <a href={githubUrl} target="_blank" rel="noopener noreferrer" className={BUTTON_SECONDARY}>
              View on GitHub <ArrowTopRightOnSquareIcon className="h-4 w-4" />
            </a>
          }
        />

        <div className="mb-4 flex flex-wrap items-center gap-2 text-sm">
          <span className={issue.state === 'open' ? BADGE_GREEN : BADGE_GRAY}>{issue.state === 'open' ? 'Open' : 'Closed'}</span>
          {kind && <span className={kind.className}>{kind.name}</span>}
          {issue.labels.filter((label) => !['bug', 'enhancement', 'question'].includes(label)).map((label) => (
            <span key={label} className={BADGE_GRAY}>{label}</span>
          ))}
          <button type="button" disabled={!identity?.github_posting || reactionBusy || reactionAdded} onClick={() => void addReaction()}
            title={identity?.github_posting ? reactionTitle : 'Connect GitHub to react'}
            aria-describedby={!identity?.github_posting ? 'support-reaction-purpose support-reaction-count support-reaction-connect' : 'support-reaction-purpose support-reaction-count'}
            className="ml-auto inline-flex min-h-11 items-center gap-2 rounded-lg px-2 text-gray-300 hover:bg-gray-800 disabled:cursor-not-allowed disabled:opacity-50">
            <HandThumbUpIcon className="h-4 w-4" />Same here
          </button>
          <span id="support-reaction-purpose" className="sr-only">{reactionTitle}.</span>
          <span id="support-reaction-count" className="text-xs text-gray-400">{issue.reactions.thumbs_up} GitHub thumbs-up {issue.reactions.thumbs_up === 1 ? 'reaction' : 'reactions'}</span>
          <span className="inline-flex items-center gap-1 text-gray-400"><ChatBubbleLeftRightIcon className="h-4 w-4" aria-hidden="true" />{issue.comment_count}<span className="sr-only"> {issue.comment_count === 1 ? 'comment' : 'comments'}</span></span>
        </div>

        {!identity?.github_posting && <p id="support-reaction-connect" className="mb-3 text-right text-xs text-gray-400">Connect GitHub below to react.</p>}
        {reactionMessage && <p role={reactionError ? 'alert' : 'status'} className={`mb-3 text-sm ${reactionError ? 'text-red-300' : 'text-gray-300'}`}>{reactionMessage}</p>}

        <div className="rounded-lg border border-red-900/30 bg-gradient-to-br from-gray-900 to-black p-4 sm:p-6">
          <p className="mb-3 text-sm font-medium text-gray-300">{issue.author} reported</p>
          <SupportIssueContent body={issue.body} />
        </div>

        <div className="mt-6 space-y-3">
          <h2 className="text-lg font-bold text-white">Conversation</h2>
          {result.isFetching && <p role="status" className="text-sm text-gray-400">Loading replies...</p>}
          {comments.length === 0 && !result.isFetching && <p className="rounded-lg border border-gray-800 bg-black/30 p-4 text-sm text-gray-400">No replies yet.</p>}
          {comments.map((comment) => (
            <div key={comment.id} className="rounded-lg border border-gray-800 bg-black/30 p-4 sm:p-5">
              <div className="mb-3 flex flex-wrap items-center justify-between gap-2 text-xs text-gray-400">
                <span className="font-medium text-gray-200">{comment.author}</span>
                <time dateTime={comment.created_at}>{new Date(comment.created_at).toLocaleString()}</time>
              </div>
              <SupportIssueContent body={comment.body} decodeEntities={false} />
            </div>
          ))}
          {issue.comment_count > 30 && <div className="flex flex-wrap items-center justify-between gap-3 pt-2 text-sm text-gray-400">
            <span>Page {commentPage} of {Math.ceil(issue.comment_count / 30)}</span>
            <div className="flex gap-2">
              <button type="button" className={BUTTON_SECONDARY} disabled={commentPage === 1} onClick={() => setCommentPage(commentPage - 1)}>Previous</button>
              <button type="button" className={BUTTON_SECONDARY} disabled={commentPage * 30 >= issue.comment_count} onClick={() => setCommentPage(commentPage + 1)}>Next</button>
            </div>
          </div>}
        </div>

        <section className="mt-6 rounded-lg border border-red-900/30 bg-gradient-to-br from-gray-900 to-black p-4 sm:p-5">
          <h2 className="font-bold text-white">Reply to this issue</h2>
          <p className="mt-1 text-sm text-gray-400">Your reply and connected account name will be public on GitHub and in the linked Discord discussion, when there is one.</p>
          {pendingComment && !identity && <p role="status" className="mt-3 text-sm text-amber-200">A saved reply is still pending. Reconnect to check whether it posted before writing another.</p>}
          <div className="mt-4"><SupportConnection key={connectionVersion} onIdentityChange={handleIdentity} /></div>
          {error && !identity && <p role="alert" className="mt-3 text-sm text-red-300">{error}</p>}
          {identity && <form className="mt-4" onSubmit={(event) => {
            event.preventDefault();
            if (reply.trim().length < 2) {
              setError('Please write a reply before posting.');
              return;
            }
            if (identity.github_posting) void sendReply('github');
            else if (identity.connected_providers.includes('github')) setConfirmBot(true);
            else void sendReply('bot');
          }}>
            <label htmlFor="support-reply" className="mb-2 block text-sm text-gray-300">Your reply</label>
            <textarea id="support-reply" required minLength={2} maxLength={5000} rows={5} value={reply} disabled={!!pendingReply}
              onChange={(event) => setReply(event.target.value)} className="w-full rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-white focus:border-red-600 focus:outline-none focus:ring-1 focus:ring-red-500" />
            {error && <p role="alert" className="mt-2 text-sm text-red-300">{error}</p>}
            {pendingReply?.id === null && <div role="status" className="mt-2 text-sm text-amber-200">
              <p>{pendingFailed ? 'Your reply could not be confirmed. Check GitHub before trying again.' : 'Reply saved. Posting is not confirmed yet. Check it before writing another.'}</p>
              {!pendingFailed && <div className="mt-3"><button type="button" disabled={busy || postingIssues.has(number)} onClick={() => void sendReply(pendingReply.mode, pendingReply)} className={`${BUTTON_SECONDARY} w-full sm:w-auto`}>Check saved reply</button></div>}
            </div>}
            {pendingComment && <p role="status" className="mt-2 text-sm text-amber-200">{pendingFailed ? 'Your reply could not be confirmed. Check GitHub before trying again.' : 'Your reply is saved. Sportarr will keep trying to post it.'}{pendingMessage && <span className="mt-1 block">{pendingMessage}</span>}</p>}
            {pendingReply && pendingFailed && <button type="button" className={`${BUTTON_SECONDARY} mt-3 w-full sm:w-auto`} onClick={() => setConfirmNewReply(true)}>Start another reply</button>}
            <div className="mt-3 flex justify-end"><button type="submit" disabled={busy || postingIssues.has(number) || !!pendingReply} className={BUTTON_PRIMARY}>{busy || postingIssues.has(number) ? 'Posting...' : 'Post reply'}</button></div>
          </form>}
        </section>

        {confirmBot && createPortal(<div ref={dialogRef} role="dialog" aria-modal="true" aria-label="Choose reply author" className="fixed inset-0 z-[60] flex items-end bg-black/70 sm:items-center sm:justify-center sm:p-4">
          <div className="w-full rounded-t-2xl border border-red-900/40 bg-gray-900 p-5 pb-[calc(env(safe-area-inset-bottom)+1.25rem)] shadow-2xl sm:max-w-md sm:rounded-2xl sm:pb-5">
            <h2 className="text-lg font-bold text-white">Choose how to reply</h2>
            <p className="mt-2 text-sm text-gray-300">GitHub posting permission was not completed. Finish it in the account card to reply under your GitHub name, or reply through Sportarr now.</p>
            <div className="mt-5 flex flex-col gap-2">
              <button type="button" className={BUTTON_PRIMARY} onClick={() => void sendReply('bot')}>Reply through Sportarr</button>
              <button type="button" className={BUTTON_SECONDARY} onClick={() => setConfirmBot(false)}>Keep editing</button>
            </div>
          </div>
        </div>, document.body)}
        {confirmNewReply && createPortal(<div ref={newReplyDialogRef} role="dialog" aria-modal="true" aria-label="Check before starting another reply" className="fixed inset-0 z-[60] flex items-end bg-black/70 sm:items-center sm:justify-center sm:p-4">
          <div className="w-full rounded-t-2xl border border-red-900/40 bg-gray-900 p-5 pb-[calc(env(safe-area-inset-bottom)+1.25rem)] shadow-2xl sm:max-w-md sm:rounded-2xl sm:pb-5">
            <h2 className="text-lg font-bold text-white">Check GitHub first</h2>
            <p className="mt-2 text-sm text-gray-300">Sportarr could not confirm whether your reply posted. Check the issue on GitHub before starting another one, or the same message may appear twice.</p>
            <div className="mt-5 flex flex-col gap-2">
              {githubUrl && <a href={githubUrl} target="_blank" rel="noopener noreferrer" className={BUTTON_SECONDARY}>View issue on GitHub</a>}
              <button type="button" className={BUTTON_PRIMARY} onClick={startAnotherReply}>I checked GitHub, start again</button>
              <button type="button" className={BUTTON_SECONDARY} onClick={() => setConfirmNewReply(false)}>Keep saved reply</button>
            </div>
          </div>
        </div>, document.body)}
      </div>
    </PageShell>
  );
}
