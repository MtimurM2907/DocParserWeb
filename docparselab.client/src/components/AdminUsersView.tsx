import { useCallback, useEffect, useMemo, useState } from 'react';
import type { Department, UserBrief } from '../types/office';
import { OFFICE_ROLES, ROLE_LABELS } from '../types/office';
import { createUserAccount } from '../api/backend';
import { deleteUser, fetchDepartments, fetchOfficeUsers, updateUser } from '../api/office';
import { AppSelect } from './AppSelect';

type UserDraft = {
  email: string;
  displayName: string;
  role: string;
  departmentId: string;
  password: string;
};

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
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [sortBy, setSortBy] = useState<'name' | 'email' | 'role' | 'department'>('name');
  const [drafts, setDrafts] = useState<Record<number, UserDraft>>({});
  const [newUser, setNewUser] = useState<UserDraft>({
    email: '',
    displayName: '',
    password: '',
    role: 'Employee',
    departmentId: '',
  });

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [u, d] = await Promise.all([fetchOfficeUsers(token), fetchDepartments(token)]);
      setUsers(u);
      setDepartments(d);
      const next: Record<number, UserDraft> = {};
      for (const user of u) {
        next[user.id] = {
          email: user.email,
          displayName: user.displayName ?? '',
          role: user.role,
          departmentId: user.departmentId != null ? String(user.departmentId) : '',
          password: '',
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

  const roleOptions = useMemo(
    () => OFFICE_ROLES.map((r) => ({ value: r, label: ROLE_LABELS[r] ?? r })),
    [],
  );

  const departmentOptions = useMemo(
    () => [
      { value: '', label: '— выберите —' },
      ...departments.map((d) => ({ value: String(d.id), label: d.name })),
    ],
    [departments],
  );

  const departmentOptionsShort = useMemo(
    () => [{ value: '', label: '—' }, ...departments.map((d) => ({ value: String(d.id), label: d.name }))],
    [departments],
  );

  const sortOptions = useMemo(
    () => [
      { value: 'name', label: 'По ФИО' },
      { value: 'email', label: 'По email' },
      { value: 'role', label: 'По роли' },
      { value: 'department', label: 'По подразделению' },
    ],
    [],
  );

  const deptNameById = useMemo(() => {
    const map = new Map<number, string>();
    for (const d of departments) map.set(d.id, d.name);
    return map;
  }, [departments]);

  const filteredUsers = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    let list = users;
    if (q) {
      list = users.filter((u) => {
        const draft = drafts[u.id];
        const name = (draft?.displayName ?? u.displayName ?? '').toLowerCase();
        const email = (draft?.email ?? u.email).toLowerCase();
        const role = (ROLE_LABELS[draft?.role ?? u.role] ?? draft?.role ?? u.role).toLowerCase();
        const dept =
          u.departmentId != null
            ? (deptNameById.get(u.departmentId) ?? '').toLowerCase()
            : '';
        return name.includes(q) || email.includes(q) || role.includes(q) || dept.includes(q);
      });
    }
    const sorted = [...list];
    sorted.sort((a, b) => {
      const da = drafts[a.id];
      const db = drafts[b.id];
      switch (sortBy) {
        case 'email':
          return (da?.email ?? a.email).localeCompare(db?.email ?? b.email, 'ru');
        case 'role':
          return (da?.role ?? a.role).localeCompare(db?.role ?? b.role, 'ru');
        case 'department': {
          const na = a.departmentId != null ? deptNameById.get(a.departmentId) ?? '' : '';
          const nb = b.departmentId != null ? deptNameById.get(b.departmentId) ?? '' : '';
          return na.localeCompare(nb, 'ru');
        }
        default:
          return (da?.displayName ?? a.displayName ?? '').localeCompare(
            db?.displayName ?? b.displayName ?? '',
            'ru',
          );
      }
    });
    return sorted;
  }, [users, drafts, searchQuery, sortBy, deptNameById]);

  const validateDraft = (draft: UserDraft, requirePassword: boolean) => {
    if (!draft.email.trim() || !draft.displayName.trim()) {
      return 'Укажите email и ФИО (логин).';
    }
    if (!draft.departmentId) {
      return 'Выберите подразделение.';
    }
    if (requirePassword && !draft.password) {
      return 'Укажите пароль.';
    }
    if (draft.password && draft.password.length < 6) {
      return 'Пароль должен быть не короче 6 символов.';
    }
    return null;
  };

  const saveUser = async (userId: number) => {
    const draft = drafts[userId];
    if (!draft) return;
    const validation = validateDraft(draft, false);
    if (validation) {
      setError(validation);
      return;
    }
    setSavingId(userId);
    setError(null);
    setSuccess(null);
    try {
      const updated = await updateUser(token, userId, {
        email: draft.email.trim(),
        displayName: draft.displayName.trim(),
        role: draft.role,
        departmentId: parseInt(draft.departmentId, 10),
        password: draft.password.trim() || undefined,
      });
      setUsers((prev) => prev.map((u) => (u.id === userId ? updated : u)));
      setDrafts((prev) => ({
        ...prev,
        [userId]: { ...draft, password: '' },
      }));
      setSuccess('Изменения сохранены.');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить');
    } finally {
      setSavingId(null);
    }
  };

  const createUser = async () => {
    const validation = validateDraft(newUser, true);
    if (validation) {
      setError(validation);
      return;
    }
    setCreating(true);
    setError(null);
    setSuccess(null);
    try {
      const created = await createUserAccount(token, {
        email: newUser.email.trim(),
        password: newUser.password,
        displayName: newUser.displayName.trim(),
        role: newUser.role,
        departmentId: parseInt(newUser.departmentId, 10),
      });
      setUsers((prev) => [...prev, created].sort((a, b) => a.email.localeCompare(b.email)));
      setDrafts((prev) => ({
        ...prev,
        [created.id]: {
          email: created.email,
          displayName: created.displayName ?? '',
          role: created.role,
          departmentId: created.departmentId != null ? String(created.departmentId) : '',
          password: '',
        },
      }));
      setNewUser({
        email: '',
        displayName: '',
        password: '',
        role: 'Employee',
        departmentId: '',
      });
      setSuccess(`Пользователь ${created.email} создан.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось создать пользователя');
    } finally {
      setCreating(false);
    }
  };

  const removeUser = async (userId: number, email: string) => {
    if (!window.confirm(`Удалить пользователя ${email}? Документы останутся в системе без владельца.`)) return;
    setDeletingId(userId);
    setError(null);
    setSuccess(null);
    try {
      await deleteUser(token, userId);
      setUsers((prev) => prev.filter((u) => u.id !== userId));
      setDrafts((prev) => {
        const next = { ...prev };
        delete next[userId];
        return next;
      });
      setSuccess(`Пользователь ${email} удалён.`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось удалить');
    } finally {
      setDeletingId(null);
    }
  };

  return (
    <section className="office-admin">
      <div className="office-admin-header">
        <div>
          <h2>Управление пользователями</h2>
          <p className="office-admin-subtitle">{users.length} учётных записей</p>
        </div>
        <button type="button" className="btn-secondary btn-sm" onClick={() => void load()} disabled={loading}>
          {loading ? '…' : 'Обновить'}
        </button>
      </div>

      {error && <div className="admin-alert admin-alert--error">{error}</div>}
      {success && <div className="admin-alert admin-alert--success">{success}</div>}

      <div className="admin-panel-block">
        <h3 className="admin-panel-block__title">Новый пользователь</h3>
        <div className="admin-form-grid admin-form-grid--create">
          <label className="admin-field">
            <span>ФИО (логин)</span>
            <input
              type="text"
              value={newUser.displayName}
              onChange={(e) => setNewUser((u) => ({ ...u, displayName: e.target.value }))}
              disabled={creating}
              placeholder="Иванов Иван Иванович"
            />
          </label>
          <label className="admin-field">
            <span>Эл. почта</span>
            <input
              type="email"
              value={newUser.email}
              onChange={(e) => setNewUser((u) => ({ ...u, email: e.target.value }))}
              disabled={creating}
              placeholder="user@company.ru"
            />
          </label>
          <label className="admin-field">
            <span>Пароль</span>
            <input
              type="password"
              value={newUser.password}
              onChange={(e) => setNewUser((u) => ({ ...u, password: e.target.value }))}
              disabled={creating}
              placeholder="не короче 6 символов"
              autoComplete="new-password"
            />
          </label>
          <label className="admin-field">
            <span>Подразделение</span>
            <AppSelect
              value={newUser.departmentId}
              onChange={(departmentId) => setNewUser((u) => ({ ...u, departmentId }))}
              options={departmentOptions}
              disabled={creating}
            />
          </label>
          <label className="admin-field">
            <span>Роль</span>
            <AppSelect
              value={newUser.role}
              onChange={(role) => setNewUser((u) => ({ ...u, role }))}
              options={roleOptions}
              disabled={creating}
            />
          </label>
        </div>
        <div className="admin-panel-block__actions">
          <button type="button" className="btn-primary" disabled={creating} onClick={() => void createUser()}>
            {creating ? 'Создание…' : 'Создать пользователя'}
          </button>
        </div>
      </div>

      <div className="admin-panel-block admin-panel-block--list">
        <div className="admin-list-toolbar">
          <h3 className="admin-panel-block__title">Список пользователей</h3>
          <div className="admin-list-toolbar__controls">
            <input
              type="search"
              className="admin-list-toolbar__search"
              placeholder="Поиск по ФИО, email, роли, подразделению…"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
            <label className="admin-list-toolbar__sort">
              <span>Сортировка</span>
              <AppSelect value={sortBy} onChange={(v) => setSortBy(v as typeof sortBy)} options={sortOptions} />
            </label>
          </div>
        </div>
        {users.length === 0 && !loading ? (
          <p className="registry-meta">Пользователей нет.</p>
        ) : filteredUsers.length === 0 ? (
          <p className="registry-meta">Ничего не найдено.</p>
        ) : (
          <ul className="admin-user-list">
            {filteredUsers.map((u) => {
              const draft = drafts[u.id];
              if (!draft) return null;
              return (
                <li key={u.id} className="admin-user-card">
                  <div className="admin-user-card__badge">{ROLE_LABELS[draft.role] ?? draft.role}</div>
                  <div className="admin-form-grid admin-form-grid--user">
                    <label className="admin-field">
                      <span>ФИО (логин)</span>
                      <input
                        type="text"
                        value={draft.displayName}
                        onChange={(e) =>
                          setDrafts((prev) => ({
                            ...prev,
                            [u.id]: { ...draft, displayName: e.target.value },
                          }))
                        }
                      />
                    </label>
                    <label className="admin-field">
                      <span>Эл. почта</span>
                      <input
                        type="email"
                        value={draft.email}
                        onChange={(e) =>
                          setDrafts((prev) => ({
                            ...prev,
                            [u.id]: { ...draft, email: e.target.value },
                          }))
                        }
                      />
                    </label>
                    <label className="admin-field">
                      <span>Подразделение</span>
                      <AppSelect
                        value={draft.departmentId}
                        onChange={(departmentId) =>
                          setDrafts((prev) => ({
                            ...prev,
                            [u.id]: { ...draft, departmentId },
                          }))
                        }
                        options={departmentOptionsShort}
                        placeholder="—"
                      />
                    </label>
                    <label className="admin-field">
                      <span>Роль</span>
                      <AppSelect
                        value={draft.role}
                        onChange={(role) =>
                          setDrafts((prev) => ({
                            ...prev,
                            [u.id]: { ...draft, role },
                          }))
                        }
                        options={roleOptions}
                      />
                    </label>
                    <label className="admin-field admin-field--password">
                      <span>Новый пароль</span>
                      <input
                        type="password"
                        value={draft.password}
                        placeholder="оставьте пустым, чтобы не менять"
                        autoComplete="new-password"
                        onChange={(e) =>
                          setDrafts((prev) => ({
                            ...prev,
                            [u.id]: { ...draft, password: e.target.value },
                          }))
                        }
                      />
                    </label>
                  </div>
                  <div className="admin-user-card__actions">
                    <button
                      type="button"
                      className="btn-primary admin-user-card__save"
                      disabled={savingId === u.id || deletingId === u.id}
                      onClick={() => void saveUser(u.id)}
                    >
                      {savingId === u.id ? 'Сохранение…' : 'Сохранить'}
                    </button>
                    <button
                      type="button"
                      className="btn-danger btn-sm"
                      disabled={savingId === u.id || deletingId === u.id}
                      onClick={() => void removeUser(u.id, draft.email)}
                    >
                      {deletingId === u.id ? '…' : 'Удалить'}
                    </button>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </section>
  );
}
