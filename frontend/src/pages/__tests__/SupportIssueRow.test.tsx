import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { SupportIssueRow } from '../SupportPage';

describe('Support issue row', () => {
  it('shows the GitHub reaction count without suggesting the row count is a reaction button', () => {
    render(<MemoryRouter><SupportIssueRow issue={{
      number: 412, title: 'Synthetic import issue', body: '', url: 'https://github.com/Sportarr/Sportarr/issues/412',
      state: 'open', state_reason: null, author: 'demo_user', labels: ['bug'], comment_count: 0,
      reactions: { total: 3, thumbs_up: 3, thumbs_down: 0 },
      created_at: '2026-10-02T12:00:00Z', updated_at: '2026-10-03T12:00:00Z',
    }} /></MemoryRouter>);

    expect(screen.getByRole('link', { name: /Synthetic import issue/ })).toHaveAttribute('href', '/support/issues/412');
    expect(screen.getByRole('link', { name: /0 comments\. 3 GitHub thumbs-up reactions/ })).toBeInTheDocument();
    expect(screen.queryByText(/Same here 3/)).not.toBeInTheDocument();
  });
});
