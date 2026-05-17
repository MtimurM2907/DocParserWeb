import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import type { SelectOption } from './AppSelect';

type AppMultiSelectProps = {
  values: string[];
  onChange: (values: string[]) => void;
  options: SelectOption[];
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  id?: string;
  'aria-label'?: string;
};

export function AppMultiSelect({
  values,
  onChange,
  options,
  placeholder = '— выберите —',
  disabled = false,
  className = '',
  id,
  'aria-label': ariaLabel,
}: AppMultiSelectProps) {
  const autoId = useId();
  const listboxId = `${id ?? autoId}-listbox`;
  const [open, setOpen] = useState(false);
  const [menuStyle, setMenuStyle] = useState<React.CSSProperties>({});
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);

  const selectedLabels = options.filter((o) => values.includes(o.value)).map((o) => o.label);
  const displayLabel =
    selectedLabels.length === 0
      ? placeholder
      : selectedLabels.length <= 2
        ? selectedLabels.join(', ')
        : `${selectedLabels.length} выбрано`;

  const updatePosition = useCallback(() => {
    const el = triggerRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const maxHeight = 240;
    const spaceBelow = window.innerHeight - rect.bottom - 8;
    setMenuStyle({
      position: 'fixed',
      top: rect.bottom + 4,
      left: rect.left,
      width: Math.max(rect.width, 260),
      zIndex: 10000,
      maxHeight: Math.min(maxHeight, Math.max(120, spaceBelow - 4)),
    });
  }, []);

  useEffect(() => {
    if (!open) return;
    updatePosition();
    const onReposition = () => updatePosition();
    window.addEventListener('resize', onReposition);
    window.addEventListener('scroll', onReposition, true);
    return () => {
      window.removeEventListener('resize', onReposition);
      window.removeEventListener('scroll', onReposition, true);
    };
  }, [open, updatePosition]);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (rootRef.current?.contains(target)) return;
      const menu = document.getElementById(listboxId);
      if (menu?.contains(target)) return;
      setOpen(false);
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open, listboxId]);

  const toggle = (optValue: string) => {
    if (values.includes(optValue)) {
      onChange(values.filter((v) => v !== optValue));
    } else {
      onChange([...values, optValue]);
    }
  };

  const menu = open && !disabled && (
    <div id={listboxId} role="listbox" className="app-select__menu app-select__menu--multi" style={menuStyle} aria-label={ariaLabel} aria-multiselectable>
      {options.map((opt) => {
        const checked = values.includes(opt.value);
        return (
          <label key={opt.value} className={`app-select__option app-select__option--check${checked ? ' app-select__option--selected' : ''}`}>
            <input type="checkbox" checked={checked} disabled={opt.disabled} onChange={() => toggle(opt.value)} />
            <span>{opt.label}</span>
          </label>
        );
      })}
    </div>
  );

  return (
    <div
      ref={rootRef}
      className={`app-select app-select--multi${open ? ' app-select--open' : ''}${disabled ? ' app-select--disabled' : ''} ${className}`.trim()}
    >
      <button
        ref={triggerRef}
        type="button"
        id={id}
        className={`app-select__trigger${selectedLabels.length === 0 ? ' app-select__trigger--placeholder' : ''}`}
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listboxId}
        aria-label={ariaLabel}
        onClick={() => {
          if (disabled) return;
          if (!open) updatePosition();
          setOpen((v) => !v);
        }}
      >
        <span className="app-select__value">{displayLabel}</span>
        <svg className="app-select__chevron" width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden>
          <path d="M6 9l6 6 6-6" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>
      {typeof document !== 'undefined' && menu ? createPortal(menu, document.body) : null}
    </div>
  );
}
