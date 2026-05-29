import { useEffect, useRef, useState } from 'react';
import type { ParsedDocument } from '../types/api';
import type { ApprovalStep, UserBrief, WorkflowHistoryItem } from '../types/office';
import { WORKFLOW_STATUS_LABELS } from '../types/office';
import { ApproverPickerField } from './ApproverPickerField';
import {
  approveDocument,
  archiveDocument,
  fetchApprovalCandidates,
  fetchApprovalSteps,
  fetchCurrentUser,
  fetchWorkflowHistory,
  rejectDocument,
  resubmitForApproval,
  returnDocumentToDraft,
  submitForApproval,
} from '../api/office';

type Props = {
  token: string;
  document: ParsedDocument;
  onUpdated: (doc: ParsedDocument) => void;
  onError: (msg: string) => void;
};

const STEP_STATUS_LABELS: Record<string, string> = {
  Pending: 'Ожидает',
  Approved: 'Согласован',
  Rejected: 'Возвращён',
};

export function OfficeWorkflowPanel({ token, document, onUpdated, onError }: Props) {
  const [candidates, setCandidates] = useState<UserBrief[]>([]);
  const [departmentName, setDepartmentName] = useState<string | null>(null);
  const [approverIds, setApproverIds] = useState<number[]>([]);
  const [approvalSteps, setApprovalSteps] = useState<ApprovalStep[]>([]);
  const [comment, setComment] = useState('');
  const [rejectComment, setRejectComment] = useState('');
  const [history, setHistory] = useState<WorkflowHistoryItem[]>([]);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);

  useEffect(() => {
    void fetchCurrentUser(token)
      .then((u) => setDepartmentName(u.departmentName ?? null))
      .catch(() => setDepartmentName(null));
    void fetchApprovalCandidates(token)
      .then(setCandidates)
      .catch(() => setCandidates([]));
  }, [token]);

  useEffect(() => {
    void fetchWorkflowHistory(token, document.id)
      .then(setHistory)
      .catch(() => setHistory([]));
  }, [token, document.id, document.workflowStatus]);

  useEffect(() => {
    const status = document.workflowStatus ?? 'Draft';
    if (status !== 'Rejected' && status !== 'OnApproval' && status !== 'Approved') {
      setApprovalSteps([]);
      return;
    }
    void fetchApprovalSteps(token, document.id)
      .then(setApprovalSteps)
      .catch(() => setApprovalSteps([]));
  }, [token, document.id, document.workflowStatus]);

  const run = async (fn: () => Promise<ParsedDocument>) => {
    if (busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    onError('');
    try {
      const updated = await fn();
      onUpdated(updated);
      setComment('');
      setRejectComment('');
      void fetchWorkflowHistory(token, document.id).then(setHistory).catch(() => setHistory([]));
      if (updated.workflowStatus === 'OnApproval' || updated.workflowStatus === 'Approved' || updated.workflowStatus === 'Rejected') {
        void fetchApprovalSteps(token, document.id).then(setApprovalSteps).catch(() => setApprovalSteps([]));
      }
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Ошибка операции');
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };

  const status = document.workflowStatus ?? 'Draft';
  const statusLabel = WORKFLOW_STATUS_LABELS[status] ?? status;
  const hasApprovalRoute = approvalSteps.length > 0;

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
        {status === 'Draft' && (
          <div className="office-workflow-form">
            <label className="parse-field">
              <span className="parse-field-label">Согласующие (порядок этапов)</span>
              <ApproverPickerField
                token={token}
                selectedIds={approverIds}
                candidates={candidates}
                departmentName={departmentName}
                onChange={setApproverIds}
                disabled={busy}
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
                  onError('Выберите хотя бы одного согласующего из вашего подразделения');
                  return;
                }
                void run(() => submitForApproval(token, document.id, approverIds, comment || undefined));
              }}
            >
              {busy ? 'Отправка…' : 'Отправить на согласование'}
            </button>
          </div>
        )}

        {status === 'Rejected' && (
          <div className="office-workflow-form">
            {hasApprovalRoute ? (
              <div className="office-approval-route">
                <span className="parse-field-label">Маршрут согласования (без изменений)</span>
                <ol className="office-approval-route__list">
                  {approvalSteps.map((step) => (
                    <li key={step.stepOrder} className={`office-approval-route__item status-${step.status}`}>
                      <span className="office-approval-route__order">{step.stepOrder}.</span>
                      <span className="office-approval-route__name">
                        {step.approverEmail ?? `ID ${step.approverUserId}`}
                      </span>
                      <span className="office-approval-route__status">
                        {STEP_STATUS_LABELS[step.status] ?? step.status}
                      </span>
                    </li>
                  ))}
                </ol>
                <p className="office-approval-route__hint">
                  После доработки документ снова пойдёт по этой же цепочке согласующих.
                </p>
              </div>
            ) : (
              <p className="registry-meta">
                Маршрут не найден. Верните документ в черновик и назначьте согласующих заново.
              </p>
            )}
            <label className="parse-field">
              <span className="parse-field-label">Комментарий к повторной отправке (необязательно)</span>
              <input type="text" value={comment} onChange={(e) => setComment(e.target.value)} />
            </label>
            <button
              type="button"
              className="btn-primary office-sidebar-btn"
              disabled={busy || !hasApprovalRoute}
              onClick={() => void run(() => resubmitForApproval(token, document.id, comment || undefined))}
            >
              {busy ? 'Отправка…' : 'Повторно отправить на согласование'}
            </button>
          </div>
        )}

        {document.canApprove && status === 'OnApproval' && (
          <div className="office-workflow-form">
            {hasApprovalRoute && (
              <div className="office-approval-route office-approval-route--compact">
                <span className="parse-field-label">Этапы согласования</span>
                <ol className="office-approval-route__list">
                  {approvalSteps.map((step) => (
                    <li
                      key={step.stepOrder}
                      className={`office-approval-route__item status-${step.status}${
                        document.currentApproverEmail &&
                        step.approverEmail === document.currentApproverEmail
                          ? ' is-current'
                          : ''
                      }`}
                    >
                      <span className="office-approval-route__order">{step.stepOrder}.</span>
                      <span className="office-approval-route__name">
                        {step.approverEmail ?? `ID ${step.approverUserId}`}
                      </span>
                      <span className="office-approval-route__status">
                        {STEP_STATUS_LABELS[step.status] ?? step.status}
                      </span>
                    </li>
                  ))}
                </ol>
              </div>
            )}
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
