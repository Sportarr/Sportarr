import type { ReactNode } from 'react';

const marker = /<!-- sportarr-bridge:[^>]* -->/g;
const attachment = /^- (!?)\[((?:\\.|[^\]])+)\]\((https:\/\/[^\s)]+)\)$/;
const allowedHosts = new Set(['github.com', 'hub.sportarr.net', 'sportarr.net', 'discord.com']);
const entities: Record<string, string> = { '&amp;': '&', '&lt;': '<', '&gt;': '>', '&quot;': '"', '&#x27;': "'" };

function readableText(value: string) {
  return value.replace(/&(?:amp|lt|gt|quot|#x27);/g, (entity) => entities[entity]);
}

function readableLabel(value: string) {
  return value.replace(/\\([\\\[\]])/g, '$1');
}

function linkedText(value: string): ReactNode[] {
  const parts: ReactNode[] = [];
  const urls = /https:\/\/[^\s<>"']+/g;
  let start = 0;
  for (const match of value.matchAll(urls)) {
    const at = match.index;
    if (at > start) parts.push(value.slice(start, at));
    const candidate = match[0].replace(/[.,;!?]+$/, '');
    const url = safeLink(candidate);
    if (url) parts.push(<a key={at} href={url} target="_blank" rel="noopener noreferrer" className="break-all text-red-300 underline hover:text-red-200">{candidate}</a>);
    else parts.push(candidate);
    const end = at + match[0].length;
    if (candidate.length < match[0].length) parts.push(match[0].slice(candidate.length));
    start = end;
  }
  if (start < value.length) parts.push(value.slice(start));
  return parts;
}

export function safeLink(url: string) {
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'https:' && allowedHosts.has(parsed.hostname) ? parsed.href : null;
  } catch { return null; }
}

export default function SupportIssueContent({ body, decodeEntities = true }: { body: string; decodeEntities?: boolean }) {
  const display = (value: string) => decodeEntities ? readableText(value) : value;
  const lines = body.replace(/\r\n?/g, '\n').replace(marker, '').trim().split('\n');
  const elements: ReactNode[] = [];
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    if (!line.trim() || line === 'source:sportarr-app') continue;
    if (line.startsWith('### ')) {
      elements.push(<h3 key={index} className="mt-5 border-b border-red-900/50 pb-2 text-sm font-bold uppercase tracking-wide text-red-300 first:mt-0">{display(line.slice(4))}</h3>);
      continue;
    }
    const linked = line.match(attachment);
    const url = linked && safeLink(linked[3]);
    if (linked && url) {
      elements.push(<div key={index} className="my-2 min-w-0">
        {linked[1] ? <a href={url} target="_blank" rel="noopener noreferrer" className="block w-fit max-w-full overflow-hidden rounded-lg border border-gray-700"><img src={url} alt={display(readableLabel(linked[2]))} className="max-h-72 max-w-full object-contain" loading="lazy" /></a>
          : <a href={url} target="_blank" rel="noopener noreferrer" className="break-all text-red-300 underline hover:text-red-200">{display(readableLabel(linked[2]))}</a>}
      </div>);
      continue;
    }
    if (line === '<details>') {
      const summary = lines[index + 1]?.match(/^<summary>(.*)<\/summary>$/);
      const end = lines.indexOf('</details>', index + 1);
      if (summary && end > index) {
        const contents = lines.slice(index + 2, end).join('\n').replace(/^\n*```[a-z]*\n?/, '').replace(/\n?```\n*$/, '');
        elements.push(<details key={index} className="my-3 rounded-lg border border-gray-700 p-3"><summary className="cursor-pointer break-all text-sm text-red-300">{display(summary[1])}</summary><pre className="mt-3 max-h-96 overflow-auto whitespace-pre-wrap break-words text-xs text-gray-300">{contents}</pre></details>);
        index = end;
        continue;
      }
    }
    const paragraph = /^- .*: Quarantined because sensitive content was detected\.$/.test(line)
      ? readableLabel(line) : line;
    elements.push(<p key={index} className="whitespace-pre-wrap break-words text-sm leading-relaxed text-gray-200">{linkedText(display(paragraph))}</p>);
  }
  return <div className="space-y-2">{elements.length ? elements : <p className="text-sm text-gray-400">No description was provided.</p>}</div>;
}
