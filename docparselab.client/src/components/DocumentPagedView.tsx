import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { findTextMatches } from '../lib/search';
import { splitDocumentIntoPages } from '../lib/documentPages';
import { prepareDocLikeSource, type TextMatchRange } from './DocumentTextViews';

type Props = {
  fullText: string;
  /** Рендер одной страницы (уже подготовленной для «вида документа»). */
  renderPage: (
    pageText: string,
    searchMatches: TextMatchRange[] | undefined,
    activeSearchIndex: number | undefined,
  ) => ReactNode;
  searchQuery?: string;
  activeSearchIndex?: number;
  onSearchIndexChange?: (index: number) => void;
};

export function DocumentPagedView({
  fullText,
  renderPage,
  searchQuery = '',
  activeSearchIndex = 0,
  onSearchIndexChange,
}: Props) {
  const rawPages = useMemo(() => splitDocumentIntoPages(fullText), [fullText]);
  const preparedPages = useMemo(
    () => rawPages.map((p) => prepareDocLikeSource(p)),
    [rawPages],
  );
  const [pageIndex, setPageIndex] = useState(0);
  const [pageInput, setPageInput] = useState('1');
  const safePage = Math.min(pageIndex, Math.max(0, preparedPages.length - 1));

  const q = searchQuery.trim();
  const globalMatches = useMemo(() => {
    if (!q) return [] as { pageIndex: number; localIndex: number; start: number; end: number }[];
    const out: { pageIndex: number; localIndex: number; start: number; end: number }[] = [];
    preparedPages.forEach((pageText, pi) => {
      const local = findTextMatches(pageText, q);
      local.forEach((m, li) => {
        out.push({ pageIndex: pi, localIndex: li, start: m.start, end: m.end });
      });
    });
    return out;
  }, [preparedPages, q]);

  useEffect(() => {
    setPageIndex(0);
    setPageInput('1');
  }, [fullText]);

  useEffect(() => {
    setPageInput(String(safePage + 1));
  }, [safePage]);

  useEffect(() => {
    if (!q || globalMatches.length === 0) return;
    const active = globalMatches[Math.min(activeSearchIndex, globalMatches.length - 1)];
    if (active && active.pageIndex !== pageIndex) {
      setPageIndex(active.pageIndex);
    }
  }, [activeSearchIndex, globalMatches, q, pageIndex]);

  const pageText = preparedPages[safePage] ?? '';

  const pageLocalMatches = useMemo(() => {
    if (!q) return undefined;
    return findTextMatches(pageText, q);
  }, [pageText, q]);

  const pageLocalActiveIndex = useMemo(() => {
    if (!q || globalMatches.length === 0) return undefined;
    const global = globalMatches[Math.min(activeSearchIndex, globalMatches.length - 1)];
    if (!global || global.pageIndex !== safePage) return pageLocalMatches && pageLocalMatches.length > 0 ? 0 : undefined;
    const onPage = globalMatches.filter((m) => m.pageIndex === safePage);
    return onPage.findIndex((m) => m.localIndex === global.localIndex);
  }, [activeSearchIndex, globalMatches, pageLocalMatches, q, safePage]);

  if (preparedPages.length <= 1) {
    return (
      <div className="document-paged document-paged--single">
        <div className="document-paged__body">
          {renderPage(
            pageText,
            q ? pageLocalMatches : undefined,
            q && pageLocalMatches && pageLocalMatches.length > 0 ? (pageLocalActiveIndex ?? 0) : undefined,
          )}
        </div>
      </div>
    );
  }

  const goPrev = () => setPageIndex((i) => Math.max(0, i - 1));
  const goNext = () => setPageIndex((i) => Math.min(preparedPages.length - 1, i + 1));

  const goToPageNumber = () => {
    const parsed = parseInt(pageInput.trim(), 10);
    if (!Number.isFinite(parsed)) {
      setPageInput(String(safePage + 1));
      return;
    }
    const target = Math.min(preparedPages.length, Math.max(1, parsed)) - 1;
    setPageIndex(target);
    setPageInput(String(target + 1));
  };

  const jumpMatchOnPage = (direction: 1 | -1) => {
    if (!q || !pageLocalMatches || pageLocalMatches.length === 0 || !onSearchIndexChange) return;
    const onPage = globalMatches.filter((m) => m.pageIndex === safePage);
    if (onPage.length === 0) return;
    const currentGlobal = globalMatches[Math.min(activeSearchIndex, globalMatches.length - 1)];
    const posOnPage = onPage.findIndex((m) => m === currentGlobal);
    const nextPos = (posOnPage + direction + onPage.length) % onPage.length;
    const globalIdx = globalMatches.indexOf(onPage[nextPos]!);
    onSearchIndexChange(globalIdx >= 0 ? globalIdx : 0);
  };

  return (
    <div className="document-paged">
      <div className="document-paged__toolbar" role="navigation" aria-label="Страницы документа">
        <button type="button" className="btn-secondary btn-sm" disabled={safePage <= 0} onClick={goPrev}>
          ← Страница
        </button>
        <div className="document-paged__goto">
          <span className="document-paged__goto-label">Страница</span>
          <input
            type="number"
            className="document-paged__goto-input"
            min={1}
            max={preparedPages.length}
            value={pageInput}
            aria-label={`Номер страницы, всего ${preparedPages.length}`}
            onChange={(e) => setPageInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                goToPageNumber();
              }
            }}
          />
          <span className="document-paged__goto-total">из {preparedPages.length}</span>
          <button type="button" className="btn-secondary btn-sm" onClick={goToPageNumber}>
            Перейти
          </button>
        </div>
        <button
          type="button"
          className="btn-secondary btn-sm"
          disabled={safePage >= preparedPages.length - 1}
          onClick={goNext}
        >
          Страница →
        </button>
        {q && pageLocalMatches && pageLocalMatches.length > 0 && onSearchIndexChange && (
          <span className="document-paged__search-hint">
            На странице: {pageLocalMatches.length}{' '}
            {pageLocalMatches.length === 1 ? 'совпадение' : 'совпадений'}
            <button type="button" className="btn-secondary btn-sm" onClick={() => jumpMatchOnPage(-1)}>
              ◀
            </button>
            <button type="button" className="btn-secondary btn-sm" onClick={() => jumpMatchOnPage(1)}>
              ▶
            </button>
          </span>
        )}
      </div>
      <div className="document-paged__body">{renderPage(pageText, pageLocalMatches, pageLocalActiveIndex)}</div>
    </div>
  );
}