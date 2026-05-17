import type { CurrentUser } from '../types/office';
import { ROLE_LABELS } from '../types/office';
import { NotificationsBell } from './NotificationsBell';
import { IconLogOut, IconUser } from './AppIcons';

type Props = {
  token: string;
  user: CurrentUser | null;
  email: string;
  onOpenProfile: () => void;
  onLogout: () => void;
  onOpenDocument: (docId: number) => void;
};

function userInitials(displayName: string | null | undefined, email: string): string {
  const src = displayName?.trim() || email;
  const parts = src.split(/\s+/).filter(Boolean);
  if (parts.length >= 2) {
    return `${parts[0]![0] ?? ''}${parts[1]![0] ?? ''}`.toUpperCase();
  }
  return src.slice(0, 2).toUpperCase();
}

export function UserHeaderMenu({ token, user, email, onOpenProfile, onLogout, onOpenDocument }: Props) {
  const displayName = user?.displayName?.trim() || email;
  const roleLabel = user?.role ? (ROLE_LABELS[user.role] ?? user.role) : null;

  return (
    <div className="user-header-menu">
      <NotificationsBell token={token} onOpenDocument={onOpenDocument} />

      <div className="user-header-menu__divider" aria-hidden />

      <div className="user-header-menu__identity">
        <span className="user-header-menu__avatar" aria-hidden>
          {userInitials(user?.displayName, email)}
        </span>
        <div className="user-header-menu__text">
          <span className="user-header-menu__name">{displayName}</span>
          <span className="user-header-menu__email">{email}</span>
          {user?.departmentName && <span className="user-header-menu__dept">{user.departmentName}</span>}
        </div>
        {roleLabel && <span className="user-header-menu__role">{roleLabel}</span>}
      </div>

      <div className="user-header-menu__divider user-header-menu__divider--actions" aria-hidden />

      <div className="user-header-menu__actions">
        <button type="button" className="user-header-menu__action" onClick={onOpenProfile} title="Профиль">
          <IconUser />
          <span>Профиль</span>
        </button>
        <button type="button" className="user-header-menu__action user-header-menu__action--logout" onClick={onLogout} title="Выйти">
          <IconLogOut />
          <span>Выйти</span>
        </button>
      </div>
    </div>
  );
}
