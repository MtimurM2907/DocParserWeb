import { useMemo, useState } from 'react';
import type { UserBrief } from '../types/office';
import { ROLE_LABELS } from '../types/office';
import { ApproverPickerModal } from './ApproverPickerModal';

type Props = {
  token: string;
  selectedIds: number[];
  candidates: UserBrief[];
  departmentName?: string | null;
  onChange: (ids: number[]) => void;
  disabled?: boolean;
};

function userLabel(u: UserBrief): string {
  return u.displayName?.trim() || u.email;
}

export function ApproverPickerField({
  token,
  selectedIds,
  candidates,
  departmentName,
  onChange,
  disabled = false,
}: Props) {
  const [modalOpen, setModalOpen] = useState(false);

  const selectedLabels = useMemo(
    () =>
      selectedIds
        .map((id) => candidates.find((c) => c.id === id))
        .filter((u): u is UserBrief => u != null)
        .map((u) => userLabel(u)),
    [selectedIds, candidates],
  );

  return (
    <>
      <div className="approver-picker-field">
        <button
          type="button"
          className="btn-secondary office-sidebar-btn approver-picker-field__open"
          disabled={disabled}
          onClick={() => setModalOpen(true)}
        >
          {selectedIds.length === 0 ? 'Выбрать согласующих…' : 'Изменить согласующих'}
        </button>
        {selectedIds.length > 0 && (
          <ol className="approver-picker-field__chips">
            {selectedIds.map((id, i) => {
              const u = candidates.find((c) => c.id === id);
              if (!u) return null;
              return (
                <li key={id}>
                  <span className="approver-picker-field__step">{i + 1}.</span>
                  {userLabel(u)}
                  <span className="approver-picker-field__role">{ROLE_LABELS[u.role] ?? u.role}</span>
                </li>
              );
            })}
          </ol>
        )}
        {selectedLabels.length === 0 && departmentName && (
          <p className="approver-picker-field__hint">
            Коллеги из «{departmentName}»
          </p>
        )}
      </div>
      {modalOpen && (
        <ApproverPickerModal
          token={token}
          selectedIds={selectedIds}
          departmentName={departmentName}
          onConfirm={onChange}
          onClose={() => setModalOpen(false)}
        />
      )}
    </>
  );
}
