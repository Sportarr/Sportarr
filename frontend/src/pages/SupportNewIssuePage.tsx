import { useCallback, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { DocumentArrowUpIcon, PlusIcon, XMarkIcon } from '@heroicons/react/24/outline';
import { clearSupportToken, SupportApiError, supportRequest, type SupportIdentity } from '../api/supportConnection';
import { useDialogFocus } from '../hooks/useDialogFocus';
import SupportConnection from '../components/SupportConnection';
import GitHubMark from '../components/GitHubMark';
import PageHeader from '../components/PageHeader';
import PageShell from '../components/PageShell';
import { BUTTON_PRIMARY, BUTTON_SECONDARY, OPTION_CARD_SELECTED, OPTION_CARD_UNSELECTED } from '../utils/designTokens';

type Category = 'bug' | 'feature' | 'question';
type Draft = { category: Category; title: string; primary: string; secondary: string; tertiary: string; autoFilledVersion: string | null; idempotencyKey: string; fileNames: string[] };

const draftKey = 'sportarr-support-issue-draft';
const pendingKey = 'sportarr-support-pending-report';
let fileDraft: { key: string; files: File[] } | null = null;
const allowedExtensions = ['.txt', '.log', '.json', '.yml', '.yaml', '.csv', '.md', '.png', '.jpg', '.jpeg', '.gif', '.webp'];
const inputClass = 'w-full rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-white focus:border-red-600 focus:outline-none focus:ring-1 focus:ring-red-500';
const forms = {
  bug: {
    name: 'Bug', title: 'Report a bug', description: 'Something is not working as expected.',
    primary: 'What happened', primaryHint: 'What did you expect, and what happened instead?',
    secondary: 'Steps to reproduce', secondaryHint: 'List the steps that lead to the problem.',
    tertiary: 'Sportarr version and install type', tertiaryHint: 'For example, 4.1.6.805 / Docker on Unraid',
    secondaryRequired: false, tertiaryRequired: true,
  },
  feature: {
    name: 'Feature request', title: 'Request a feature', description: 'Suggest a way to improve Sportarr.',
    primary: 'Problem or goal', primaryHint: 'What are you trying to do, and what is missing?',
    secondary: 'What should Sportarr do?', secondaryHint: 'Describe the change you would like to see.',
    tertiary: 'Example or alternative', tertiaryHint: 'Optional example, workaround, or alternative you considered.',
    secondaryRequired: true, tertiaryRequired: false,
  },
  question: {
    name: 'Question', title: 'Ask a question', description: 'Get help from the community.',
    primary: 'Your question', primaryHint: 'What would you like to know or get help with?',
    secondary: "Context or what you've tried", secondaryHint: 'Optional details that may help someone answer.',
    tertiary: 'Version and install type', tertiaryHint: 'Optional, for example, 4.1.6.805 / Docker on Unraid',
    secondaryRequired: false, tertiaryRequired: false,
  },
} as const;

function newKey() {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
}

function readDraft(): Draft {
  try {
    const value = JSON.parse(localStorage.getItem(draftKey) || '{}') as Partial<Draft>;
    if (value.category && value.category in forms) {
      return { category: value.category, title: value.title || '', primary: value.primary || '',
        secondary: value.secondary || '', tertiary: value.tertiary || '',
        autoFilledVersion: typeof value.autoFilledVersion === 'string' ? value.autoFilledVersion : null,
        idempotencyKey: value.idempotencyKey || newKey(),
        fileNames: Array.isArray(value.fileNames) ? value.fileNames.filter((name): name is string => typeof name === 'string').slice(0, 12) : [] };
    }
  } catch { /* An invalid saved draft starts fresh. */ }
  return { category: 'bug', title: '', primary: '', secondary: '', tertiary: '', autoFilledVersion: null, idempotencyKey: newKey(), fileNames: [] };
}

function encodeFile(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result;
      if (typeof result !== 'string' || !result.includes(',')) reject(new Error('Could not read the selected file.'));
      else resolve(result.slice(result.indexOf(',') + 1));
    };
    reader.onerror = () => reject(new Error('Could not read the selected file.'));
    reader.readAsDataURL(file);
  });
}

function waitDescription(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  const remainder = seconds % 60;
  const parts = [];
  if (minutes) parts.push(`${minutes} ${minutes === 1 ? 'minute' : 'minutes'}`);
  if (remainder) parts.push(`${remainder} ${remainder === 1 ? 'second' : 'seconds'}`);
  return parts.join(' ');
}

function FullTextLink({ file }: { file: File }) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    const next = URL.createObjectURL(new Blob([file], { type: 'text/plain' }));
    setUrl(next);
    return () => URL.revokeObjectURL(next);
  }, [file]);
  if (!url) return null;
  return <a href={url} target="_blank" rel="noopener noreferrer" className="text-xs text-red-300 underline hover:text-red-200">Review full text</a>;
}

export default function SupportNewIssuePage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [draft, setDraft] = useState<Draft>(readDraft);
  const [files, setFiles] = useState<File[]>(() => fileDraft?.key === draft.idempotencyKey ? fileDraft.files : []);
  const [diagnosticPreview, setDiagnosticPreview] = useState<string | null>(null);
  const [handoffMessage, setHandoffMessage] = useState<string | null>(null);
  const [identity, setIdentity] = useState<SupportIdentity | null>(null);
  const [connectionVersion, setConnectionVersion] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirmBot, setConfirmBot] = useState(false);
  const dialogRef = useDialogFocus(confirmBot, () => setConfirmBot(false));
  const [pendingReport, setPendingReport] = useState<string | null>(() => localStorage.getItem(pendingKey));
  const [pendingMessage, setPendingMessage] = useState<string | null>(null);
  const form = forms[draft.category];
  const missingFiles = draft.fileNames.filter((name) => !files.some((file) => file.name === name));
  const handleIdentity = useCallback((value: SupportIdentity | null) => setIdentity(value), []);

  useEffect(() => {
    localStorage.setItem(draftKey, JSON.stringify(draft));
  }, [draft]);
  useEffect(() => {
    fileDraft = { key: draft.idempotencyKey, files };
  }, [draft.idempotencyKey, files]);
  useEffect(() => {
    if (pendingReport) localStorage.setItem(pendingKey, pendingReport);
    else localStorage.removeItem(pendingKey);
  }, [pendingReport]);

  useEffect(() => {
    const supportLog = files.find((file) => file.name.startsWith('sportarr-support-'));
    if (!supportLog) {
      setDiagnosticPreview(null);
      return;
    }
    let active = true;
    void supportLog.text().then((content) => {
      if (active) setDiagnosticPreview(content.slice(0, 16000));
    }).catch(() => {
      if (active) setError('Could not preview the diagnostic log. Review the file before submitting.');
    });
    return () => { active = false; };
  }, [files]);

  useEffect(() => {
    const handoff = location.state as { diagnosticFiles?: File[]; versionAndInstallType?: string } | null;
    const incoming = handoff?.diagnosticFiles;
    if (handoff?.versionAndInstallType && !draft.tertiary.trim() && draft.category !== 'feature') {
      const version = handoff.versionAndInstallType.slice(0, 300);
      setDraft((current) => current.tertiary.trim() || current.category === 'feature'
        ? current : { ...current, tertiary: version, autoFilledVersion: version });
    }
    if (Array.isArray(incoming) && incoming.every((file) => file instanceof File)) {
      const supportParts = incoming.filter((file) => file.name.startsWith('sportarr-support-'));
      const keptFiles = supportParts.length
        ? files.filter((file) => !file.name.startsWith('sportarr-support-')) : files;
      const supportBytes = supportParts.reduce((size, file) => size + file.size, 0);
      if (supportParts.some((file) => file.size > 10 * 1024 * 1024)
        || keptFiles.length + supportParts.length > 12
        || keptFiles.reduce((size, file) => size + file.size, 0) + supportBytes > 25 * 1024 * 1024) {
        setError('The new diagnostic log could not be added. Your earlier files are still attached.');
        navigate(location.pathname + location.search, { replace: true, state: null });
        return;
      }
      const merged = [...keptFiles];
      let duplicateSystemInfo = 0;
      let rejected = 0;
      let addedSystemInfo = false;
      let addedSupportLog = false;
      for (const file of incoming) {
        const existingIndex = merged.findIndex((item) => item.name === file.name);
        if (file.name === 'sportarr-system-info.txt' && existingIndex >= 0) {
          duplicateSystemInfo += 1;
          continue;
        }
        const replaceLog = file.name.startsWith('sportarr-support-') && existingIndex >= 0;
        if (!replaceLog && merged.some((item) => item.name === file.name && item.size === file.size
          && item.lastModified === file.lastModified)) continue;
        const total = merged.reduce((size, item) => size + item.size, 0)
          - (replaceLog ? merged[existingIndex].size : 0) + file.size;
        if (file.size > 10 * 1024 * 1024 || total > 25 * 1024 * 1024
          || merged.length + (replaceLog ? 0 : 1) > 12) {
          rejected += 1;
          continue;
        }
        if (replaceLog) merged[existingIndex] = file;
        else merged.push(file);
        if (file.name === 'sportarr-system-info.txt') addedSystemInfo = true;
        if (file.name.startsWith('sportarr-support-')) addedSupportLog = true;
      }
      if (rejected) setError('Some diagnostic files could not be added. Remove another file and try again.');
      setFiles(merged);
      setDraft((current) => ({ ...current, fileNames: Array.from(new Set([
        ...current.fileNames.filter((name) => !supportParts.length || !name.startsWith('sportarr-support-')),
        ...merged.map((file) => file.name),
      ])) }));
      const existingDraft = Boolean(draft.title.trim() || draft.primary.trim() || draft.fileNames.length);
      if (addedSystemInfo) {
        setHandoffMessage(existingDraft
          ? 'System info added to your existing draft. Review it before submitting.'
          : 'System info attached. Review it before submitting.');
      } else if (duplicateSystemInfo) {
        setHandoffMessage('System info is already attached. Remove it first to add a new copy.');
      } else if (addedSupportLog) {
        setHandoffMessage(existingDraft
          ? 'Diagnostic log added to your existing draft. Review it before submitting.'
          : 'Diagnostic log attached. Review it before submitting.');
      }
    }
    if (handoff) navigate(location.pathname + location.search, { replace: true, state: null });
  }, [location.pathname, location.search, location.state, navigate]);

  const expireConnection = useCallback(() => {
    clearSupportToken();
    setIdentity(null);
    setConnectionVersion((current) => current + 1);
  }, []);

  function update(field: keyof Draft, value: string) {
    setDraft((current) => {
      if (field === 'tertiary') return { ...current, tertiary: value, autoFilledVersion: null };
      if (field === 'category' && current.autoFilledVersion) {
        if (value === 'feature' && current.tertiary === current.autoFilledVersion) {
          return { ...current, category: 'feature', tertiary: '' };
        }
        if (current.category === 'feature' && value !== 'feature' && !current.tertiary.trim()) {
          return { ...current, category: value as Category, tertiary: current.autoFilledVersion };
        }
      }
      return { ...current, [field]: value };
    });
  }

  function addFiles(incoming: FileList | null) {
    if (!incoming) return;
    const next = [...files];
    for (const file of Array.from(incoming)) {
      const lower = file.name.toLowerCase();
      if (!allowedExtensions.some((extension) => lower.endsWith(extension))) {
        setError(`${file.name} cannot be attached. Export a plain-text log instead of an archive.`);
        continue;
      }
      if (next.length >= 12) {
        setError('You can attach up to 12 files to one issue. Remove a file before adding another.');
        continue;
      }
      if (file.size > 10 * 1024 * 1024 || next.reduce((size, item) => size + item.size, 0) + file.size > 25 * 1024 * 1024) {
        setError(`${file.name} is too large. The limit is 10 MB per file and 25 MB per issue.`);
        continue;
      }
      next.push(file);
    }
    setFiles(next);
    setDraft((current) => ({ ...current, fileNames: Array.from(new Set([
      ...current.fileNames, ...next.map((file) => file.name),
    ])) }));
  }

  async function createIssue(mode: 'github' | 'bot') {
    setError(null);
    setConfirmBot(false);
    setBusy(true);
    try {
      const attachments = await Promise.all(files.map(async (file) => ({
        filename: file.name, content_base64: await encodeFile(file),
      })));
      const created = await supportRequest<{ report_id: string; number: number | null; status: 'published' | 'pending' | 'failed'; error?: string }>(
        '/issues', { method: 'POST', body: JSON.stringify({
          category: draft.category, title: draft.title, primary: draft.primary,
          secondary: draft.secondary, tertiary: draft.tertiary,
          idempotency_key: draft.idempotencyKey, author_mode: mode, files: attachments,
        }) },
      );
      if (created.status === 'failed') {
        setError(created.error || 'GitHub could not create this issue. Check GitHub before trying again.');
        setDraft((current) => ({ ...current, idempotencyKey: newKey() }));
      } else if (created.number) {
        localStorage.removeItem(draftKey);
        localStorage.removeItem(pendingKey);
        fileDraft = null;
        navigate(`/support/issues/${created.number}`);
      } else {
        setPendingReport(created.report_id);
        setPendingMessage(created.error || null);
      }
    } catch (failure) {
      if (failure instanceof SupportApiError && failure.status === 401) expireConnection();
      if (failure instanceof SupportApiError && failure.status === 409
        && failure.message.includes('GitHub posting') && mode === 'github') {
        setConfirmBot(true);
      }
      if (failure instanceof SupportApiError && failure.status === 409
        && (failure.message === 'Issue request has already been used'
          || failure.message.startsWith('Issue details changed after submission'))) {
        setDraft((current) => ({ ...current, idempotencyKey: newKey() }));
      }
      setError(failure instanceof SupportApiError && failure.status === 429 && failure.retryAfterSeconds
        ? `Please wait ${waitDescription(failure.retryAfterSeconds)} before creating another issue.`
        : failure instanceof Error ? failure.message : 'Could not create issue. Try again.');
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => {
    if (!pendingReport) return;
    let failures = 0;
    const timer = window.setInterval(() => {
      supportRequest<{ number: number | null; status: 'published' | 'pending' | 'failed'; error?: string }>(`/submissions/${pendingReport}`).then((result) => {
        failures = 0;
        if (result.number) {
          localStorage.removeItem(draftKey);
          localStorage.removeItem(pendingKey);
          fileDraft = null;
          navigate(`/support/issues/${result.number}`);
        } else if (result.status === 'failed') {
          localStorage.removeItem(pendingKey);
          setPendingReport(null);
          setPendingMessage(null);
          setError(result.error || 'GitHub could not create this issue. Check GitHub before trying again.');
          setDraft((current) => ({ ...current, idempotencyKey: newKey() }));
        } else setPendingMessage(result.error || null);
      }).catch((failure) => {
        failures += 1;
        if (failure instanceof SupportApiError && failure.status === 401) {
          expireConnection();
          setError('Your account connection expired. Reconnect to check this saved issue under Your issues.');
          window.clearInterval(timer);
        } else if (failures >= 3) {
          setError('Sportarr cannot check this saved issue right now. It will keep trying to post. Check Your issues later.');
          window.clearInterval(timer);
        }
      });
    }, 10000);
    return () => window.clearInterval(timer);
  }, [pendingReport, navigate, expireConnection]);

  function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (!identity) return;
    if (missingFiles.length) {
      setError('Reattach the missing files or choose to continue without them before submitting.');
      return;
    }
    if (draft.title.trim().length < 5 || draft.primary.trim().length < 20
      || (draft.category === 'feature' && draft.secondary.trim().length < 20)
      || (draft.category === 'bug' && draft.tertiary.trim().length < 3)) {
      setError('Please add enough detail to the required fields before creating this issue.');
      return;
    }
    if (identity.github_posting) {
      void createIssue('github');
    } else if (identity.connected_providers.includes('github')) {
      setConfirmBot(true);
    } else {
      void createIssue('bot');
    }
  }

  return (
    <PageShell>
      <div className="mx-auto max-w-4xl">
        <Link to="/support" className="mb-4 inline-flex min-h-11 items-center text-sm text-gray-400 hover:text-white">Back to issues</Link>
        <PageHeader title="New issue" subtitle="Describe one topic so others can follow and help." icon={GitHubMark} />
        <form onSubmit={handleSubmit} className="space-y-5">
          <section className="rounded-lg border border-red-900/30 bg-gradient-to-br from-gray-900 to-black p-4 sm:p-5">
            <h2 className="font-bold text-white">What kind of issue is this?</h2>
            <div className="mt-3 grid grid-cols-3 gap-2 sm:gap-3">
              {(Object.keys(forms) as Category[]).map((category) => (
                <button
                  key={category} type="button" aria-pressed={draft.category === category}
                  onClick={() => update('category', category)}
                  className={`${draft.category === category ? OPTION_CARD_SELECTED : OPTION_CARD_UNSELECTED} !p-2 text-center sm:!p-4 sm:text-left`}
                >
                  <span className="block text-xs font-bold text-white sm:text-base">{forms[category].name}</span>
                  <span className="mt-1 hidden text-sm text-gray-400 sm:block">{forms[category].description}</span>
                </button>
              ))}
            </div>
          </section>

          <section className="space-y-4 rounded-lg border border-red-900/30 bg-gradient-to-br from-gray-900 to-black p-4 sm:p-5">
            <h2 className="font-bold text-white">{form.title}</h2>
            <div>
              <label htmlFor="issue-title" className="mb-1 block text-sm text-gray-300">Title</label>
              <input id="issue-title" required minLength={5} maxLength={100} value={draft.title} onChange={(event) => update('title', event.target.value)} placeholder="Give this issue a clear title" className={inputClass} />
            </div>
            <div>
              <label htmlFor="issue-primary" className="mb-1 block text-sm text-gray-300">{form.primary}</label>
              <textarea id="issue-primary" required minLength={20} maxLength={3000} rows={5} value={draft.primary} onChange={(event) => update('primary', event.target.value)} placeholder={form.primaryHint} className={inputClass} />
            </div>
            <div>
              <label htmlFor="issue-secondary" className="mb-1 block text-sm text-gray-300">{form.secondary}</label>
              <textarea id="issue-secondary" required={form.secondaryRequired} minLength={form.secondaryRequired ? 20 : undefined} maxLength={1500} rows={3} value={draft.secondary} onChange={(event) => update('secondary', event.target.value)} placeholder={form.secondaryHint} className={inputClass} />
            </div>
            <div>
              <label htmlFor="issue-tertiary" className="mb-1 block text-sm text-gray-300">{form.tertiary}</label>
              <textarea id="issue-tertiary" required={form.tertiaryRequired} minLength={form.tertiaryRequired ? 3 : undefined} maxLength={draft.category === 'feature' ? 1500 : 300} rows={2} value={draft.tertiary} onChange={(event) => update('tertiary', event.target.value)} placeholder={form.tertiaryHint} className={inputClass} />
            </div>
          </section>

          <section className="rounded-lg border border-red-900/30 bg-gradient-to-br from-gray-900 to-black p-4 sm:p-5">
            <h2 className="font-bold text-white">Files</h2>
            {handoffMessage && <p role="status" className="mt-2 text-sm text-gray-300">{handoffMessage}</p>}
            <p className="mt-1 text-sm text-gray-400">Files and small redacted text excerpts are public on GitHub. Stored files expire after 90 days, but excerpts remain in the issue. Archives are not accepted.</p>
            {missingFiles.length > 0 && <div role="alert" className="mt-3 rounded-lg border border-amber-700/50 bg-amber-950/20 p-3 text-sm text-amber-100">
              <p>Files chosen earlier are not available after this page reloaded. Reattach {missingFiles.join(', ')} before submitting, or continue without them.</p>
              <button type="button" className="mt-2 text-amber-100 underline" onClick={() => setDraft((current) => ({
                ...current, fileNames: files.map((file) => file.name),
              }))}>Continue without missing files</button>
            </div>}
            <label className={`${BUTTON_SECONDARY} mt-3 cursor-pointer focus-within:ring-2 focus-within:ring-red-500 focus-within:ring-offset-2 focus-within:ring-offset-black`}>
              <DocumentArrowUpIcon className="h-5 w-5" />Choose files
              <input type="file" multiple className="sr-only" accept={allowedExtensions.join(',')} onChange={(event) => { addFiles(event.target.files); event.target.value = ''; }} />
            </label>
            {files.length > 0 && <ul className="mt-3 space-y-2">
              {files.map((file, index) => <li key={`${file.name}-${index}`} className="flex min-w-0 items-center justify-between gap-3 rounded-lg border border-gray-800 bg-black/30 px-3 py-2 text-sm text-gray-300">
                <span className="min-w-0 break-all">{file.name}
                  {['.txt', '.log', '.json', '.yml', '.yaml', '.csv', '.md'].some((extension) => file.name.toLowerCase().endsWith(extension))
                    && <span className="mt-1 block"><FullTextLink file={file} /></span>}
                </span>
                <button type="button" aria-label={`Remove ${file.name}`} className="shrink-0 rounded-lg p-2 text-gray-400 hover:text-white" onClick={() => {
                  const next = files.filter((_, position) => position !== index);
                  setFiles(next);
                  setDraft((current) => ({ ...current, fileNames: current.fileNames.filter((name) =>
                    name !== file.name || next.some((remaining) => remaining.name === name)),
                  }));
                }}><XMarkIcon className="h-5 w-5" /></button>
              </li>)}
            </ul>}
            {diagnosticPreview && files.some((file) => file.name.startsWith('sportarr-support-')) && <details className="mt-3 rounded-lg border border-gray-700 bg-black/30 p-3 text-sm text-gray-300">
              <summary className="cursor-pointer font-medium text-white">Preview redacted diagnostic log</summary>
              <p className="my-2 text-xs text-gray-400">Showing the first 16 KB. Review your log before posting. File contents will be public.</p>
              <pre className="max-h-64 overflow-auto whitespace-pre-wrap break-all text-xs">{diagnosticPreview}</pre>
            </details>}
          </section>

          <SupportConnection key={connectionVersion} onIdentityChange={handleIdentity} />
          <p className="text-sm text-gray-400">Your connected account name and issue details will be public on GitHub.</p>
          {pendingReport && <p role="status" className="rounded-lg border border-amber-800/50 bg-amber-950/20 p-4 text-sm text-amber-100">Your issue is saved. GitHub is taking longer than expected, so Sportarr will keep trying. You can find it under Your issues.{pendingMessage && <span className="mt-2 block">{pendingMessage}</span>}</p>}
          {error && <p role="alert" className="rounded-lg border border-red-800 bg-red-950/30 p-4 text-sm text-red-300">{error}</p>}
          <div className="flex flex-wrap justify-end gap-2">
            <button type="submit" disabled={!identity || busy || !!pendingReport} className={BUTTON_PRIMARY}><PlusIcon className="h-5 w-5" />{busy ? 'Submitting issue...' : pendingReport ? 'Issue saved' : 'Submit issue'}</button>
          </div>
        </form>

        {confirmBot && createPortal(<div ref={dialogRef} role="dialog" aria-modal="true" aria-label="Choose issue author" className="fixed inset-0 z-[60] flex items-end bg-black/70 p-0 sm:items-center sm:justify-center sm:p-4">
          <div className="w-full rounded-t-2xl border border-red-900/40 bg-gray-900 p-5 pb-[calc(env(safe-area-inset-bottom)+1.25rem)] shadow-2xl sm:max-w-md sm:rounded-2xl sm:pb-5">
            <h2 className="text-lg font-bold text-white">Choose how to post</h2>
            <p className="mt-2 text-sm text-gray-300">GitHub posting permission was not completed. Finish it in the account card to submit under your GitHub name, or submit through Sportarr now. Your draft and files will stay while this page is open.</p>
            <div className="mt-5 flex flex-col gap-2">
              <button type="button" className={BUTTON_PRIMARY} onClick={() => void createIssue('bot')}>Submit through Sportarr</button>
              <button type="button" className={BUTTON_SECONDARY} onClick={() => setConfirmBot(false)}>Keep editing</button>
            </div>
          </div>
        </div>, document.body)}
      </div>
    </PageShell>
  );
}
