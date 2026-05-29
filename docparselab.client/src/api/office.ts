import type {
  ApprovalTask,
  CurrentUser,
  Department,
  DocumentRegistryPage,
  DocumentSignature,
  DocumentVersionBrief,
  DocumentVersionDetail,
  SignatureVerification,
  UserBrief,
  WorkflowHistoryItem,
} from '../types/office';
import type { ParsedDocument } from '../types/api';
import { authHeaders, readResponseError } from '../lib/http';

const jsonContent = { 'Content-Type': 'application/json' };

export async function fetchCurrentUser(token: string): Promise<CurrentUser> {
  const r = await fetch('/api/auth/me', { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить профиль'));
  return r.json() as Promise<CurrentUser>;
}

export async function fetchDepartments(token: string): Promise<Department[]> {
  const r = await fetch('/api/office/departments', { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить подразделения'));
  return r.json() as Promise<Department[]>;
}

export async function fetchOfficeUsers(token: string): Promise<UserBrief[]> {
  const r = await fetch('/api/office/users', { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить пользователей'));
  return r.json() as Promise<UserBrief[]>;
}

/** Сотрудники и руководители того же подразделения (для маршрута согласования). */
export async function fetchApprovalCandidates(token: string): Promise<UserBrief[]> {
  const r = await fetch('/api/office/approval-candidates', { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить список согласующих'));
  return r.json() as Promise<UserBrief[]>;
}

export async function fetchRegistry(
  token: string,
  params: {
    status?: string;
    documentType?: string;
    departmentId?: number;
    search?: string;
    mineOnly?: boolean;
    skip?: number;
    take?: number;
  },
): Promise<DocumentRegistryPage> {
  const q = new URLSearchParams();
  if (params.status) q.set('status', params.status);
  if (params.documentType) q.set('documentType', params.documentType);
  if (params.departmentId != null) q.set('departmentId', String(params.departmentId));
  if (params.search) q.set('search', params.search);
  if (params.mineOnly) q.set('mineOnly', 'true');
  q.set('skip', String(params.skip ?? 0));
  q.set('take', String(params.take ?? 50));
  const r = await fetch(`/api/office/registry?${q}`, { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить реестр'));
  return r.json() as Promise<DocumentRegistryPage>;
}

export async function fetchMyTasks(token: string): Promise<ApprovalTask[]> {
  const r = await fetch('/api/office/my-tasks', { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить задачи'));
  return r.json() as Promise<ApprovalTask[]>;
}

export async function updateDocumentMetadata(
  token: string,
  docId: number,
  body: {
    title?: string;
    documentType?: string;
    departmentId?: number | null;
    responsibleUserId?: number | null;
    tags?: string;
    dataClassification?: string;
  },
): Promise<ParsedDocument> {
  const r = await fetch(`/api/office/documents/${docId}/metadata`, {
    method: 'PATCH',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify(body),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось обновить карточку'));
  return r.json() as Promise<ParsedDocument>;
}

export async function submitForApproval(
  token: string,
  docId: number,
  approverUserIds: number[],
  comment?: string,
  approvalDueAt?: string,
): Promise<ParsedDocument> {
  const r = await fetch(`/api/office/documents/${docId}/submit`, {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({
      approverUserId: approverUserIds[0] ?? 0,
      approverUserIds,
      comment,
      approvalDueAt,
    }),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось отправить на согласование'));
  return r.json() as Promise<ParsedDocument>;
}

/** Повторная отправка после доработки — тот же маршрут согласующих, без смены цепочки. */
export async function resubmitForApproval(
  token: string,
  docId: number,
  comment?: string,
): Promise<ParsedDocument> {
  const r = await fetch(`/api/office/documents/${docId}/submit`, {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ approverUserIds: [], comment }),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось отправить на согласование'));
  return r.json() as Promise<ParsedDocument>;
}

export async function approveDocument(
  token: string,
  docId: number,
  comment?: string,
): Promise<ParsedDocument> {
  const r = await fetch(`/api/office/documents/${docId}/approve`, {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ comment }),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось согласовать'));
  return r.json() as Promise<ParsedDocument>;
}

export async function rejectDocument(
  token: string,
  docId: number,
  comment: string,
): Promise<ParsedDocument> {
  const r = await fetch(`/api/office/documents/${docId}/reject`, {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ comment }),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось вернуть документ'));
  return r.json() as Promise<ParsedDocument>;
}

export async function returnDocumentToDraft(token: string, docId: number): Promise<ParsedDocument> {
  const r = await fetch(`/api/office/documents/${docId}/return-to-draft`, {
    method: 'POST',
    headers: authHeaders(token),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось вернуть в черновик'));
  return r.json() as Promise<ParsedDocument>;
}

export async function archiveDocument(token: string, docId: number): Promise<ParsedDocument> {
  const r = await fetch(`/api/office/documents/${docId}/archive`, {
    method: 'POST',
    headers: authHeaders(token),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось отправить в архив'));
  return r.json() as Promise<ParsedDocument>;
}

export async function fetchDocumentVersions(token: string, docId: number): Promise<DocumentVersionBrief[]> {
  const r = await fetch(`/api/office/documents/${docId}/versions`, { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить версии'));
  return r.json() as Promise<DocumentVersionBrief[]>;
}

export async function fetchDocumentVersionDetail(
  token: string,
  docId: number,
  versionId: number,
): Promise<DocumentVersionDetail> {
  const r = await fetch(`/api/office/documents/${docId}/versions/${versionId}`, { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить версию'));
  return r.json() as Promise<DocumentVersionDetail>;
}

export async function deleteUser(token: string, userId: number) {
  const r = await fetch(`/api/office/users/${userId}`, { method: 'DELETE', headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось удалить пользователя'));
}

export async function updateUser(
  token: string,
  userId: number,
  body: {
    email: string;
    displayName: string;
    role: string;
    departmentId: number;
    password?: string;
  },
): Promise<UserBrief> {
  const r = await fetch(`/api/office/users/${userId}`, {
    method: 'PUT',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify(body),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось обновить пользователя'));
  return r.json() as Promise<UserBrief>;
}

export async function fetchWorkflowHistory(token: string, docId: number): Promise<WorkflowHistoryItem[]> {
  const r = await fetch(`/api/office/documents/${docId}/workflow-history`, { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить историю'));
  return r.json() as Promise<WorkflowHistoryItem[]>;
}

export async function signDocument(
  token: string,
  docId: number,
  comment?: string,
): Promise<ParsedDocument> {
  const r = await fetch(`/api/office/documents/${docId}/sign`, {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ comment }),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось подписать документ'));
  return r.json() as Promise<ParsedDocument>;
}

export async function fetchDocumentSignatures(token: string, docId: number): Promise<DocumentSignature[]> {
  const r = await fetch(`/api/office/documents/${docId}/signatures`, { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить подписи'));
  return r.json() as Promise<DocumentSignature[]>;
}

export async function verifyDocumentSignature(token: string, docId: number): Promise<SignatureVerification> {
  const r = await fetch(`/api/office/documents/${docId}/signatures/verify`, { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось проверить подпись'));
  return r.json() as Promise<SignatureVerification>;
}

export async function fetchDocumentSigningPayload(
  token: string,
  docId: number,
): Promise<import('../types/office').DocumentSigningPayload> {
  const r = await fetch(`/api/office/documents/${docId}/signing-payload`, { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось получить данные для подписи'));
  return r.json() as Promise<import('../types/office').DocumentSigningPayload>;
}

export async function fetchNotifications(token: string, unreadOnly = false) {
  const q = unreadOnly ? '?unreadOnly=true' : '';
  const r = await fetch(`/api/office/notifications${q}`, { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить уведомления'));
  return r.json() as Promise<import('../types/office').UserNotification[]>;
}

export async function markNotificationRead(token: string, id: number) {
  const r = await fetch(`/api/office/notifications/${id}/read`, { method: 'POST', headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Ошибка'));
}

export async function fetchDocumentComments(token: string, docId: number) {
  const r = await fetch(`/api/office/documents/${docId}/comments`, { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить комментарии'));
  return r.json() as Promise<import('../types/office').DocumentComment[]>;
}

export async function addDocumentComment(token: string, docId: number, text: string) {
  const r = await fetch(`/api/office/documents/${docId}/comments`, {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ text }),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось добавить комментарий'));
  return r.json() as Promise<import('../types/office').DocumentComment>;
}

export async function fetchApprovalSteps(token: string, docId: number) {
  const r = await fetch(`/api/office/documents/${docId}/approval-steps`, { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось загрузить этапы'));
  return r.json() as Promise<import('../types/office').ApprovalStep[]>;
}

export async function fetchVersionDiff(token: string, docId: number, fromVersionId: number, toVersionId: number) {
  const r = await fetch(
    `/api/office/documents/${docId}/versions/diff?fromVersionId=${fromVersionId}&toVersionId=${toVersionId}`,
    { headers: authHeaders(token) },
  );
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось построить diff'));
  return r.json() as Promise<import('../types/office').VersionDiff>;
}

export async function createDepartment(token: string, name: string) {
  const r = await fetch('/api/office/departments', {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ name }),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось создать подразделение'));
  return r.json() as Promise<Department>;
}

export async function updateDepartment(token: string, id: number, name: string) {
  const r = await fetch(`/api/office/departments/${id}`, {
    method: 'PATCH',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ name }),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось обновить подразделение'));
  return r.json() as Promise<Department>;
}

export async function deleteDepartment(token: string, id: number) {
  const r = await fetch(`/api/office/departments/${id}`, { method: 'DELETE', headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось удалить подразделение'));
}

export async function fetchEditLock(token: string, docId: number) {
  const r = await fetch(`/api/office/documents/${docId}/edit-lock`, { headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось получить блокировку'));
  return r.json() as Promise<import('../types/office').DocumentEditLockStatus>;
}

export async function acquireEditLock(token: string, docId: number) {
  const r = await fetch(`/api/office/documents/${docId}/edit-lock`, { method: 'POST', headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Документ занят другим пользователем'));
  return r.json() as Promise<import('../types/office').DocumentEditLockStatus>;
}

export async function releaseEditLock(token: string, docId: number) {
  const r = await fetch(`/api/office/documents/${docId}/edit-lock`, { method: 'DELETE', headers: authHeaders(token) });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось снять блокировку'));
}

export async function signExternalDocument(
  token: string,
  docId: number,
  form: FormData,
): Promise<ParsedDocument> {
  const r = await fetch(`/api/office/documents/${docId}/sign/external`, {
    method: 'POST',
    headers: authHeaders(token),
    body: form,
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось зарегистрировать внешнюю подпись'));
  return r.json() as Promise<ParsedDocument>;
}

export async function bulkArchiveDocuments(token: string, documentIds: number[]) {
  const r = await fetch('/api/office/documents/bulk-archive', {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ documentIds }),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось архивировать'));
  return r.json() as Promise<{ archivedCount: number }>;
}
