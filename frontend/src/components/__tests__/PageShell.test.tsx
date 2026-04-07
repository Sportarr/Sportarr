import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import PageShell, { PageErrorState, PageLoadingState } from '../PageShell';

describe('PageShell', () => {
  it('applies default page padding classes', () => {
    const { container } = render(
      <PageShell>
        <div>Content</div>
      </PageShell>
    );

    expect(container.firstElementChild).toHaveClass('p-4', 'md:p-8');
  });

  it('merges custom className with default classes', () => {
    const { container } = render(
      <PageShell className="pb-8">
        <div>Content</div>
      </PageShell>
    );

    expect(container.firstElementChild).toHaveClass('p-4', 'md:p-8', 'pb-8');
  });
});

describe('PageShell states', () => {
  it('renders loading state label', () => {
    render(<PageLoadingState label="Loading calendar..." />);
    expect(screen.getByText('Loading calendar...')).toBeInTheDocument();
  });

  it('renders error state title and message', () => {
    render(<PageErrorState title="Oops" message="Failed to load" />);
    expect(screen.getByText('Oops')).toBeInTheDocument();
    expect(screen.getByText('Failed to load')).toBeInTheDocument();
  });
});
