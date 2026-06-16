import { normalizeNewlines } from './text';

/** Примерно одна «экранная» страница в режиме чтения (без \f из PDF). */
export const VIRTUAL_PAGE_LINE_LIMIT = 28;

/** Запасной лимит для текста без переносов строк. */
const VIRTUAL_PAGE_CHAR_LIMIT = 3200;

function splitLongTextIntoVirtualPages(normalized: string): string[] {
  if (!normalized.includes('\n') && normalized.length > VIRTUAL_PAGE_CHAR_LIMIT) {
    const pages: string[] = [];
    for (let i = 0; i < normalized.length; i += VIRTUAL_PAGE_CHAR_LIMIT) {
      pages.push(normalized.slice(i, i + VIRTUAL_PAGE_CHAR_LIMIT));
    }
    return pages;
  }

  const lines = normalized.split('\n');
  if (lines.length <= VIRTUAL_PAGE_LINE_LIMIT) {
    return [normalized];
  }

  const pages: string[] = [];
  let start = 0;

  while (start < lines.length) {
    let end = Math.min(start + VIRTUAL_PAGE_LINE_LIMIT, lines.length);

    if (end < lines.length) {
      const searchFrom = start + Math.floor(VIRTUAL_PAGE_LINE_LIMIT * 0.6);
      let bestBreak = end;
      for (let i = end - 1; i >= searchFrom; i--) {
        if (lines[i]?.trim() === '') {
          bestBreak = i + 1;
          break;
        }
      }
      end = bestBreak;
    }

    pages.push(lines.slice(start, end).join('\n').replace(/\n+$/, ''));
    start = end;
    while (start < lines.length && lines[start]?.trim() === '') {
      start += 1;
    }
  }

  return pages.length > 0 ? pages : [normalized];
}

/** Разбивает текст документа на страницы (PDF — символ \f между страницами). */
export function splitDocumentIntoPages(text: string): string[] {
  const normalized = normalizeNewlines(text ?? '');
  if (!normalized.trim()) return [''];

  if (normalized.includes('\f')) {
    const parts = normalized.split('\f');
    if (parts.length === 1) return splitLongTextIntoVirtualPages(parts[0]!);
    return parts.flatMap((part) => splitLongTextIntoVirtualPages(part.trimEnd()));
  }

  return splitLongTextIntoVirtualPages(normalized);
}

/** Число страниц: из PDF, иначе из разметки текста (\f) в одном или нескольких фрагментах. */
export function getDocumentPageCount(
  text: string,
  pdfPageCount: number | null | undefined,
  ...moreTexts: string[]
): number {
  let fromText = splitDocumentIntoPages(text).length;
  for (const extra of moreTexts) {
    if (extra) fromText = Math.max(fromText, splitDocumentIntoPages(extra).length);
  }
  if (pdfPageCount != null && pdfPageCount > 0) {
    return Math.max(pdfPageCount, fromText);
  }
  return Math.max(1, fromText);
}
