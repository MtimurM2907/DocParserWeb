/** Человекочитаемые подписи кодов действий аудита. */
export const AUDIT_ACTION_LABELS: Record<string, string> = {
  'document.parse': 'Загрузка / парсинг',
  'document.text_update': 'Изменение текста',
  'document.delete': 'Удаление документа',
  'document.export': 'Экспорт',
  'document.email_send': 'Отправка на email',
  'document.share': 'Предоставление доступа',
  'document.share_revoke': 'Отзыв доступа',
  'document.sign': 'Подписание',
  'office.metadata': 'Карточка документа',
  'workflow.submit': 'Отправка на согласование',
  'workflow.resubmit': 'Повторная отправка',
  'workflow.approve': 'Согласование',
  'workflow.reject': 'Возврат на доработку',
  'workflow.return_to_draft': 'Возврат в черновик',
  'workflow.archive': 'Архивирование',
  'admin.user.delete': 'Удаление пользователя',
  'enterprise.batch': 'Пакетная загрузка',
  'enterprise.entities': 'Извлечение сущностей',
};

export function auditActionLabel(action: string): string {
  return AUDIT_ACTION_LABELS[action] ?? action;
}

export type AuditSortField = 'createdAt' | 'user' | 'action' | 'resource';

export const AUDIT_SORT_LABELS: Record<AuditSortField, string> = {
  createdAt: 'Время',
  user: 'Пользователь',
  action: 'Действие',
  resource: 'Ресурс',
};
