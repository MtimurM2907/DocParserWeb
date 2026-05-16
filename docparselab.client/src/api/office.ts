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
  approverUserId: number,
  comment?: string,
): Promise<ParsedDocument> {
  const r = await fetch(`/api/office/documents/${docId}/submit`, {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ approverUserId, comment }),
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

export async function updateProfile(
  token: string,
  body: { displayName?: string; departmentId?: number | null },
): Promise<CurrentUser> {
  const r = await fetch('/api/office/profile', {
    method: 'PATCH',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify(body),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось обновить профиль'));
  return r.json() as Promise<CurrentUser>;
}

export async function setUserRole(
  token: string,
  userId: number,
  body: { role: string; departmentId?: number | null },
): Promise<UserBrief> {
  const r = await fetch(`/api/office/users/${userId}/role`, {
    method: 'PUT',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify(body),
  });
  if (!r.ok) throw new Error(await readResponseError(r, 'Не удалось изменить роль'));
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
