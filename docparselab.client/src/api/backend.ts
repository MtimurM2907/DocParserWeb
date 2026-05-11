import type {
  AuthResponse,
  ParsedDocument,
  SpellcheckResponse,
} from '../types/api';
import { authHeaders, readResponseError } from '../lib/http';

const jsonContent = { 'Content-Type': 'application/json' };

export async function parseDocument(file: File, token: string | null): Promise<ParsedDocument> {
  const formData = new FormData();
  formData.append('file', file);
  const response = await fetch('/api/pdf/parse', {
    method: 'POST',
    body: formData,
    headers: authHeaders(token),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка при загрузке файла'));
  }
  return response.json() as Promise<ParsedDocument>;
}

export async function authLoginOrRegister(
  mode: 'login' | 'register',
  email: string,
  password: string,
): Promise<AuthResponse> {
  const response = await fetch(`/api/auth/${mode}`, {
    method: 'POST',
    headers: jsonContent,
    body: JSON.stringify({ email, password }),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка авторизации'));
  }
  return response.json() as Promise<AuthResponse>;
}

export async function listMyDocuments(token: string): Promise<ParsedDocument[]> {
  const response = await fetch('/api/pdf/my', { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка загрузки документов'));
  }
  return response.json() as Promise<ParsedDocument[]>;
}

export async function shareDocument(token: string, documentId: number, targetEmail: string): Promise<void> {
  const response = await fetch('/api/pdf/share', {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ documentId, targetEmail }),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка при отправке документа'));
  }
}

export async function sendDocumentByEmail(
  token: string,
  documentId: number,
  targetEmail: string,
  format: 'docx' | 'pdf',
): Promise<void> {
  const response = await fetch(`/api/pdf/${documentId}/send-email`, {
    method: 'POST',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ targetEmail, format }),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось отправить документ на email'));
  }
}

export async function getDocument(token: string, docId: number): Promise<ParsedDocument> {
  const response = await fetch(`/api/pdf/${docId}`, { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось открыть документ'));
  }
  return response.json() as Promise<ParsedDocument>;
}

export async function saveDocumentText(token: string, docId: number, text: string): Promise<ParsedDocument> {
  const response = await fetch(`/api/pdf/${docId}/text`, {
    method: 'PUT',
    headers: { ...jsonContent, ...authHeaders(token) },
    body: JSON.stringify({ text }),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось сохранить изменения'));
  }
  return response.json() as Promise<ParsedDocument>;
}

export async function runSpellcheck(text: string): Promise<SpellcheckResponse> {
  const response = await fetch('/api/spellcheck/check', {
    method: 'POST',
    headers: jsonContent,
    body: JSON.stringify({
      text,
      language: 'ru_RU',
      maxSuggestions: 5,
      maxMistakes: 200,
    }),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка проверки орфографии'));
  }
  return response.json() as Promise<SpellcheckResponse>;
}

export async function deleteDocument(token: string, docId: number): Promise<void> {
  const response = await fetch(`/api/pdf/${docId}`, {
    method: 'DELETE',
    headers: authHeaders(token),
  });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Не удалось удалить документ'));
  }
}

export async function exportDocument(token: string, docId: number, fileName: string, format: 'docx' | 'pdf'): Promise<void> {
  const response = await fetch(`/api/pdf/${docId}/export?format=${format}`, { headers: authHeaders(token) });
  if (!response.ok) {
    throw new Error(await readResponseError(response, 'Ошибка экспорта'));
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `${fileName}.${format}`;
  a.click();
  URL.revokeObjectURL(url);
}

