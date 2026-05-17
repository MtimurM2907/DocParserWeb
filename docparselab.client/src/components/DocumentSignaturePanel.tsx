import { useCallback, useEffect, useState } from 'react';
import type { ParsedDocument } from '../types/api';
import type { CadesCertificateInfo } from '../lib/cryptopro/cadesPlugin';
import {
  cmsBase64ToSignatureFile,
  getCadesPlugin,
  isCadesPluginAvailable,
  listCadesCertificates,
  signDetachedCmsBase64,
} from '../lib/cryptopro/cadesPlugin';
import type { DocumentSignature, SignatureVerification } from '../types/office';
import {
  fetchDocumentSignatures,
  fetchDocumentSigningPayload,
  signDocument,
  signExternalDocument,
  verifyDocumentSignature,
} from '../api/office';

type Props = {
  token: string;
  document: ParsedDocument;
  onUpdated: (doc: ParsedDocument) => void;
  onError: (msg: string) => void;
};

type CadesUiState = 'idle' | 'checking' | 'ready' | 'unavailable';

export function DocumentSignaturePanel({ token, document, onUpdated, onError }: Props) {
  const [expanded, setExpanded] = useState(true);
  const [signatures, setSignatures] = useState<DocumentSignature[]>([]);
  const [verify, setVerify] = useState<SignatureVerification | null>(null);
  const [comment, setComment] = useState('');
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [extSubject, setExtSubject] = useState('');
  const [extThumb, setExtThumb] = useState('');
  const [extFile, setExtFile] = useState<File | null>(null);
  const [cadesState, setCadesState] = useState<CadesUiState>('idle');
  const [cadesCerts, setCadesCerts] = useState<CadesCertificateInfo[]>([]);
  const [cadesCertThumb, setCadesCertThumb] = useState('');

  const status = document.workflowStatus ?? 'Draft';
  const canUseExternal = document.canSign && (status === 'Approved' || status === 'Signed');

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

  useEffect(() => {
    if (!expanded || !canUseExternal) {
      setCadesState('idle');
      return;
    }

    let cancelled = false;
    setCadesState('checking');
    void (async () => {
      const ok = await isCadesPluginAvailable();
      if (cancelled) return;
      if (!ok) {
        setCadesState('unavailable');
        return;
      }
      try {
        const plugin = await getCadesPlugin();
        const certs = await listCadesCertificates(plugin);
        if (cancelled) return;
        setCadesCerts(certs);
        setCadesCertThumb((prev) => prev || certs[0]?.thumbprint || '');
        setCadesState('ready');
      } catch {
        if (!cancelled) setCadesState('unavailable');
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [expanded, canUseExternal, document.id]);

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

  const registerExternal = async (form: FormData) => {
    setBusy(true);
    onError('');
    try {
      onUpdated(await signExternalDocument(token, document.id, form));
      setExtFile(null);
      await load();
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Ошибка внешней подписи');
    } finally {
      setBusy(false);
    }
  };

  const handleSignCryptoPro = async () => {
    if (!cadesCertThumb) {
      onError('Выберите сертификат для подписи.');
      return;
    }

    setBusy(true);
    onError('');
    try {
      const payload = await fetchDocumentSigningPayload(token, document.id);
      const plugin = await getCadesPlugin();
      const cert = cadesCerts.find((c) => c.thumbprint === cadesCertThumb);
      const signatureB64 = await signDetachedCmsBase64(plugin, payload.contentBase64, cadesCertThumb);

      const fd = new FormData();
      if (cert?.subject) fd.set('CertificateSubject', cert.subject);
      fd.set('CertificateThumbprint', cadesCertThumb);
      if (comment.trim()) fd.set('Comment', comment.trim());
      fd.set('signatureFile', cmsBase64ToSignatureFile(signatureB64, `${document.fileName}.sig`));

      onUpdated(await signExternalDocument(token, document.id, fd));
      setComment('');
      await load();
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Не удалось подписать через КриптоПро');
    } finally {
      setBusy(false);
    }
  };

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
            Внутренняя ЭП фиксирует SHA-256 хеш текста. УКЭП через КриптоПро — отсоединённая CMS-подпись канонического
            текста документа (нужен плагин и сертификат в хранилище «Личные»).
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
                {busy ? 'Подписание…' : 'Подписать (внутренняя ЭП)'}
              </button>
            </div>
          )}

          {canUseExternal && (
            <div className="signature-sign-form signature-cryptopro-form">
              <h4>УКЭП — КриптоПро</h4>
              {cadesState === 'checking' && <p className="registry-meta">Проверка плагина КриптоПро…</p>}
              {cadesState === 'unavailable' && (
                <p className="registry-meta signature-cryptopro-hint">
                  Плагин не обнаружен. Установите{' '}
                  <a href="https://www.cryptopro.ru/products/cades/plugin" target="_blank" rel="noreferrer">
                    КриптоПро ЭЦП Browser plug-in
                  </a>{' '}
                  (и расширение для браузера), перезапустите браузер. При необходимости положите{' '}
                  <code>cadesplugin_api.js</code> в <code>wwwroot</code>.
                </p>
              )}
              {cadesState === 'ready' && (
                <>
                  <label className="parse-field">
                    <span className="parse-field-label">Сертификат</span>
                    <select
                      className="app-select-native signature-cert-select"
                      value={cadesCertThumb}
                      onChange={(e) => setCadesCertThumb(e.target.value)}
                      disabled={busy}
                    >
                      {cadesCerts.length === 0 && <option value="">Нет сертификатов в «Личные»</option>}
                      {cadesCerts.map((c) => (
                        <option key={c.thumbprint} value={c.thumbprint}>
                          {c.subject.length > 72 ? `${c.subject.slice(0, 72)}…` : c.subject}
                          {c.validTo ? ` · до ${c.validTo}` : ''}
                        </option>
                      ))}
                    </select>
                  </label>
                  <button
                    type="button"
                    className="btn-primary office-sidebar-btn"
                    disabled={busy || !cadesCertThumb || cadesCerts.length === 0}
                    onClick={() => void handleSignCryptoPro()}
                  >
                    {busy ? 'Подписание…' : 'Подписать УКЭП (КриптоПро)'}
                  </button>
                </>
              )}

              <details className="signature-manual-upload">
                <summary>Загрузить готовый файл подписи (.sig / .p7s)</summary>
                <div className="signature-manual-upload__body">
                  <label className="parse-field">
                    <span className="parse-field-label">Субъект сертификата</span>
                    <input type="text" value={extSubject} onChange={(e) => setExtSubject(e.target.value)} disabled={busy} />
                  </label>
                  <label className="parse-field">
                    <span className="parse-field-label">Отпечаток</span>
                    <input type="text" value={extThumb} onChange={(e) => setExtThumb(e.target.value)} disabled={busy} />
                  </label>
                  <label className="parse-field">
                    <span className="parse-field-label">Файл подписи</span>
                    <input type="file" accept=".sig,.p7s,.p7m" onChange={(e) => setExtFile(e.target.files?.[0] ?? null)} />
                  </label>
                  <button
                    type="button"
                    className="btn-secondary office-sidebar-btn"
                    disabled={busy}
                    onClick={() => {
                      const fd = new FormData();
                      if (extSubject.trim()) fd.set('CertificateSubject', extSubject.trim());
                      if (extThumb.trim()) fd.set('CertificateThumbprint', extThumb.trim());
                      if (extFile) fd.set('signatureFile', extFile);
                      void registerExternal(fd);
                    }}
                  >
                    Зарегистрировать файл УКЭП
                  </button>
                </div>
              </details>
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
                    {s.signerRole} · {s.signatureKind === 'internal' ? 'внутренняя ЭП' : 'УКЭП'}
                    {s.externalCryptoVerified === true && ' · CMS OK'}
                    {s.externalCryptoVerified === false && ' · CMS не проверена'}
                  </div>
                  {s.certificateSubject && (
                    <p className="signature-list-comment" title={s.certificateSubject}>
                      {s.certificateSubject.length > 100
                        ? `${s.certificateSubject.slice(0, 100)}…`
                        : s.certificateSubject}
                    </p>
                  )}
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
