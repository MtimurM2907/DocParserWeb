export type MainView = 'workspace' | 'registry' | 'tasks' | 'admin';

export interface Department {
  id: number;
  name: string;
}

export interface UserBrief {
  id: number;
  email: string;
  displayName?: string | null;
  role: string;
  departmentId?: number | null;
  departmentName?: string | null;
}

export interface CurrentUser {
  id: number;
  email: string;
  displayName?: string | null;
  role: string;
  departmentId?: number | null;
  departmentName?: string | null;
}

export interface DocumentRegistryItem {
  id: number;
  fileName: string;
  title: string;
  documentType: string;
  workflowStatus: string;
  dataClassification: string;
  uploadedAt: string;
  ownerId?: number | null;
  ownerEmail?: string | null;
  departmentId?: number | null;
  departmentName?: string | null;
  responsibleUserId?: number | null;
  responsibleUserEmail?: string | null;
  currentApproverUserId?: number | null;
  currentApproverEmail?: string | null;
  tags?: string | null;
  aiSummaryPreview?: string | null;
}

export interface DocumentRegistryPage {
  items: DocumentRegistryItem[];
  total: number;
}

export interface ApprovalTask {
  documentId: number;
  title: string;
  fileName: string;
  workflowStatus: string;
  submittedAt?: string | null;
  ownerEmail?: string | null;
  workflowComment?: string | null;
}

export interface DocumentVersionBrief {
  id: number;
  versionNumber: number;
  changeType: string;
  createdAt: string;
  createdByUserId?: number | null;
  createdByEmail?: string | null;
  textLength: number;
}

export interface DocumentVersionDetail extends DocumentVersionBrief {
  text: string;
}

export interface WorkflowHistoryItem {
  id: number;
  action: string;
  comment?: string | null;
  createdAt: string;
  userEmail?: string | null;
}

export interface DocumentSignature {
  id: number;
  documentId: number;
  textHashSha256: string;
  signedAt: string;
  signerEmail: string;
  signerDisplayName?: string | null;
  signerRole: string;
  comment?: string | null;
  signatureKind: string;
}

export interface SignatureVerification {
  hasSignatures: boolean;
  textMatchesLastSignature: boolean;
  currentTextHashSha256: string;
  lastSignatureHashSha256?: string | null;
  lastSignedAt?: string | null;
  lastSignerEmail?: string | null;
}

export const WORKFLOW_STATUS_LABELS: Record<string, string> = {
  Draft: 'Черновик',
  OnApproval: 'На согласовании',
  Approved: 'Согласован',
  Signed: 'Подписан',
  Rejected: 'На доработке',
  Archived: 'В архиве',
};

export const DOCUMENT_TYPE_LABELS: Record<string, string> = {
  general: 'Общий',
  letter: 'Письмо',
  memo: 'Служебная записка',
  order: 'Приказ',
  contract: 'Договор',
  instruction: 'Инструкция',
};

export const DATA_CLASSIFICATION_LABELS: Record<string, string> = {
  Internal: 'Внутренний',
  Public: 'Публичный',
  Confidential: 'Конфиденциальный',
};

export const OFFICE_ROLES = ['Admin', 'Manager', 'Employee', 'Viewer'] as const;

export const ROLE_LABELS: Record<string, string> = {
  Admin: 'Администратор',
  Manager: 'Руководитель',
  Employee: 'Сотрудник',
  Viewer: 'Только просмотр',
};
