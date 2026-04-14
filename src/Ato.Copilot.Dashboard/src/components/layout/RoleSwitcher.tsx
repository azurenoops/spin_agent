import { useState, useRef, useEffect } from 'react';
import { useSettings } from '../../hooks/useSettings';

const roles = [
  { value: 'ISSM', label: 'ISSM', description: 'Information System Security Manager' },
  { value: 'ISSO', label: 'ISSO', description: 'Information System Security Officer' },
  { value: 'MissionOwner', label: 'Mission Owner', description: 'System Mission Owner' },
  { value: 'Engineer', label: 'Engineer', description: 'System Engineer / Developer' },
  { value: 'SCA', label: 'SCA', description: 'Security Control Assessor' },
  { value: 'AO', label: 'AO', description: 'Authorizing Official' },
] as const;

type RoleValue = (typeof roles)[number]['value'];

export default function RoleSwitcher() {
  const { settings, updateSettings } = useSettings();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const handleClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [open]);

  const activeRole = roles.find((r) => r.value === settings.role);

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="flex items-center gap-1.5 rounded-md border border-dashed border-amber-400 px-2.5 py-1 text-xs font-mono text-amber-700 hover:bg-amber-50 transition-colors"
        title="Switch simulated role (DEV)"
        aria-expanded={open}
        aria-haspopup="listbox"
      >
        <span className="text-amber-500 font-semibold">DEV</span>
        <span className="text-gray-600">{activeRole?.label ?? 'Select Role'}</span>
        <svg className={`h-3.5 w-3.5 text-gray-400 transition-transform ${open ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 8.25l-7.5 7.5-7.5-7.5" />
        </svg>
      </button>

      {open && (
        <div className="absolute right-0 z-50 mt-1 w-64 rounded-lg border border-gray-200 bg-white py-1 shadow-lg" role="listbox">
          {roles.map((role) => (
            <button
              key={role.value}
              type="button"
              role="option"
              aria-selected={settings.role === role.value}
              onClick={() => {
                updateSettings({ role: role.value as RoleValue });
                setOpen(false);
              }}
              className={`flex w-full items-center gap-3 px-3 py-2 text-left text-sm transition-colors ${
                settings.role === role.value
                  ? 'bg-blue-50 text-blue-700'
                  : 'text-gray-700 hover:bg-gray-50'
              }`}
            >
              <div className="flex-1">
                <div className="font-medium">{role.label}</div>
                <div className="text-xs text-gray-400">{role.description}</div>
              </div>
              {settings.role === role.value && (
                <svg className="h-4 w-4 text-blue-600 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
                </svg>
              )}
            </button>
          ))}
          {settings.role && (
            <>
              <div className="mx-3 my-1 border-t border-gray-100" />
              <button
                type="button"
                onClick={() => {
                  updateSettings({ role: '' });
                  setOpen(false);
                }}
                className="flex w-full items-center gap-3 px-3 py-2 text-left text-sm text-gray-500 hover:bg-gray-50"
              >
                Clear role
              </button>
            </>
          )}
        </div>
      )}
    </div>
  );
}
