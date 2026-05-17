import { useEffect, useMemo, useState } from 'react';
import type { ParsedDocument } from '../types/api';
import type { Department, UserBrief } from '../types/office';
import { DATA_CLASSIFICATION_LABELS, DOCUMENT_TYPE_LABELS } from '../types/office';
import { fetchDepartments, fetchOfficeUsers, updateDocumentMetadata } from '../api/office';
import { AppSelect, optionsFromLabels } from './AppSelect';

type Props = {
  token: string;
  document: ParsedDocument;
  onUpdated: (doc: ParsedDocument) => void;
  onError: (msg: string) => void;
};

export function DocumentMetadataPanel({ token, document, onUpdated, onError }: Props) {
  const [title, setTitle] = useState(document.title ?? document.fileName);
  const [documentType, setDocumentType] = useState(document.documentType ?? 'general');
  const [departmentId, setDepartmentId] = useState(document.departmentId != null ? String(document.departmentId) : '');
  const [responsibleUserId, setResponsibleUserId] = useState(
    document.responsibleUserId != null ? String(document.responsibleUserId) : '',
  );
  const [tags, setTags] = useState(document.tags ?? '');
  const [dataClassification, setDataClassification] = useState(document.dataClassification ?? 'Internal');
  const [departments, setDepartments] = useState<Department[]>([]);
  const [users, setUsers] = useState<UserBrief[]>([]);
  const [busy, setBusy] = useState(false);
  const [expanded, setExpanded] = useState(true);

  const readOnly = document.canEdit === false;

  useEffect(() => {
    setTitle(document.title ?? document.fileName);
    setDocumentType(document.documentType ?? 'general');
    setDepartmentId(document.departmentId != null ? String(document.departmentId) : '');
    setResponsibleUserId(document.responsibleUserId != null ? String(document.responsibleUserId) : '');
    setTags(document.tags ?? '');
    setDataClassification(document.dataClassification ?? 'Internal');
  }, [document.id, document.title, document.fileName, document.documentType, document.departmentId, document.responsibleUserId, document.tags, document.dataClassification]);

  useEffect(() => {
    void fetchDepartments(token).then(setDepartments).catch(() => setDepartments([]));
    void fetchOfficeUsers(token).then(setUsers).catch(() => setUsers([]));
  }, [token]);

  const documentTypeOptions = useMemo(() => optionsFromLabels(DOCUMENT_TYPE_LABELS), []);
  const departmentOptions = useMemo(
    () => [
      { value: '', label: '— не указано —' },
      ...departments.map((d) => ({ value: String(d.id), label: d.name })),
    ],
    [departments],
  );
  const responsibleOptions = useMemo(
    () => [
      { value: '', label: '— не назначен —' },
      ...users.map((u) => ({ value: String(u.id), label: u.displayName || u.email })),
    ],
    [users],
  );
  const classificationOptions = useMemo(() => optionsFromLabels(DATA_CLASSIFICATION_LABELS), []);

  const save = async () => {
    setBusy(true);
    onError('');
    try {
      const updated = await updateDocumentMetadata(token, document.id, {
        title: title.trim() || document.fileName,
        documentType,
        departmentId: departmentId ? parseInt(departmentId, 10) : null,
        responsibleUserId: responsibleUserId ? parseInt(responsibleUserId, 10) : null,
        tags: tags.trim() || undefined,
        dataClassification,
      });
      onUpdated(updated);
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Не удалось сохранить карточку');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="office-card-panel">
      <button
        type="button"
        className="office-card-panel-toggle"
        onClick={() => setExpanded((e) => !e)}
        aria-expanded={expanded}
      >
        <h3>Карточка документа</h3>
        <span className="office-card-panel-chevron">{expanded ? '▲' : '▼'}</span>
      </button>
      {expanded && (
        <div className="office-card-panel-body">
          {readOnly && (
            <p className="office-card-readonly-hint">
              Редактирование недоступно: документ на согласовании, согласован или в архиве.
            </p>
          )}
          <div className="office-card-grid">
            <label className="parse-field">
              <span className="parse-field-label">Название</span>
              <input type="text" value={title} disabled={readOnly} onChange={(e) => setTitle(e.target.value)} />
            </label>
            <label className="parse-field">
              <span className="parse-field-label">Тип документа</span>
              <AppSelect
                value={documentType}
                disabled={readOnly}
                onChange={setDocumentType}
                options={documentTypeOptions}
              />
            </label>
            <label className="parse-field">
              <span className="parse-field-label">Подразделение</span>
              <AppSelect
                value={departmentId}
                disabled={readOnly}
                onChange={setDepartmentId}
                options={departmentOptions}
              />
            </label>
            <label className="parse-field">
              <span className="parse-field-label">Ответственный</span>
              <AppSelect
                value={responsibleUserId}
                disabled={readOnly}
                onChange={setResponsibleUserId}
                options={responsibleOptions}
              />
            </label>
            <label className="parse-field">
              <span className="parse-field-label">Классификация</span>
              <AppSelect
                value={dataClassification}
                disabled={readOnly}
                onChange={setDataClassification}
                options={classificationOptions}
              />
            </label>
            <label className="parse-field office-card-tags">
              <span className="parse-field-label">Теги (через запятую)</span>
              <input type="text" value={tags} disabled={readOnly} onChange={(e) => setTags(e.target.value)} />
            </label>
          </div>
          {!readOnly && (
            <button type="button" className="btn-primary office-sidebar-btn" disabled={busy} onClick={() => void save()}>
              {busy ? 'Сохранение…' : 'Сохранить карточку'}
            </button>
          )}
        </div>
      )}
    </div>
  );
}
