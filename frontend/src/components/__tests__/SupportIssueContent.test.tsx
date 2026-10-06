import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import SupportIssueContent, { safeLink } from '../SupportIssueContent';

describe('SupportIssueContent', () => {
  it('accepts only approved HTTPS destinations', () => {
    expect(safeLink('https://github.com/Sportarr/Sportarr/issues/123')).toBe('https://github.com/Sportarr/Sportarr/issues/123');
    expect(safeLink('javascript:alert(1)')).toBeNull();
    expect(safeLink('https://github.com.evil.example/issue')).toBeNull();
    expect(safeLink('http://github.com/Sportarr/Sportarr/issues/123')).toBeNull();
  });

  it('renders Windows-line-ending attachments and redacted logs', () => {
    render(<SupportIssueContent body={[
      '### Attachments',
      '- [sportarr-support-01.txt](https://hub.sportarr.net/api/support/attachments/example)',
      '<details>',
      '<summary>Redacted log</summary>',
      '```text',
      '[ERR] Synthetic import failure',
      '```',
      '</details>',
      'source:sportarr-app',
      '<!-- sportarr-bridge:report:example -->',
    ].join('\r\n')} />);

    expect(screen.getByRole('link', { name: 'sportarr-support-01.txt' })).toHaveAttribute(
      'href', 'https://hub.sportarr.net/api/support/attachments/example',
    );
    expect(screen.getByText('Redacted log')).toBeInTheDocument();
    expect(screen.getByText(/Synthetic import failure/)).toBeInTheDocument();
    expect(screen.queryByText('source:sportarr-app')).not.toBeInTheDocument();
  });

  it('shows escaped issue-form punctuation as ordinary text', () => {
    render(<SupportIssueContent body={'### What happened\n\nIt doesn&#x27;t import &amp; shows &lt;error&gt;.'} />);

    expect(screen.getByText("It doesn't import & shows <error>.")).toBeInTheDocument();
  });

  it('keeps literal entities in GitHub comments', () => {
    render(<SupportIssueContent body={'Keep &amp; and &lt; in the reply.'} decodeEntities={false} />);

    expect(screen.getByText('Keep &amp; and &lt; in the reply.')).toBeInTheDocument();
  });

  it('renders links and quarantined names with square brackets', () => {
    render(<SupportIssueContent body={[
      '### Attachments',
      '- [log\\[1\\].txt](https://hub.sportarr.net/api/support/attachments/example)',
      '- private\\[2\\].log: Quarantined because sensitive content was detected.',
    ].join('\n')} />);

    expect(screen.getByRole('link', { name: 'log[1].txt' })).toHaveAttribute(
      'href', 'https://hub.sportarr.net/api/support/attachments/example',
    );
    expect(screen.getByText('- private[2].log: Quarantined because sensitive content was detected.')).toBeInTheDocument();
  });

  it('links the Discord discussion without linking untrusted sites', () => {
    render(<SupportIssueContent body={[
      'Discord report: https://discord.com/channels/123/456',
      'See https://untrusted.example/issue for more context.',
    ].join('\n')} />);

    expect(screen.getByRole('link', { name: 'https://discord.com/channels/123/456' })).toHaveAttribute(
      'href', 'https://discord.com/channels/123/456',
    );
    expect(screen.queryByRole('link', { name: 'https://untrusted.example/issue' })).not.toBeInTheDocument();
    expect(screen.getByText(/untrusted\.example\/issue/)).toBeInTheDocument();
  });
});
