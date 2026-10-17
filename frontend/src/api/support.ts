export interface SupportIssue {
  number: number;
  title: string;
  body: string;
  url: string;
  state: 'open' | 'closed';
  state_reason: 'completed' | 'not_planned' | 'reopened' | null;
  author: string;
  labels: string[];
  comment_count: number;
  reactions: { total: number; thumbs_up: number; thumbs_down: number };
  created_at: string;
  updated_at: string;
}

export interface SupportComment {
  id: number;
  body: string;
  author: string;
  url: string;
  created_at: string;
}

export interface SupportIssueList {
  issues: SupportIssue[];
  total_count: number;
  page: number;
}

export interface SupportIssueDetail {
  issue: SupportIssue;
  comments: SupportComment[];
  comment_page: number;
}

const supportApiUrl = 'https://sportarr.net/api/support/issues';

async function readJson<T>(url: string): Promise<T> {
  const response = await fetch(url, { headers: { Accept: 'application/json' } });
  if (!response.ok) {
    throw new Error(response.status === 404 ? 'Issue not found.' : 'Issues are unavailable right now. Try again shortly.');
  }
  return response.json() as Promise<T>;
}

export async function browseSupportIssues(query: string, state: 'open' | 'closed' | 'all', page: number) {
  const params = new URLSearchParams({ q: query, state, page: String(page) });
  return readJson<SupportIssueList>(`${supportApiUrl}?${params}`);
}

export async function readSupportIssue(number: number, commentPage = 1) {
  const params = new URLSearchParams({ comment_page: String(commentPage) });
  return readJson<SupportIssueDetail>(`${supportApiUrl}/${number}?${params}`);
}
