import { describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import SupportPage from '../SupportPage';
import SupportYourIssuesPage from '../SupportYourIssuesPage';

vi.mock('../../api/support', () => ({
  browseSupportIssues: vi.fn(async () => ({ issues: [], total_count: 0, page: 1 })),
}));

vi.mock('../../components/SupportConnection', () => ({
  default: () => <div>Connect account</div>,
}));

function show(page: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={client}><MemoryRouter>{page}</MemoryRouter></QueryClientProvider>);
}

describe('Support navigation', () => {
  it('places the new issue action in the section row on Browse issues', async () => {
    show(<SupportPage />);

    const navigation = await screen.findByRole('navigation', { name: 'Support sections' });
    expect(within(navigation).getByRole('link', { name: /Browse issues/ })).toHaveAttribute('aria-current', 'page');
    expect(within(navigation).getByRole('link', { name: /Your issues/ })).toHaveAttribute('href', '/support/yours');
    expect(within(navigation).getByRole('link', { name: 'New issue' })).toHaveAttribute('href', '/support/new');
    expect(screen.getAllByRole('link', { name: 'New issue' })).toHaveLength(1);
  });

  it('keeps the new issue action in the row on Your issues before account connection', async () => {
    show(<SupportYourIssuesPage />);

    const navigation = await screen.findByRole('navigation', { name: 'Support sections' });
    expect(within(navigation).getByRole('link', { name: /Your issues/ })).toHaveAttribute('aria-current', 'page');
    expect(within(navigation).getByRole('link', { name: /Browse issues/ })).toHaveAttribute('href', '/support');
    expect(within(navigation).getByRole('link', { name: 'New issue' })).toHaveAttribute('href', '/support/new');
    expect(screen.getAllByRole('link', { name: 'New issue' })).toHaveLength(1);
  });
});
