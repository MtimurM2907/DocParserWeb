import { useEffect, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import {
  DocumentTextComparePanel,
  DocumentTextViewModeToolbar,
  type DocumentTextViewMode,
} from './DocumentTextComparePanel';

function useModalLock(onClose: () => void) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.style.overflow = prev;
    };
  }, [onClose]);
}

type Props = {
  open: boolean;
  onClose: () => void;
  title: string;
  fileName: string;
  processedText: string;
  originalText?: string;
  initialPdfPageCount?: number | null;
  documentId: number;
  authToken: string;
  isPdf: boolean;
  hasOriginalFile: boolean;
  viewMode: DocumentTextViewMode;
  onViewModeChange: (mode: DocumentTextViewMode) => void;
  showSource: boolean;
  searchQuery: string;
  onSearchQueryChange: (q: string) => void;
  onSearchPrev: () => void;
  onSearchNext: () => void;
  searchMeta: string;
  searchDisabled: boolean;
  activeSearchIndex: number;
  onSearchIndexChange: (index: number) => void;
  children?: ReactNode;
};

export function DocumentTextViewModal({
  open,
  onClose,
  title,
  fileName,
  processedText,
  originalText,
  initialPdfPageCount,
  documentId,
  authToken,
  isPdf,
  hasOriginalFile,
  viewMode,
  onViewModeChange,
  showSource,
  searchQuery,
  onSearchQueryChange,
  onSearchPrev,
  onSearchNext,
  searchMeta,
  searchDisabled,
  activeSearchIndex,
  onSearchIndexChange,
}: Props) {
  useModalLock(onClose);

  if (!open) return null;

  const isSource = viewMode === 'source';

  return createPortal(
    <div className="modal-overlay doc-text-modal-overlay" onClick={onClose}>
      <div
        className={`modal-card modal-card-doc-text${isSource ? ' modal-card-doc-text--source' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-label={isSource ? 'Оригинал документа' : 'Просмотр текста документа'}
        onClick={(e) => e.stopPropagation()}
      >
        <header className="doc-text-modal__header">
          <div className="doc-text-modal__titles">
            <h3>{title}</h3>
            <p className="doc-text-modal__meta">{fileName}</p>
          </div>
          <button type="button" className="doc-text-modal__close" onClick={onClose} aria-label="Закрыть">
            ×
          </button>
        </header>

        <div className="doc-text-modal__toolbar">
          <DocumentTextViewModeToolbar
            viewMode={viewMode}
            onViewModeChange={onViewModeChange}
            showSource={showSource}
          />
          {!isSource && (
            <div className="text-search-row doc-text-modal__search">
              <label className="text-search-label" htmlFor="text-search-modal">
                Поиск
              </label>
              <input
                id="text-search-modal"
                type="search"
                className="text-search-input"
                placeholder="Найти в тексте…"
                value={searchQuery}
                onChange={(e) => onSearchQueryChange(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    if (e.shiftKey) onSearchPrev();
                    else onSearchNext();
                  }
                }}
                autoComplete="off"
              />
              <button type="button" className="btn-secondary btn-sm" onClick={onSearchPrev} disabled={searchDisabled}>
                Назад
              </button>
              <button type="button" className="btn-secondary btn-sm" onClick={onSearchNext} disabled={searchDisabled}>
                Далее
              </button>
              <span className="text-search-meta" aria-live="polite">
                {searchMeta}
              </span>
            </div>
          )}
        </div>

        <div className={`doc-text-modal__body${isSource ? ' doc-text-modal__body--source' : ''}`}>
          <DocumentTextComparePanel
            processedText={processedText}
            originalText={originalText}
            documentId={documentId}
            authToken={authToken}
            isPdf={isPdf}
            hasOriginalFile={hasOriginalFile}
            initialPdfPageCount={initialPdfPageCount}
            searchQuery={searchQuery}
            activeSearchIndex={activeSearchIndex}
            onSearchIndexChange={onSearchIndexChange}
            viewMode={viewMode}
          />
        </div>
      </div>
    </div>,
    document.body,
  );
}
