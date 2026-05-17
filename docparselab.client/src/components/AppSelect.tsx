import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';

export type SelectOption = {
  value: string;
  label: string;
  disabled?: boolean;
};

type AppSelectProps = {
  value: string;
  onChange: (value: string) => void;
  options: SelectOption[];
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  id?: string;
  'aria-label'?: string;
};

export function optionsFromEntries(
  entries: [string, string][],
  emptyOption?: { value: string; label: string },
): SelectOption[] {
  const list = entries.map(([value, label]) => ({ value, label }));
  return emptyOption ? [emptyOption, ...list] : list;
}

export function optionsFromLabels(
  labels: Record<string, string>,
  emptyOption?: { value: string; label: string },
): SelectOption[] {
  return optionsFromEntries(Object.entries(labels), emptyOption);
}

const MENU_EXTRA_WIDTH = 36;

function measureLabelsWidth(labels: string[]): number {
  if (typeof document === 'undefined' || labels.length === 0) return 0;
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d');
  if (!ctx) return 0;
  ctx.font = '400 0.88rem "Plus Jakarta Sans", system-ui, sans-serif';
  return Math.max(...labels.map((label) => ctx.measureText(label).width));
}

export function AppSelect({
  value,
  onChange,
  options,
  placeholder = '— выберите —',
  disabled = false,
  className = '',
  id,
  'aria-label': ariaLabel,
}: AppSelectProps) {
  const autoId = useId();
  const listboxId = `${id ?? autoId}-listbox`;
  const [open, setOpen] = useState(false);
  const [menuStyle, setMenuStyle] = useState<React.CSSProperties>({});
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);

  const selected = options.find((o) => o.value === value);
  const displayLabel = selected?.label ?? placeholder;
  const isPlaceholder = !selected && value === '';

  const updatePosition = useCallback(() => {
    const el = triggerRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const maxHeight = 280;
    const spaceBelow = window.innerHeight - rect.bottom - 8;
    const spaceAbove = rect.top - 8;
    const openUp = spaceBelow < 160 && spaceAbove > spaceBelow;
    const height = Math.min(maxHeight, openUp ? spaceAbove - 4 : spaceBelow - 4);

    const labels = options.map((o) => o.label);
    const contentWidth = measureLabelsWidth(labels) + MENU_EXTRA_WIDTH;
    const menuWidth = Math.min(window.innerWidth - rect.left - 12, contentWidth, 280);

    setMenuStyle({
      position: 'fixed',
      left: rect.left,
      width: menuWidth,
      zIndex: 10000,
      maxHeight: Math.max(120, height),
      ...(openUp
        ? { bottom: window.innerHeight - rect.top + 4 }
        : { top: rect.bottom + 4 }),
    });
  }, [options]);

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

  const pick = (next: string) => {
    onChange(next);
    setOpen(false);
  };

  const menu = open && !disabled && (
    <div
      id={listboxId}
      role="listbox"
      className="app-select__menu"
      style={menuStyle}
      aria-label={ariaLabel}
    >
      {options.map((opt) => {
        const active = opt.value === value;
        return (
          <button
            key={opt.value === '' ? '__empty' : opt.value}
            type="button"
            role="option"
            aria-selected={active}
            disabled={opt.disabled}
            className={`app-select__option${active ? ' app-select__option--selected' : ''}`}
            onClick={() => pick(opt.value)}
            title={opt.label}
          >
            <span className="app-select__option-label">{opt.label}</span>
            {active && (
              <svg className="app-select__check" width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden>
                <path d="M5 12l5 5L20 7" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
            )}
          </button>
        );
      })}
    </div>
  );

  return (
    <div
      ref={rootRef}
      className={`app-select${open ? ' app-select--open' : ''}${disabled ? ' app-select--disabled' : ''} ${className}`.trim()}
    >
      <button
        ref={triggerRef}
        type="button"
        id={id}
        className={`app-select__trigger${isPlaceholder ? ' app-select__trigger--placeholder' : ''}`}
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
