import { useEffect, useMemo, useState } from 'react';
import { fetchDocumentPageCount } from '../api/backend';
import { DocumentOriginalViewer } from './DocumentOriginalViewer';
import { getDocumentPageCount } from '../lib/documentPages';
import { DocumentPagedView } from './DocumentPagedView';
import { prepareDocLikeSource, renderDocLikeText } from './DocumentTextViews';

export type DocumentTextViewMode = 'processed' | 'source';

type Props = {
  processedText: string;
  originalText?: string;
  documentId: number;
  authToken: string;
  isPdf: boolean;
  hasOriginalFile: boolean;
  initialPdfPageCount?: number | null;
  searchQuery?: string;
  activeSearchIndex?: number;
  onSearchIndexChange?: (index: number) => void;
  viewMode: DocumentTextViewMode;
};

export function DocumentTextViewModeToolbar({
  viewMode,
  onViewModeChange,
  showSource,
}: {
  viewMode: DocumentTextViewMode;
  onViewModeChange: (mode: DocumentTextViewMode) => void;
  showSource: boolean;
}) {
  return (
    <div className="doc-text-view-modes" role="tablist" aria-label="Режим просмотра текста">
      <button
        type="button"
        role="tab"
        aria-selected={viewMode === 'processed'}
        className={viewMode === 'processed' ? 'active' : ''}
        onClick={() => onViewModeChange('processed')}
      >
        Обработанный
      </button>
      {showSource && (
        <button
          type="button"
          role="tab"
          aria-selected={viewMode === 'source'}
          className={viewMode === 'source' ? 'active' : ''}
          onClick={() => onViewModeChange('source')}
        >
          Оригинал
        </button>
      )}
    </div>
  );
}

function PagedTextView({
  text,
  className,
  searchQuery,
  activeSearchIndex,
  onSearchIndexChange,
}: {
  text: string;
  className: string;
  searchQuery: string;
  activeSearchIndex: number;
  onSearchIndexChange?: (index: number) => void;
}) {
  return (
    <DocumentPagedView
      fullText={text}
      searchQuery={searchQuery}
      activeSearchIndex={activeSearchIndex}
      onSearchIndexChange={onSearchIndexChange}
      renderPage={(pageText) => (
        <div className={`${className} doc-text-processed--reader`}>
          {renderDocLikeText(prepareDocLikeSource(pageText))}
        </div>
      )}
    />
  );
}

export function DocumentTextComparePanel({
  processedText,
  originalText = '',
  documentId,
  authToken,
  isPdf,
  hasOriginalFile,
  initialPdfPageCount = null,
  searchQuery = '',
  activeSearchIndex = 0,
  onSearchIndexChange,
  viewMode,
}: Props) {
  const [pdfPageCount, setPdfPageCount] = useState<number | null>(initialPdfPageCount ?? null);

  const pageCount = useMemo(
    () => getDocumentPageCount(processedText, pdfPageCount ?? initialPdfPageCount, originalText),
    [processedText, originalText, pdfPageCount, initialPdfPageCount],
  );

  useEffect(() => {
    setPdfPageCount(initialPdfPageCount ?? null);
  }, [documentId, initialPdfPageCount]);

  useEffect(() => {
    if (!isPdf || !hasOriginalFile) {
      setPdfPageCount(null);
      return;
    }
    if (initialPdfPageCount != null && initialPdfPageCount > 0) {
      return;
    }

    let cancelled = false;
    void fetchDocumentPageCount(authToken, documentId)
      .then((count) => {
        if (!cancelled) setPdfPageCount(count);
      })
      .catch(() => {
        if (!cancelled) setPdfPageCount(null);
      });

    return () => {
      cancelled = true;
    };
  }, [authToken, documentId, isPdf, hasOriginalFile, initialPdfPageCount]);

  if (viewMode === 'source') {
    if (!isPdf || !hasOriginalFile) {
      return <p className="registry-meta">Просмотр оригинала доступен только для сохранённых PDF.</p>;
    }
    return <DocumentOriginalViewer documentId={documentId} authToken={authToken} pageCount={pageCount} />;
  }

  return (
    <PagedTextView
      text={processedText}
      className="doc-text-processed"
      searchQuery={searchQuery}
      activeSearchIndex={activeSearchIndex}
      onSearchIndexChange={onSearchIndexChange}
    />
  );
}
