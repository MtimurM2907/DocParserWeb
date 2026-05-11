/** Все вхождения подстроки (без учёта регистра), перекрывающиеся допускаются. */
export function findTextMatches(text: string, query: string): { start: number; end: number }[] {
  const q = query.trim();
  if (!q || !text) return [];
  const lower = text.toLowerCase();
  const lq = q.toLowerCase();
  const out: { start: number; end: number }[] = [];
  let pos = 0;
  while (pos <= lower.length - lq.length) {
    const i = lower.indexOf(lq, pos);
    if (i < 0) break;
    out.push({ start: i, end: i + lq.length });
    pos = i + 1;
  }
  return out;
}
