import { useEffect, useState } from 'react';
import type { CurrentUser, Department } from '../types/office';
import { ROLE_LABELS } from '../types/office';
import { fetchDepartments, updateProfile } from '../api/office';

type Props = {
  token: string;
  user: CurrentUser;
  onClose: () => void;
  onSaved: (user: CurrentUser) => void;
};

export function ProfileSettingsModal({ token, user, onClose, onSaved }: Props) {
  const [displayName, setDisplayName] = useState(user.displayName ?? '');
  const [departmentId, setDepartmentId] = useState(user.departmentId != null ? String(user.departmentId) : '');
  const [departments, setDepartments] = useState<Department[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void fetchDepartments(token).then(setDepartments).catch(() => setDepartments([]));
  }, [token]);

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const updated = await updateProfile(token, {
        displayName: displayName.trim() || undefined,
        departmentId: departmentId ? parseInt(departmentId, 10) : null,
      });
      onSaved(updated);
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить профиль');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-card"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-labelledby="profile-modal-title"
      >
        <h2 id="profile-modal-title">Профиль</h2>
        <p className="profile-modal-meta">
          {user.email} · {ROLE_LABELS[user.role] ?? user.role}
        </p>
        {error && <p className="office-card-error">{error}</p>}
        <label className="modal-label" htmlFor="profile-display-name">
          Отображаемое имя
        </label>
        <input
          id="profile-display-name"
          type="text"
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
        />
        <label className="modal-label" htmlFor="profile-department">
          Подразделение
        </label>
        <select id="profile-department" value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
          <option value="">— не указано —</option>
          {departments.map((d) => (
            <option key={d.id} value={d.id}>
              {d.name}
            </option>
          ))}
        </select>
        <div className="modal-actions">
          <button type="button" onClick={onClose}>
            Отмена
          </button>
          <button type="button" className="btn-primary" disabled={busy} onClick={() => void save()}>
            {busy ? 'Сохранение…' : 'Сохранить'}
          </button>
        </div>
      </div>
    </div>
  );
}
