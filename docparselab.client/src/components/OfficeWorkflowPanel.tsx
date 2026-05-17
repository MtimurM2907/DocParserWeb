import { useEffect, useMemo, useState } from 'react';
import type { ParsedDocument } from '../types/api';
import type { UserBrief, WorkflowHistoryItem } from '../types/office';
import { WORKFLOW_STATUS_LABELS } from '../types/office';
import { AppMultiSelect } from './AppMultiSelect';
import {
  approveDocument,
  archiveDocument,
  fetchOfficeUsers,
  fetchWorkflowHistory,
  rejectDocument,
  returnDocumentToDraft,
  submitForApproval,
} from '../api/office';

type Props = {
  token: string;
  document: ParsedDocument;
  onUpdated: (doc: ParsedDocument) => void;
  onError: (msg: string) => void;
};

export function OfficeWorkflowPanel({ token, document, onUpdated, onError }: Props) {
  const [users, setUsers] = useState<UserBrief[]>([]);
  const [approverIds, setApproverIds] = useState<number[]>([]);
  const [comment, setComment] = useState('');

  const approverOptions = useMemo(
    () => users.map((u) => ({ value: String(u.id), label: u.displayName || u.email })),
    [users],
  );
  const [rejectComment, setRejectComment] = useState('');
  const [history, setHistory] = useState<WorkflowHistoryItem[]>([]);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void fetchOfficeUsers(token).then(setUsers).catch(() => setUsers([]));
  }, [token]);

  useEffect(() => {
    void fetchWorkflowHistory(token, document.id)
      .then(setHistory)
      .catch(() => setHistory([]));
  }, [token, document.id, document.workflowStatus]);

  const run = async (fn: () => Promise<ParsedDocument>) => {
    setBusy(true);
    onError('');
    try {
      const updated = await fn();
      onUpdated(updated);
      setComment('');
      setRejectComment('');
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Ошибка операции');
    } finally {
      setBusy(false);
    }
  };

  const status = document.workflowStatus ?? 'Draft';
  const statusLabel = WORKFLOW_STATUS_LABELS[status] ?? status;

  return (
    <div className="office-sidebar-card office-workflow">
      <div className="office-sidebar-card__head">
        <h3>Согласование</h3>
        <div className="office-workflow-status-row">
          <span className="office-workflow-status-label">Статус</span>
          <span className={`status-badge status-${status}`}>{statusLabel}</span>
        </div>
        {document.currentApproverEmail && (
          <p className="office-workflow-approver">
            Согласующий: <span>{document.currentApproverEmail}</span>
          </p>
        )}
        {document.workflowComment && (
          <p className="office-workflow-comment">Комментарий: {document.workflowComment}</p>
        )}
      </div>

      <div className="office-sidebar-card__body">
        {(status === 'Draft' || status === 'Rejected') && (
          <div className="office-workflow-form">
            <label className="parse-field">
              <span className="parse-field-label">Согласующие (порядок этапов)</span>
              <AppMultiSelect
                values={approverIds.map(String)}
                onChange={(ids) => setApproverIds(ids.map((id) => parseInt(id, 10)).filter((n) => !Number.isNaN(n)))}
                options={approverOptions}
                placeholder="Выберите согласующих"
              />
            </label>
            <label className="parse-field">
              <span className="parse-field-label">Комментарий (необязательно)</span>
              <input type="text" value={comment} onChange={(e) => setComment(e.target.value)} />
            </label>
            <button
              type="button"
              className="btn-primary office-sidebar-btn"
              disabled={busy}
              onClick={() => {
                if (approverIds.length === 0) {
                  onError('Выберите хотя бы одного согласующего');
                  return;
                }
                void run(() => submitForApproval(token, document.id, approverIds, comment || undefined));
              }}
            >
              {busy ? 'Отправка…' : 'Отправить на согласование'}
            </button>
          </div>
        )}

        {document.canApprove && status === 'OnApproval' && (
          <div className="office-workflow-form">
            <label className="parse-field">
              <span className="parse-field-label">Комментарий к согласованию</span>
              <input type="text" value={comment} onChange={(e) => setComment(e.target.value)} />
            </label>
            <div className="office-workflow-actions">
              <button
                type="button"
                className="btn-approve office-sidebar-btn"
                disabled={busy}
                onClick={() => void run(() => approveDocument(token, document.id, comment || undefined))}
              >
                Согласовать
              </button>
            </div>
            <label className="parse-field">
              <span className="parse-field-label">Комментарий при возврате (обязательно)</span>
              <input type="text" value={rejectComment} onChange={(e) => setRejectComment(e.target.value)} />
            </label>
            <button
              type="button"
              className="btn-reject office-sidebar-btn"
              disabled={busy}
              onClick={() => {
                if (!rejectComment.trim()) {
                  onError('Укажите комментарий при возврате');
                  return;
                }
                void run(() => rejectDocument(token, document.id, rejectComment.trim()));
              }}
            >
              Вернуть на доработку
            </button>
          </div>
        )}

        {(status === 'Rejected' || status === 'Approved') && (
          <div className="office-workflow-actions">
            <button
              type="button"
              className="btn-secondary office-sidebar-btn"
              disabled={busy}
              onClick={() => void run(() => returnDocumentToDraft(token, document.id))}
            >
              Вернуть в черновик
            </button>
          </div>
        )}

        {status === 'Signed' && (
          <div className="office-workflow-actions">
            <button
              type="button"
              className="btn-secondary office-sidebar-btn"
              disabled={busy}
              onClick={() => void run(() => archiveDocument(token, document.id))}
            >
              В архив
            </button>
          </div>
        )}

        {history.length > 0 && (
          <div className="office-history">
            <h4>История</h4>
            <ul>
              {history.map((h) => (
                <li key={h.id}>
                  <span className="office-history-date">{new Date(h.createdAt).toLocaleString()}</span>
                  {' — '}
                  {h.action}
                  {h.userEmail ? ` (${h.userEmail})` : ''}
                  {h.comment ? `: ${h.comment}` : ''}
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </div>
  );
}
