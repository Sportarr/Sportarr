import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import TeamsPage from '../TeamsPage';

const transport = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));
vi.mock('../../api/client', () => ({ default: transport }));

const clients: QueryClient[] = [];
beforeEach(() => {
  localStorage.clear();
  transport.get.mockReset();
});
afterEach(() => {
  for (const client of clients.splice(0)) client.clear();
});

const teams = Array.from({ length: 90 }, (_, index) => ({
  idTeam: String(1000 + index),
  strTeam: `Team ${String(index).padStart(3, '0')}`,
  strSport: 'Soccer',
  strTeamBadge: `https://badges.example/${index}.png`,
}));

async function renderTeamsPage() {
  transport.get.mockImplementation(async (path: string) => {
    if (path === '/teams/all') return { data: teams };
    if (path === '/followed-teams') return { data: [] };
    if (path === '/qualityprofile') return { data: [] };
    throw new Error('Unconfigured page request ' + path);
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  clients.push(client);
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter><TeamsPage /></MemoryRouter>
    </QueryClientProvider>
  );
  await screen.findByText('Team 000');
}

describe('teams page render limit', () => {
  it('renders the first page of teams and reveals more on demand', async () => {
    await renderTeamsPage();

    expect(screen.getAllByAltText(/^Team \d{3}$/)).toHaveLength(60);
    expect(screen.queryByText('Team 060')).not.toBeInTheDocument();

    fireEvent.click(screen.getByText('Show more (30 remaining)'));

    expect(screen.getByText('Team 089')).toBeInTheDocument();
    expect(screen.getAllByAltText(/^Team \d{3}$/)).toHaveLength(90);
    expect(screen.queryByText(/Show more/)).not.toBeInTheDocument();
  });

  it('searches all teams, not only the rendered page', async () => {
    await renderTeamsPage();

    fireEvent.change(screen.getByPlaceholderText(/Filter teams/), { target: { value: 'Team 089' } });

    expect(await screen.findByText('Team 089')).toBeInTheDocument();
    expect(screen.queryByText(/Show more/)).not.toBeInTheDocument();
  });

  it('lazy loads team badges', async () => {
    await renderTeamsPage();

    expect(screen.getByAltText('Team 000')).toHaveAttribute('loading', 'lazy');
  });
});
