import { useCallback, useEffect, useState } from 'react';
import type { Department, UserBrief } from '../types/office';
import { OFFICE_ROLES, ROLE_LABELS } from '../types/office';
import { createUserAccount } from '../api/backend';
import { fetchDepartments, fetchOfficeUsers, setUserRole } from '../api/office';

type Props = {
  token: string;
};

export function AdminUsersView({ token }: Props) {
  const [users, setUsers] = useState<UserBrief[]>([]);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [savingId, setSavingId] = useState<number | null>(null);
  const [creating, setCreating] = useState(false);
  const [drafts, setDrafts] = useState<Record<number, { role: string; departmentId: string }>>({});
  const [newEmail, setNewEmail] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newRole, setNewRole] = useState('Employee');
  const [newDepartmentId, setNewDepartmentId] = useState('');
  const [newDisplayName, setNewDisplayName] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [u, d] = await Promise.all([fetchOfficeUsers(token), fetchDepartments(token)]);
      setUsers(u);
      setDepartments(d);
      const next: Record<number, { role: string; departmentId: string }> = {};
      for (const user of u) {
        next[user.id] = {
          role: user.role,
          departmentId: user.departmentId != null ? String(user.departmentId) : '',
        };
      }
      setDrafts(next);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить пользователей');
      setUsers([]);
    } finally {
      setLoading(false);
    }
  }, [token]);

  useEffect(() => {
    void load();
  }, [load]);

  const saveUser = async (userId: number) => {
    const draft = drafts[userId];
    if (!draft) return;
    setSavingId(userId);
    setError(null);
    setSuccess(null);
    try {
      const updated = await setUserRole(token, userId, {
        role: draft.role,
        departmentId: draft.departmentId ? parseInt(draft.departmentId, 10) : null,
      });
      setUsers((prev) => prev.map((u) => (u.id === userId ? updated : u)));
      setSuccess('Изменения сохранены.');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить');
    } finally {
      setSavingId(null);
    }
  };

  const createUser = async () => {
    if (!newEmail.trim() || !newPassword) {
      setError('Укажите email и пароль нового пользователя.');
      return;
    }
    if (newPassword.length < 6) {
      setError('Пароль должен быть не короче 6 символов.');
      return;
    }
    setCreating(true);
    setError(null);
    setSuccess(null);
    try {
      const created = await createUserAccount(token, {
        email: newEmail.trim(),
        password: newPassword,
        role: newRole,
        departmentId: newDepartmentId ? parseInt(newDepartmentId, 10) : null,
        displayName: newDisplayName.trim() || null,
      });
      setUsers((prev) => [...prev, created].sort((a, b) => a.email.localeCompare(b.email)));
      setDrafts((prev) => ({
        ...prev,
        [created.id]: {
          role: created.role,
          departmentId: created.departmentId != null ? String(created.departmentId) : '',
        },
      }));
      setNewEmail('');
      setNewPassword('');
      setNewRole('Employee');
      setNewDepartmentId('');
      setNewDisplayName('');
      setSuccess(`Пользователь ${created.email} создан.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось создать пользователя');
    } finally {
      setCreating(false);
    }
  };

  return (
    <section className="office-admin">
      <div className="office-admin-header">
        <h2>Управление пользователями</h2>
        <button type="button" className="btn-secondary" onClick={() => void load()} disabled={loading}>
          {loading ? '…' : 'Обновить'}
        </button>
      </div>
      {error && <p className="office-card-error">{error}</p>}
      {success && <p className="share-success">{success}</p>}

      <div className="admin-create-user">
        <h3>Новый пользователь</h3>
        <div className="admin-create-user-grid">
          <label className="parse-field">
            <span className="parse-field-label">Email</span>
            <input
              type="email"
              value={newEmail}
              onChange={(e) => setNewEmail(e.target.value)}
              disabled={creating}
              placeholder="user@company.ru"
            />
          </label>
          <label className="parse-field">
            <span className="parse-field-label">Пароль</span>
            <input
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              disabled={creating}
              autoComplete="new-password"
            />
          </label>
          <label className="parse-field">
            <span className="parse-field-label">Имя (необязательно)</span>
            <input
              type="text"
              value={newDisplayName}
              onChange={(e) => setNewDisplayName(e.target.value)}
              disabled={creating}
            />
          </label>
          <label className="parse-field">
            <span className="parse-field-label">Роль</span>
            <select value={newRole} onChange={(e) => setNewRole(e.target.value)} disabled={creating}>
              {OFFICE_ROLES.map((r) => (
                <option key={r} value={r}>
                  {ROLE_LABELS[r] ?? r}
                </option>
              ))}
            </select>
          </label>
          <label className="parse-field">
            <span className="parse-field-label">Подразделение</span>
            <select
              value={newDepartmentId}
              onChange={(e) => setNewDepartmentId(e.target.value)}
              disabled={creating}
            >
              <option value="">—</option>
              {departments.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </select>
          </label>
        </div>
        <button type="button" className="btn-primary" disabled={creating} onClick={() => void createUser()}>
          {creating ? 'Создание…' : 'Создать пользователя'}
        </button>
      </div>

      <div className="registry-table-wrap">
        <table className="registry-table admin-users-table">
          <thead>
            <tr>
              <th>Email</th>
              <th>Имя</th>
              <th>Роль</th>
              <th>Подразделение</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {users.map((u) => {
              const draft = drafts[u.id] ?? { role: u.role, departmentId: '' };
              return (
                <tr key={u.id}>
                  <td>{u.email}</td>
                  <td>{u.displayName ?? '—'}</td>
                  <td>
                    <select
                      value={draft.role}
                      onChange={(e) =>
                        setDrafts((prev) => ({
                          ...prev,
                          [u.id]: { ...draft, role: e.target.value },
                        }))
                      }
                    >
                      {OFFICE_ROLES.map((r) => (
                        <option key={r} value={r}>
                          {ROLE_LABELS[r] ?? r}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <select
                      value={draft.departmentId}
                      onChange={(e) =>
                        setDrafts((prev) => ({
                          ...prev,
                          [u.id]: { ...draft, departmentId: e.target.value },
                        }))
                      }
                    >
                      <option value="">—</option>
                      {departments.map((d) => (
                        <option key={d.id} value={d.id}>
                          {d.name}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <button
                      type="button"
                      className="btn-primary"
                      disabled={savingId === u.id}
                      onClick={() => void saveUser(u.id)}
                    >
                      {savingId === u.id ? '…' : 'Сохранить'}
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}
