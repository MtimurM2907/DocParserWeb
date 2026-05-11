import type { ReactNode } from 'react';
import type { SpellcheckMistake } from '../types/api';
import { normalizeNewlines } from '../lib/text';

export type DocLineType = 'capsTitle' | 'sectionTitle' | 'subSection' | 'paragraph';

export interface DocLine {
  text: string;
  type: DocLineType;
}

export type TextMatchRange = { start: number; end: number };

/** Тот же текст, что используется для «вида как в оригинале» (индексы поиска должны совпадать). */
export function prepareDocLikeSource(normalized: string): string {
  let text = normalizeNewlines(normalized ?? '').replace(/\u00A0/g, ' ');
  if (!text.trim()) return '';

  // Для "слипшегося" OCR-текста стараемся восстановить структуру разделов.
  const lineBreaks = (text.match(/\n/g) ?? []).length;
  if (lineBreaks <= 3) {
    text = text.replace(/^([^\n]{12,}?)(\d+\.\s+)/, '$1\n\n$2');
    text = text.replace(/([А-ЯЁA-Z])(\d+\.\s+[А-ЯЁA-Z])/g, '$1\n\n$2');
    text = text.replace(/([а-яёa-z.])(\d+\.)/g, '$1\n$2');
    text = text.replace(/(?<!\n)(\d+\.\d+\.\s+)/g, '\n$1');
    text = text.replace(/(?<!\n)(?<!\d\.)(\d+\.\s+[А-ЯЁA-Z])/g, '\n$1');
    text = text.replace(/(?<!\n)(\d+\.\d+\.\s+[^.\n]+?\.)\s+(?=\d+\.\d+\.\s+)/g, '$1\n');
  }

  text = text.replace(/\n{3,}/g, '\n\n');
  return text.trim();
}

function classifyLine(line: string): DocLineType {
  if (/^[А-ЯЁ\s«»"()\-]{8,}$/.test(line)) return 'capsTitle';
  if (/^\d+\.\s+[А-ЯЁA-Z]/.test(line)) return 'sectionTitle';
  if (/^\d+\.\d+\.\s*/.test(line)) return 'subSection';
  return 'paragraph';
}

export function buildDocLikeLines(sourceText: string): DocLine[] {
  const t = normalizeNewlines(sourceText ?? '');
  if (!t.trim()) return [];

  const prepared = prepareDocLikeSource(t);

  const lines = prepared
    .split(/\n+/)
    .map((l) => l.trim())
    .filter(Boolean);

  return lines.map((line) => ({
    text: line,
    type: classifyLine(line),
  }));
}

export function buildDocLikeLinesWithOffsets(sourceText: string): {
  prepared: string;
  lines: Array<DocLine & { start: number; end: number }>;
} {
  const t = normalizeNewlines(sourceText ?? '');
  if (!t.trim()) return { prepared: '', lines: [] };

  const prepared = prepareDocLikeSource(t);
  const lines: Array<DocLine & { start: number; end: number }> = [];
  let pos = 0;

  while (pos < prepared.length) {
    while (pos < prepared.length && prepared[pos] === '\n') pos++;
    if (pos >= prepared.length) break;
    const lineStart = pos;
    while (pos < prepared.length && prepared[pos] !== '\n') pos++;
    const rawLine = prepared.slice(lineStart, pos);
    const trimmed = rawLine.trim();
    if (!trimmed) continue;
    const d = rawLine.indexOf(trimmed);
    const start = lineStart + (d >= 0 ? d : 0);
    const end = start + trimmed.length;
    lines.push({
      text: trimmed,
      type: classifyLine(trimmed),
      start,
      end,
    });
  }

  return { prepared, lines };
}

function renderSegmentsWithSearch(
  fragment: string,
  globalStart: number,
  matches: TextMatchRange[],
  activeMatchIndex: number,
  keyPrefix: string,
): ReactNode[] {
  if (!fragment) return [];
  const local = matches
    .map((m, idx) => ({ ...m, idx }))
    .filter((m) => m.end > globalStart && m.start < globalStart + fragment.length)
    .map((m) => ({
      start: Math.max(0, m.start - globalStart),
      end: Math.min(fragment.length, m.end - globalStart),
      idx: m.idx,
    }))
    .filter((m) => m.end > m.start)
    .sort((a, b) => a.start - b.start);

  const out: ReactNode[] = [];
  let c = 0;
  for (const m of local) {
    if (m.start > c) {
      out.push(<span key={`${keyPrefix}_p_${c}`}>{fragment.slice(c, m.start)}</span>);
    }
    const isActive = m.idx === activeMatchIndex;
    out.push(
      <mark
        key={`${keyPrefix}_s_${m.start}_${m.idx}`}
        className={`search-hit${isActive ? ' search-hit-active' : ''}`}
      >
        {fragment.slice(m.start, m.end)}
      </mark>,
    );
    c = m.end;
  }
  if (c < fragment.length) {
    out.push(<span key={`${keyPrefix}_p_end`}>{fragment.slice(c)}</span>);
  }
  return out;
}

export function renderHighlightedText(
  text: string,
  mistakes: SpellcheckMistake[],
  searchMatches?: TextMatchRange[],
  activeSearchIndex?: number,
) {
  const searchOn = Boolean(searchMatches && searchMatches.length > 0);
  const activeIdx = activeSearchIndex ?? 0;

  if (!mistakes || mistakes.length === 0) {
    const empty = !(text ?? '').trim();
    if (searchOn) {
      return (
        <pre className="text-output spell-preview" aria-label="Текст с подсветкой поиска">
          {renderSegmentsWithSearch(text ?? '', 0, searchMatches!, activeIdx, 'h0')}
        </pre>
      );
    }
    return <pre className="text-output">{empty ? '(текст не распознан)' : text}</pre>;
  }

  const safeText = text ?? '';
  const normalized = mistakes
    .filter((m) => Number.isFinite(m.start) && Number.isFinite(m.length))
    .map((m, idx) => ({
      ...m,
      start: Math.max(0, Math.min(m.start, safeText.length)),
      end: Math.max(0, Math.min(m.start + m.length, safeText.length)),
      _idx: idx,
    }))
    .filter((m) => m.end > m.start)
    .sort((a, b) => (a.start !== b.start ? a.start - b.start : a.end - b.end));

  const parts: ReactNode[] = [];
  let cursor = 0;
  const sm = searchOn ? searchMatches! : [];

  for (const m of normalized) {
    if (m.start < cursor) continue;
    if (m.start > cursor) {
      const slice = safeText.slice(cursor, m.start);
      if (searchOn) {
        parts.push(...renderSegmentsWithSearch(slice, cursor, sm, activeIdx, `p_${cursor}`));
      } else {
        parts.push(<span key={`t_${cursor}`}>{slice}</span>);
      }
    }

    const word = safeText.slice(m.start, m.end);
    const title =
      m.suggestions && m.suggestions.length > 0 ? `Подсказки: ${m.suggestions.slice(0, 5).join(', ')}` : 'Нет подсказок';

    parts.push(
      <mark key={`m_${m.start}_${m._idx}`} className="spell-mistake" title={title}>
        {searchOn
          ? renderSegmentsWithSearch(word, m.start, sm, activeIdx, `mi_${m.start}_${m._idx}`)
          : word}
      </mark>,
    );
    cursor = m.end;
  }

  if (cursor < safeText.length) {
    const slice = safeText.slice(cursor);
    if (searchOn) {
      parts.push(...renderSegmentsWithSearch(slice, cursor, sm, activeIdx, `tail_${cursor}`));
    } else {
      parts.push(<span key={`t_${cursor}`}>{slice}</span>);
    }
  }

  return (
    <pre className="text-output spell-preview" aria-label="Текст с подсветкой орфографических ошибок">
      {parts}
    </pre>
  );
}

export function renderDocLikeText(
  text: string,
  searchMatches?: TextMatchRange[],
  activeSearchIndex?: number,
) {
  const { lines } = buildDocLikeLinesWithOffsets(text);
  if (lines.length === 0) return <pre className="text-output">(текст не распознан)</pre>;

  const searchOn = Boolean(searchMatches && searchMatches.length > 0);
  const activeIdx = activeSearchIndex ?? 0;
  const sm = searchOn ? searchMatches! : [];

  return (
    <div className="doc-like-view text-output" aria-label="Текст в оригинальном стиле">
      {lines.map((line, idx) => (
        <p key={`${idx}_${line.start}_${line.text.slice(0, 16)}`} className={`doc-line doc-line-${line.type}`}>
          {searchOn
            ? renderSegmentsWithSearch(line.text, line.start, sm, activeIdx, `d_${line.start}`)
            : line.text}
        </p>
      ))}
    </div>
  );
}
