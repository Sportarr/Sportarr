import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import PageHeader from '../PageHeader';

describe('PageHeader', () => {
  it('renders title and subtitle', () => {
    render(<PageHeader title="Events" subtitle="Manage tracked events" />);

    expect(screen.getByRole('heading', { name: 'Events' })).toBeInTheDocument();
    expect(screen.getByText('Manage tracked events')).toBeInTheDocument();
  });

  it('renders actions when provided', () => {
    render(
      <PageHeader
        title="Events"
        actions={<button type="button">Refresh</button>}
      />
    );

    expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument();
  });

  it('applies custom class names', () => {
    render(
      <PageHeader
        title="Events"
        subtitle="Subtitle"
        className="mb-10"
        titleClassName="text-xl"
        subtitleClassName="text-red-400"
        actionsClassName="justify-start"
        actions={<button type="button">Act</button>}
      />
    );

    expect(screen.getByRole('heading', { name: 'Events' })).toHaveClass('text-xl');
    expect(screen.getByText('Subtitle')).toHaveClass('text-red-400');
    expect(screen.getByRole('button', { name: 'Act' }).parentElement).toHaveClass('justify-start');
  });
});
