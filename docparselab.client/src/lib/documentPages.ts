import { normalizeNewlines } from './text';

/** Разбивает текст документа на страницы (PDF — символ \f между страницами). */
export function splitDocumentIntoPages(text: string): string[] {
  const normalized = normalizeNewlines(text ?? '');
  if (!normalized.trim()) return [''];

  if (normalized.includes('\f')) {
    const parts = normalized.split('\f');
    if (parts.length === 1) return parts;
    return parts.map((p) => p.trimEnd());
  }

  return [normalized];
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
