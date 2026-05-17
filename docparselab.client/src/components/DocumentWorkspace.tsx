import type { ReactNode } from 'react';
import type { ParsedDocument } from '../types/api';
import { DATA_CLASSIFICATION_LABELS, DOCUMENT_TYPE_LABELS, WORKFLOW_STATUS_LABELS } from '../types/office';

type Props = {
  document: ParsedDocument;
  authToken: string | null;
  documentTextLocked: boolean;
  onBack?: () => void;
  onDownloadJson: () => void;
  onExport: (format: 'docx' | 'pdf' | 'signed-pdf') => void;
  onDownloadOriginal?: () => void;
  editLockMessage?: string | null;
  onSendEmail?: () => void;
  onDelete?: () => void;
  sidebar: ReactNode;
  editor: ReactNode;
  aiSummary: ReactNode;
};

export function DocumentWorkspace({
  document,
  authToken,
  documentTextLocked,
  onBack,
  onDownloadJson,
  onExport,
  onDownloadOriginal,
  editLockMessage,
  onSendEmail,
  onDelete,
  sidebar,
  editor,
  aiSummary,
}: Props) {
  const title = document.title?.trim() || document.fileName;
  const status = document.workflowStatus;
  const statusLabel = status ? (WORKFLOW_STATUS_LABELS[status] ?? status) : null;
  const typeLabel =
    DOCUMENT_TYPE_LABELS[document.documentType ?? ''] ?? document.documentType ?? '—';
  const classLabel =
    DATA_CLASSIFICATION_LABELS[document.dataClassification ?? ''] ??
    document.dataClassification ??
    '—';

  return (
    <article className="doc-workspace">
      <header className="doc-workspace__header">
        <div className="doc-workspace__header-start">
          {authToken && onBack && (
            <button type="button" className="btn-ghost" onClick={onBack}>
              ← Реестр
            </button>
          )}
          <div className="doc-workspace__title-block">
            <h2 className="doc-workspace__title">{title}</h2>
            <p className="doc-workspace__subtitle">{document.fileName}</p>
          </div>
          {statusLabel && (
            <span className={`status-badge status-${status}`}>{statusLabel}</span>
          )}
        </div>
        <div className="doc-workspace__header-actions">
          {authToken && onSendEmail && (
            <button type="button" className="btn-secondary btn-sm" onClick={onSendEmail}>
              Email
            </button>
          )}
          {authToken && onDelete && (
            <button type="button" className="btn-secondary btn-sm btn-danger-outline" onClick={onDelete}>
              Удалить
            </button>
          )}
          <button type="button" className="btn-secondary btn-sm" onClick={onDownloadJson}>
            JSON
          </button>
          <button type="button" className="btn-secondary btn-sm" onClick={() => onExport('docx')}>
            DOCX
          </button>
          <button type="button" className="btn-secondary btn-sm" onClick={() => onExport('pdf')}>
            PDF
          </button>
          {(document.signatureCount ?? 0) > 0 && (
            <button type="button" className="btn-secondary btn-sm" onClick={() => onExport('signed-pdf')}>
              PDF с подписью
            </button>
          )}
          {document.hasOriginalFile && onDownloadOriginal && (
            <button type="button" className="btn-secondary btn-sm" onClick={onDownloadOriginal}>
              Оригинал
            </button>
          )}
        </div>
      </header>

      <dl className="doc-workspace__meta-strip" aria-label="Сводка по документу">
        <div>
          <dt>Формат</dt>
          <dd>{document.originalFileType?.toUpperCase() ?? '—'}</dd>
        </div>
        <div>
          <dt>Загружен</dt>
          <dd>{new Date(document.uploadedAt).toLocaleString()}</dd>
        </div>
        <div>
          <dt>Тип</dt>
          <dd>{typeLabel}</dd>
        </div>
        <div>
          <dt>Классификация</dt>
          <dd>{classLabel}</dd>
        </div>
        {document.departmentName && (
          <div>
            <dt>Подразделение</dt>
            <dd title={document.departmentName}>{document.departmentName}</dd>
          </div>
        )}
        {document.responsibleUserEmail && (
          <div>
            <dt>Ответственный</dt>
            <dd title={document.responsibleUserEmail}>{document.responsibleUserEmail}</dd>
          </div>
        )}
      </dl>

      {documentTextLocked && (
        <p className="office-card-readonly-hint doc-workspace__lock-banner">
          Редактирование текста недоступно: документ на согласовании, согласован или в архиве.
        </p>
      )}
      {editLockMessage && (
        <p className="office-card-readonly-hint doc-workspace__lock-banner">{editLockMessage}</p>
      )}

      <div className="doc-workspace__grid">
        <aside className="doc-workspace__sidebar" aria-label="Управление документом">
          {sidebar}
        </aside>
        <div className="doc-workspace__main">
          <section className="doc-workspace__editor" aria-label="Текст документа">
            {editor}
          </section>
          <section className="doc-workspace__ai" aria-label="AI-описание">
            {aiSummary}
          </section>
        </div>
      </div>
    </article>
  );
}
