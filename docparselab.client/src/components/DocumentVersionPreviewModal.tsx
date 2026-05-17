import { useEffect, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import type { DocumentVersionDetail, VersionDiff } from '../types/office';

const CHANGE_LABELS: Record<string, string> = {
  edit: 'Правка текста',
  parse: 'Первичный разбор',
  import: 'Импорт',
};

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

function VersionModalShell({
  ariaLabel,
  title,
  subtitle,
  onClose,
  children,
  footer,
}: {
  ariaLabel: string;
  title: string;
  subtitle?: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
}) {
  useModalLock(onClose);

  return (
    <div className="modal-overlay version-modal-overlay" onClick={onClose}>
      <div
        className="modal-card modal-card-version-preview"
        role="dialog"
        aria-modal="true"
        aria-label={ariaLabel}
        onClick={(e) => e.stopPropagation()}
      >
        <header className="version-preview-modal__header">
          <div className="version-preview-modal__titles">
            <h3>{title}</h3>
            {subtitle ? <p className="version-preview-modal__meta">{subtitle}</p> : null}
          </div>
          <button type="button" className="version-preview-modal__close" onClick={onClose} aria-label="Закрыть">
            ×
          </button>
        </header>
        <div className="version-preview-modal__body">{children}</div>
        {footer ? <footer className="version-preview-modal__footer">{footer}</footer> : null}
      </div>
    </div>
  );
}

type PreviewProps = {
  open: boolean;
  version: DocumentVersionDetail | null;
  loading: boolean;
  error?: string | null;
  onClose: () => void;
  onApplyVersion?: (text: string) => void;
  onRestoreVersion?: (text: string) => Promise<void>;
  applyDisabled?: boolean;
  restoring?: boolean;
};

export function DocumentVersionPreviewModal({
  open,
  version,
  loading,
  error,
  onClose,
  onApplyVersion,
  onRestoreVersion,
  applyDisabled = false,
  restoring = false,
}: PreviewProps) {
  if (!open || typeof document === 'undefined') return null;

  const subtitle = version
    ? [
        CHANGE_LABELS[version.changeType] ?? version.changeType,
        `${version.textLength.toLocaleString()} симв.`,
        new Date(version.createdAt).toLocaleString(),
        version.createdByEmail,
      ]
        .filter(Boolean)
        .join(' · ')
    : undefined;

  const handleRestore = onRestoreVersion
    ? () => {
        if (!version) return;
        void onRestoreVersion(version.text)
          .then(onClose)
          .catch(() => undefined);
      }
    : undefined;

  return createPortal(
    <VersionModalShell
      ariaLabel="Просмотр версии документа"
      title={version ? `Версия ${version.versionNumber}` : 'Просмотр версии'}
      subtitle={subtitle}
      onClose={onClose}
      footer={
        version && !loading ? (
          <>
            {onApplyVersion && (
              <button
                type="button"
                className="btn-secondary"
                disabled={applyDisabled}
                title={applyDisabled ? 'Редактирование недоступно для текущего статуса' : undefined}
                onClick={() => {
                  onApplyVersion(version.text);
                  onClose();
                }}
              >
                В редактор
              </button>
            )}
            {handleRestore && (
              <button
                type="button"
                className="btn-primary"
                disabled={applyDisabled || restoring}
                title={applyDisabled ? 'Редактирование недоступно для текущего статуса' : undefined}
                onClick={handleRestore}
              >
                {restoring ? 'Сохранение…' : 'Сделать текущей'}
              </button>
            )}
            <button type="button" onClick={onClose}>
              Закрыть
            </button>
          </>
        ) : undefined
      }
    >
      {loading && <p className="registry-meta version-preview-modal__status">Загрузка версии…</p>}
      {error && <p className="office-card-error">{error}</p>}
      {!loading && version && (
        <pre className="text-output version-preview-modal__text">{version.text}</pre>
      )}
    </VersionModalShell>,
    document.body,
  );
}

type DiffProps = {
  open: boolean;
  diff: VersionDiff | null;
  fromLabel: string;
  toLabel: string;
  onClose: () => void;
};

export function DocumentVersionDiffModal({ open, diff, fromLabel, toLabel, onClose }: DiffProps) {
  if (!open || !diff || typeof document === 'undefined') return null;

  return createPortal(
    <VersionModalShell
      ariaLabel="Сравнение версий"
      title="Сравнение версий"
      subtitle={`${fromLabel} → ${toLabel}`}
      onClose={onClose}
      footer={
        <button type="button" onClick={onClose}>
          Закрыть
        </button>
      }
    >
      <pre className="version-diff-output version-diff-output--modal">
        {diff.lines.map((line, i) => (
          <div key={i} className={`diff-line diff-line--${line.kind}`}>
            {line.kind === 'removed' ? '- ' : line.kind === 'added' ? '+ ' : '  '}
            {line.text}
          </div>
        ))}
      </pre>
    </VersionModalShell>,
    document.body,
  );
}
