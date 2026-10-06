import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import GitHubMark from '../components/GitHubMark';
import { clearSupportToken, SupportApiError, supportRequest, type SupportIdentity } from '../api/supportConnection';
import SupportConnection from '../components/SupportConnection';
import SupportNavigation from '../components/SupportNavigation';
import PageHeader from '../components/PageHeader';
import PageShell from '../components/PageShell';
import { BADGE_AMBER, BADGE_GRAY, BADGE_GREEN } from '../utils/designTokens';

interface OwnedIssue {
  report_id: string;
  number: number | null;
  title: string;
  category: 'bug' | 'feature' | 'question';
  state: string;
  error?: string | null;
  created_at: string;
}

export function ownedIssueStatus(number: number | null, state: string): 'Open' | 'Closed' | 'Published' | 'Pending' | 'Failed' {
  if (number === null) return state === 'failed' ? 'Failed' : 'Pending';
  if (state === 'closed') return 'Closed';
  if (state === 'open') return 'Open';
  return 'Published';
}

export default function SupportYourIssuesPage() {
  const [identity, setIdentity] = useState<SupportIdentity | null>(null);
  const [issues, setIssues] = useState<OwnedIssue[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const handleIdentity = useCallback((value: SupportIdentity | null) => {
    setIdentity(value);
    if (value) setError(null);
  }, []);

  useEffect(() => {
    if (!identity) return;
    let active = true;
    setLoading(true);
    setError(null);
    supportRequest<{ issues: OwnedIssue[] }>('/me/issues').then((data) => {
      if (active) setIssues(data.issues);
    }).catch((failure) => {
      if (!active) return;
      if (failure instanceof SupportApiError && [401, 403].includes(failure.status)) {
        clearSupportToken();
        setIdentity(null);
        setError('Your connection expired. Connect your account again to see your issues.');
      } else setError('Your issues could not be loaded right now.');
    })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [identity]);

  return <PageShell>
    <div className="mx-auto max-w-6xl">
      <PageHeader title="Your issues" subtitle="Issues created from Sportarr after you connected an account." icon={GitHubMark} />
      <SupportNavigation current="yours" />
      {!identity && <>
        {error && <p role="alert" className="mb-3 text-sm text-red-300">{error}</p>}
        <SupportConnection onIdentityChange={handleIdentity} />
      </>}
      {identity && <>
        <div className="divide-y divide-gray-800 overflow-hidden rounded-lg border border-red-900/30 bg-gradient-to-br from-gray-900 to-black">
          {loading && <p className="p-5 text-sm text-gray-400">Loading your issues...</p>}
          {error && <p role="alert" className="p-5 text-sm text-red-300">{error}</p>}
          {!loading && !error && issues.length === 0 && <p className="p-5 text-sm text-gray-400">You have not created an issue from this installation yet.</p>}
          {issues.map((issue) => {
            const status = ownedIssueStatus(issue.number, issue.state);
            return <div key={issue.report_id} className="flex min-w-0 flex-wrap items-center gap-3 px-4 py-3 sm:px-5">
            <div className="min-w-0 flex-1">
              {issue.number ? <Link to={`/support/issues/${issue.number}`} className="break-words font-medium text-white hover:text-red-300">{issue.title}</Link>
                : <span className="break-words font-medium text-white">{issue.title}</span>}
              <p className="mt-1 text-xs text-gray-400">{issue.number ? `#${issue.number}` : issue.state === 'failed' ? 'Could not post' : 'Waiting for GitHub'} · {new Date(issue.created_at).toLocaleDateString()}</p>
              {issue.state === 'failed' && issue.error && <p className="mt-1 text-xs text-red-300">{issue.error}</p>}
            </div>
            <span className={status === 'Open' ? BADGE_GREEN : status === 'Pending' ? BADGE_AMBER : BADGE_GRAY}>{status}</span>
          </div>;
          })}
        </div>
      </>}
    </div>
  </PageShell>;
}
