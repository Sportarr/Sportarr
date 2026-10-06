import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  ChatBubbleLeftRightIcon,
  HandThumbUpIcon,
  MagnifyingGlassIcon,
} from '@heroicons/react/24/outline';
import { browseSupportIssues, type SupportIssue } from '../api/support';
import GitHubMark from '../components/GitHubMark';
import SupportNavigation from '../components/SupportNavigation';
import PageHeader from '../components/PageHeader';
import PageShell, { PageErrorState } from '../components/PageShell';
import { BADGE_AMBER, BADGE_BLUE, BADGE_GRAY, BADGE_GREEN, BADGE_RED, BUTTON_SECONDARY } from '../utils/designTokens';

const inputClass = 'w-full rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-white focus:border-red-600 focus:outline-none focus:ring-1 focus:ring-red-500';

function category(issue: SupportIssue) {
  if (issue.labels.includes('bug')) return { label: 'Bug', className: BADGE_RED };
  if (issue.labels.includes('enhancement')) return { label: 'Feature request', className: BADGE_BLUE };
  if (issue.labels.includes('question')) return { label: 'Question', className: BADGE_AMBER };
  return null;
}

export function SupportIssueRow({ issue }: { issue: SupportIssue }) {
  const kind = category(issue);
  return (
    <Link
      to={`/support/issues/${issue.number}`}
      className="block min-w-0 px-4 py-3 transition-colors hover:bg-gray-800/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-red-500 sm:px-5"
    >
      <div className="flex min-w-0 items-start gap-3">
        <span className={`mt-1 h-2.5 w-2.5 shrink-0 rounded-full ${issue.state === 'open' ? 'bg-green-500' : 'bg-gray-500'}`} aria-hidden="true" />
        <div className="min-w-0 flex-1">
          <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
            <span className="break-words font-medium text-white">{issue.title}</span>
            {kind && <span className={kind.className}>{kind.label}</span>}
            {issue.state === 'closed' && <span className={BADGE_GRAY}>Closed</span>}
          </div>
          <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-gray-400">
            <span>#{issue.number} by {issue.author}</span>
            <span>{new Date(issue.updated_at).toLocaleDateString()}</span>
            <span className="inline-flex items-center gap-1"><ChatBubbleLeftRightIcon className="h-4 w-4" aria-hidden="true" />{issue.comment_count}<span className="sr-only"> {issue.comment_count === 1 ? 'comment' : 'comments'}. </span></span>
            <span className="inline-flex items-center gap-1"><HandThumbUpIcon className="h-4 w-4" aria-hidden="true" />{issue.reactions.thumbs_up}<span className="sr-only"> GitHub thumbs-up {issue.reactions.thumbs_up === 1 ? 'reaction' : 'reactions'}</span></span>
          </div>
        </div>
      </div>
    </Link>
  );
}

export default function SupportPage() {
  const [draftQuery, setDraftQuery] = useState('');
  const [query, setQuery] = useState('');
  const [state, setState] = useState<'open' | 'closed' | 'all'>('open');
  const [page, setPage] = useState(1);
  const issues = useQuery({
    queryKey: ['support-issues', query, state, page],
    queryFn: () => browseSupportIssues(query, state, page),
    staleTime: 30_000,
  });

  return (
    <PageShell>
      <div className="mx-auto max-w-6xl">
        <PageHeader
          title="Support"
          subtitle="Find reported issues and follow the conversation."
          icon={GitHubMark}
        />

        <SupportNavigation current="browse" />

        <div className="rounded-lg border border-red-900/30 bg-gradient-to-br from-gray-900 to-black p-4 sm:p-5">
          <div className="flex flex-col gap-3 sm:flex-row">
            <form
              className="relative min-w-0 flex-1"
              onSubmit={(event) => { event.preventDefault(); setQuery(draftQuery.trim()); setPage(1); }}
            >
              <label htmlFor="support-search" className="sr-only">Search issues</label>
              <input
                id="support-search"
                value={draftQuery}
                onChange={(event) => setDraftQuery(event.target.value)}
                maxLength={120}
                placeholder="Search issues"
                className={`${inputClass} pr-12`}
              />
              <button type="submit" aria-label="Search issues" className="absolute inset-y-0 right-0 flex w-11 items-center justify-center text-gray-300 hover:text-white">
                <MagnifyingGlassIcon className="h-5 w-5" />
              </button>
            </form>
            <label htmlFor="support-state" className="sr-only">Issue state</label>
            <select
              id="support-state"
              value={state}
              onChange={(event) => { setState(event.target.value as 'open' | 'closed' | 'all'); setPage(1); }}
              className={`${inputClass} sm:w-40`}
            >
              <option value="open">Open</option>
              <option value="closed">Closed</option>
              <option value="all">All issues</option>
            </select>
          </div>

          <div className="mt-4 divide-y divide-gray-800 rounded-lg border border-gray-700 bg-black/30">
            {issues.isLoading && <p className="p-5 text-sm text-gray-400">Loading issues...</p>}
            {issues.isError && <p className="p-5 text-sm text-red-300">Issues are unavailable right now. Try again shortly.</p>}
            {issues.data?.issues.length === 0 && <p className="p-5 text-sm text-gray-400">No issues match this search.</p>}
            {issues.data?.issues.map((issue) => <SupportIssueRow key={issue.number} issue={issue} />)}
          </div>

          {issues.data && (
            <div className="mt-4 flex flex-wrap items-center justify-between gap-3 text-sm text-gray-400">
              <span>{issues.data.total_count} {issues.data.total_count === 1 ? 'issue' : 'issues'}</span>
              <div className="flex gap-2">
                <button className={BUTTON_SECONDARY} disabled={page === 1} onClick={() => setPage(page - 1)}>Previous</button>
                <button className={BUTTON_SECONDARY} disabled={page * 20 >= issues.data.total_count} onClick={() => setPage(page + 1)}>Next</button>
              </div>
            </div>
          )}
        </div>
      </div>
    </PageShell>
  );
}
