import { useEffect, useMemo, useState } from 'react';
import type { UserBrief } from '../types/office';
import { ROLE_LABELS } from '../types/office';
import { fetchApprovalCandidates } from '../api/office';

type Props = {
  token: string;
  selectedIds: number[];
  departmentName?: string | null;
  onConfirm: (ids: number[]) => void;
  onClose: () => void;
};

function userLabel(u: UserBrief): string {
  return u.displayName?.trim() || u.email;
}

export function ApproverPickerModal({ token, selectedIds, departmentName, onConfirm, onClose }: Props) {
  const [candidates, setCandidates] = useState<UserBrief[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [draftIds, setDraftIds] = useState<number[]>(selectedIds);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setLoadError(null);
    void fetchApprovalCandidates(token)
      .then((list) => {
        if (!cancelled) setCandidates(list);
      })
      .catch((e) => {
        if (!cancelled) {
          setCandidates([]);
          setLoadError(e instanceof Error ? e.message : 'Не удалось загрузить коллег');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [token]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return candidates;
    return candidates.filter((u) => {
      const hay = `${u.displayName ?? ''} ${u.email} ${u.departmentName ?? ''} ${u.role}`.toLowerCase();
      return hay.includes(q);
    });
  }, [candidates, search]);

  const selectedUsers = useMemo(
    () =>
      draftIds
        .map((id) => candidates.find((c) => c.id === id))
        .filter((u): u is UserBrief => u != null),
    [draftIds, candidates],
  );

  const toggle = (id: number) => {
    setDraftIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  };

  const move = (id: number, direction: -1 | 1) => {
    setDraftIds((prev) => {
      const idx = prev.indexOf(id);
      if (idx < 0) return prev;
      const next = idx + direction;
      if (next < 0 || next >= prev.length) return prev;
      const copy = [...prev];
      [copy[idx], copy[next]] = [copy[next]!, copy[idx]!];
      return copy;
    });
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-card approver-picker-modal"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-labelledby="approver-picker-title"
      >
        <h2 id="approver-picker-title">Выбор согласующих</h2>
        <p className="approver-picker-modal__hint">
          {departmentName
            ? `Показаны коллеги из подразделения «${departmentName}» (сотрудники и руководители). Вы не можете назначить себя, администратора или наблюдателя.`
            : 'Укажите подразделение в профиле (через администратора), чтобы выбрать коллег для согласования.'}
        </p>

        {selectedUsers.length > 0 && (
          <div className="approver-picker-modal__selected">
            <span className="parse-field-label">Порядок этапов</span>
            <ol className="approver-picker-modal__order">
              {selectedUsers.map((u, i) => (
                <li key={u.id}>
                  <span className="approver-picker-modal__order-num">{i + 1}.</span>
                  <span className="approver-picker-modal__order-name">{userLabel(u)}</span>
                  <span className="approver-picker-modal__order-meta">
                    {ROLE_LABELS[u.role] ?? u.role}
                  </span>
                  <span className="approver-picker-modal__order-actions">
                    <button
                      type="button"
                      className="btn-ghost btn-sm"
                      disabled={i === 0}
                      onClick={() => move(u.id, -1)}
                      aria-label="Выше"
                    >
                      ↑
                    </button>
                    <button
                      type="button"
                      className="btn-ghost btn-sm"
                      disabled={i === selectedUsers.length - 1}
                      onClick={() => move(u.id, 1)}
                      aria-label="Ниже"
                    >
                      ↓
                    </button>
                    <button
                      type="button"
                      className="btn-ghost btn-sm"
                      onClick={() => toggle(u.id)}
                      aria-label="Убрать"
                    >
                      ×
                    </button>
                  </span>
                </li>
              ))}
            </ol>
          </div>
        )}

        <label className="parse-field approver-picker-modal__search">
          <span className="parse-field-label">Поиск по ФИО или email</span>
          <input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Начните вводить имя…"
            autoFocus
          />
        </label>

        {loadError && <div className="admin-alert admin-alert--error">{loadError}</div>}

        <div className="approver-picker-modal__list" role="listbox" aria-multiselectable="true">
          {loading && <p className="registry-meta">Загрузка…</p>}
          {!loading && !loadError && filtered.length === 0 && (
            <p className="registry-meta">
              {candidates.length === 0
                ? 'В вашем подразделении нет других сотрудников для согласования. Попросите администратора добавить коллег или назначить вас в другое подразделение.'
                : 'Никого не найдено по запросу.'}
            </p>
          )}
          {filtered.map((u) => {
            const checked = draftIds.includes(u.id);
            return (
              <button
                key={u.id}
                type="button"
                role="option"
                aria-selected={checked}
                className={`approver-picker-modal__row${checked ? ' is-selected' : ''}`}
                onClick={() => toggle(u.id)}
              >
                <span className="approver-picker-modal__check" aria-hidden>
                  {checked ? '☑' : '☐'}
                </span>
                <span className="approver-picker-modal__main">
                  <strong>{userLabel(u)}</strong>
                  <span className="approver-picker-modal__email">{u.email}</span>
                </span>
                <span className="approver-picker-modal__badges">
                  <span className="approver-picker-modal__dept">{u.departmentName ?? '—'}</span>
                  <span className="approver-picker-modal__role">{ROLE_LABELS[u.role] ?? u.role}</span>
                </span>
              </button>
            );
          })}
        </div>

        <div className="modal-actions">
          <button type="button" className="btn-secondary" onClick={onClose}>
            Отмена
          </button>
          <button
            type="button"
            className="btn-primary"
            onClick={() => {
              onConfirm(draftIds);
              onClose();
            }}
          >
            Готово ({draftIds.length})
          </button>
        </div>
      </div>
    </div>
  );
}
