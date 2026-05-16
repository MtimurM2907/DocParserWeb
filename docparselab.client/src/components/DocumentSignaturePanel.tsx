import { useCallback, useEffect, useState } from 'react';
import type { ParsedDocument } from '../types/api';
import type { DocumentSignature, SignatureVerification } from '../types/office';
import {
  fetchDocumentSignatures,
  signDocument,
  verifyDocumentSignature,
} from '../api/office';

type Props = {
  token: string;
  document: ParsedDocument;
  onUpdated: (doc: ParsedDocument) => void;
  onError: (msg: string) => void;
};

export function DocumentSignaturePanel({ token, document, onUpdated, onError }: Props) {
  const [expanded, setExpanded] = useState(true);
  const [signatures, setSignatures] = useState<DocumentSignature[]>([]);
  const [verify, setVerify] = useState<SignatureVerification | null>(null);
  const [comment, setComment] = useState('');
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [sigs, ver] = await Promise.all([
        fetchDocumentSignatures(token, document.id),
        verifyDocumentSignature(token, document.id),
      ]);
      setSignatures(sigs);
      setVerify(ver);
    } catch {
      setSignatures([]);
      setVerify(null);
    } finally {
      setLoading(false);
    }
  }, [token, document.id]);

  useEffect(() => {
    if (!expanded) return;
    void load();
  }, [expanded, load, document.workflowStatus, document.signatureCount]);

  const handleSign = async () => {
    setBusy(true);
    onError('');
    try {
      const updated = await signDocument(token, document.id, comment.trim() || undefined);
      onUpdated(updated);
      setComment('');
      await load();
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Не удалось подписать документ');
    } finally {
      setBusy(false);
    }
  };

  const status = document.workflowStatus ?? 'Draft';

  return (
    <div className="office-card-panel office-signature-panel">
      <button
        type="button"
        className="office-card-panel-toggle"
        onClick={() => setExpanded((e) => !e)}
        aria-expanded={expanded}
      >
        <h3>Цифровая подпись</h3>
        <span className="office-card-panel-chevron">{expanded ? '▲' : '▼'}</span>
      </button>
      {expanded && (
        <div className="office-card-panel-body">
          <p className="registry-meta signature-intro">
            Внутренняя ЭП: фиксируется SHA-256 хеш текста, подписант и время. Это не УКЭП (КриптоПро), но
            позволяет проверить, что текст после подписания не менялся.
          </p>

          {loading && <p className="registry-meta">Загрузка…</p>}

          {!loading && verify && (
            <div
              className={`signature-integrity ${
                verify.hasSignatures
                  ? verify.textMatchesLastSignature
                    ? 'signature-integrity--ok'
                    : 'signature-integrity--bad'
                  : ''
              }`}
            >
              {verify.hasSignatures ? (
                verify.textMatchesLastSignature ? (
                  <span>Целостность текста подтверждена (совпадает с последней подписью).</span>
                ) : (
                  <span>Текст изменён после подписи — подпись недействительна для текущей версии.</span>
                )
              ) : (
                <span>Подписей пока нет.</span>
              )}
            </div>
          )}

          {document.canSign && status === 'Approved' && (
            <div className="signature-sign-form">
              <label className="parse-field">
                <span className="parse-field-label">Комментарий к подписи (необязательно)</span>
                <input
                  type="text"
                  value={comment}
                  onChange={(e) => setComment(e.target.value)}
                  disabled={busy}
                  placeholder="Например: утверждаю к исполнению"
                />
              </label>
              <button type="button" className="btn-primary office-sidebar-btn" disabled={busy} onClick={() => void handleSign()}>
                {busy ? 'Подписание…' : 'Подписать документ'}
              </button>
            </div>
          )}

          {status === 'Signed' && document.lastSignerEmail && (
            <p className="share-success">
              Подписан: {document.lastSignerEmail}
              {document.lastSignedAt && ` · ${new Date(document.lastSignedAt).toLocaleString()}`}
            </p>
          )}

          {signatures.length > 0 && (
            <ul className="signature-list">
              {signatures.map((s) => (
                <li key={s.id} className="signature-list-item">
                  <div className="signature-list-head">
                    <strong>{s.signerDisplayName || s.signerEmail}</strong>
                    <span className="signature-list-date">{new Date(s.signedAt).toLocaleString()}</span>
                  </div>
                  <div className="signature-list-meta">
                    {s.signerRole} · {s.signatureKind === 'internal' ? 'внутренняя ЭП' : s.signatureKind}
                  </div>
                  {s.comment && <p className="signature-list-comment">{s.comment}</p>}
                  <code className="signature-hash" title="SHA-256 текста на момент подписи">
                    {s.textHashSha256.slice(0, 16)}…
                  </code>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
