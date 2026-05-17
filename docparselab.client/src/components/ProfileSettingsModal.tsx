import type { CurrentUser } from '../types/office';
import { ROLE_LABELS } from '../types/office';

type Props = {
  user: CurrentUser;
  isAdmin: boolean;
  onClose: () => void;
};

export function ProfileSettingsModal({ user, isAdmin, onClose }: Props) {
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-card"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-labelledby="profile-modal-title"
      >
        <h2 id="profile-modal-title">Мой профиль</h2>
        <p className="profile-modal-meta">
          Данные учётной записи. Изменить их может только администратор в разделе «Администрирование» →
          «Управление пользователями».
        </p>
        <dl className="profile-readonly">
          <div>
            <dt>ФИО (логин)</dt>
            <dd>{user.displayName?.trim() || '—'}</dd>
          </div>
          <div>
            <dt>Эл. почта</dt>
            <dd>{user.email}</dd>
          </div>
          <div>
            <dt>Роль</dt>
            <dd>{ROLE_LABELS[user.role] ?? user.role}</dd>
          </div>
          <div>
            <dt>Подразделение</dt>
            <dd>{user.departmentName ?? '—'}</dd>
          </div>
        </dl>
        {isAdmin && (
          <p className="registry-meta">
            Вы вошли как администратор — редактируйте пользователей в разделе «Администрирование».
          </p>
        )}
        <div className="modal-actions">
          <button type="button" className="btn-primary" onClick={onClose}>
            Закрыть
          </button>
        </div>
      </div>
    </div>
  );
}
