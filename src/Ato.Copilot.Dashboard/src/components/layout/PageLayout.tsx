import { useState, useRef, useEffect, type ReactNode } from 'react';
import { NavLink } from 'react-router-dom';
import HelpPanel from '../help/HelpPanel';
import ChatToggle from '../chat/ChatToggle';
import { useChatPanel } from '../chat/ChatPanelContext';
import SettingsPanel from '../settings/SettingsPanel';
import NotificationPanel from '../notifications/NotificationPanel';
import { useNotifications } from '../../hooks/useNotifications';

const navItems = [
  { to: '/', label: 'Portfolio' },
  { to: '/assessments', label: 'Assessments' },
  { to: '/remediation', label: 'Remediation' },
  { to: '/capabilities', label: 'Capabilities' },
];

interface PageLayoutProps {
  title: string;
  children: ReactNode;
  sidePanel?: ReactNode;
}

export default function PageLayout({ title, children, sidePanel }: PageLayoutProps) {
  const [sidePanelOpen, setSidePanelOpen] = useState(true);
  const [helpPanelOpen, setHelpPanelOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const { panelState, togglePanel } = useChatPanel();
  const { unreadCount } = useNotifications();
  const notifRef = useRef<HTMLDivElement>(null);

  // Close notification panel when clicking outside
  useEffect(() => {
    if (!notificationsOpen) return;
    const handleClick = (e: MouseEvent) => {
      if (notifRef.current && !notifRef.current.contains(e.target as Node)) {
        setNotificationsOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [notificationsOpen]);

  return (
    <div className="flex h-screen flex-col overflow-hidden">
      {/* Top header */}
      <header className="flex h-14 flex-shrink-0 items-center justify-between border-b border-gray-200 bg-white px-6">
        <div className="flex items-center gap-6">
          <NavLink to="/" className="text-lg font-semibold text-blue-700 hover:text-blue-800">ATO Copilot</NavLink>
          <nav className="hidden items-center gap-1 md:flex">
            {navItems.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.to === '/'}
                className={({ isActive }) =>
                  `rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
                    isActive
                      ? 'bg-blue-50 text-blue-700'
                      : 'text-gray-600 hover:bg-gray-100 hover:text-gray-900'
                  }`
                }
              >
                {item.label}
              </NavLink>
            ))}
          </nav>
          <span className="hidden text-sm text-gray-400 lg:block">|</span>
          <h1 className="hidden text-sm font-medium text-gray-700 lg:block">{title}</h1>
        </div>
          <div className="flex items-center gap-1">
            <button type="button" className="rounded-lg p-2 text-gray-500 hover:bg-gray-100 hover:text-gray-700" aria-label="Search" title="Search">
              <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
              </svg>
            </button>
            <div ref={notifRef} className="relative">
              <button type="button" onClick={() => setNotificationsOpen(!notificationsOpen)} className={`rounded-lg p-2 hover:bg-gray-100 hover:text-gray-700 ${notificationsOpen ? 'bg-blue-50 text-blue-600' : 'text-gray-500'}`} aria-label="Notifications" title="Notifications" aria-expanded={notificationsOpen}>
                <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M14.857 17.082a23.848 23.848 0 0 0 5.454-1.31A8.967 8.967 0 0 1 18 9.75V9A6 6 0 0 0 6 9v.75a8.967 8.967 0 0 1-2.312 6.022c1.733.64 3.56 1.085 5.455 1.31m5.714 0a24.255 24.255 0 0 1-5.714 0m5.714 0a3 3 0 1 1-5.714 0" />
                </svg>
                {unreadCount > 0 && (
                  <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-500 px-1 text-[10px] font-bold text-white">
                    {unreadCount > 99 ? '99+' : unreadCount}
                  </span>
                )}
              </button>
              {notificationsOpen && <NotificationPanel onClose={() => setNotificationsOpen(false)} />}
            </div>
            <button type="button" onClick={() => setHelpPanelOpen(!helpPanelOpen)} className={`rounded-lg p-2 hover:bg-gray-100 hover:text-gray-700 ${helpPanelOpen ? 'bg-blue-50 text-blue-600' : 'text-gray-500'}`} aria-label="Help" title="Help" aria-expanded={helpPanelOpen}>
              <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9.879 7.519c1.171-1.025 3.071-1.025 4.242 0 1.172 1.025 1.172 2.687 0 3.712-.203.179-.43.326-.67.442-.745.361-1.45.999-1.45 1.827v.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 5.25h.008v.008H12v-.008Z" />
              </svg>
            </button>
            <ChatToggle isOpen={panelState.isOpen} onClick={togglePanel} />
            <button type="button" onClick={() => setSettingsOpen(!settingsOpen)} className={`rounded-lg p-2 hover:bg-gray-100 hover:text-gray-700 ${settingsOpen ? 'bg-blue-50 text-blue-600' : 'text-gray-500'}`} aria-label="Settings" title="Settings" aria-expanded={settingsOpen}>
              <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87.074.04.147.083.22.127.325.196.72.257 1.075.124l1.217-.456a1.125 1.125 0 0 1 1.37.49l1.296 2.247a1.125 1.125 0 0 1-.26 1.431l-1.003.827c-.293.241-.438.613-.43.992a7.723 7.723 0 0 1 0 .255c-.008.378.137.75.43.991l1.004.827c.424.35.534.955.26 1.43l-1.298 2.247a1.125 1.125 0 0 1-1.369.491l-1.217-.456c-.355-.133-.75-.072-1.076.124a6.47 6.47 0 0 1-.22.128c-.331.183-.581.495-.644.869l-.213 1.281c-.09.543-.56.94-1.11.94h-2.594c-.55 0-1.019-.398-1.11-.94l-.213-1.281c-.062-.374-.312-.686-.644-.87a6.52 6.52 0 0 1-.22-.127c-.325-.196-.72-.257-1.076-.124l-1.217.456a1.125 1.125 0 0 1-1.369-.49l-1.297-2.247a1.125 1.125 0 0 1 .26-1.431l1.004-.827c.292-.24.437-.613.43-.991a6.932 6.932 0 0 1 0-.255c.007-.38-.138-.751-.43-.992l-1.004-.827a1.125 1.125 0 0 1-.26-1.43l1.297-2.247a1.125 1.125 0 0 1 1.37-.491l1.216.456c.356.133.751.072 1.076-.124.072-.044.146-.086.22-.128.332-.183.582-.495.644-.869l.214-1.28Z" />
                <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
              </svg>
            </button>
            <div className="ml-2 h-8 w-8 rounded-full bg-blue-600 flex items-center justify-center text-white text-sm font-medium cursor-pointer hover:ring-2 hover:ring-blue-300" title="User profile">
              JS
            </div>
          </div>
        </header>

      {/* Content area */}
      <div className="flex flex-1 overflow-hidden">
        <main className="flex-1 overflow-y-auto p-6">{children}</main>
        {(sidePanel || helpPanelOpen) && (
          <div className="hidden xl:flex flex-shrink-0">
            {/* Toggle tab on the edge */}
            {!helpPanelOpen && (
              <button
                type="button"
                onClick={() => setSidePanelOpen(!sidePanelOpen)}
                className="flex h-8 w-5 items-center justify-center self-start mt-4 -mr-px rounded-l border border-r-0 border-gray-200 bg-gray-50 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
                title={sidePanelOpen ? 'Collapse panel' : 'Expand panel'}
                aria-label={sidePanelOpen ? 'Collapse panel' : 'Expand panel'}
              >
                <svg className={`h-3 w-3 transition-transform ${sidePanelOpen ? '' : 'rotate-180'}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
                </svg>
              </button>
            )}
            {helpPanelOpen ? (
              <aside className="w-80 overflow-y-auto border-l border-gray-200 bg-white">
                <HelpPanel onClose={() => setHelpPanelOpen(false)} />
              </aside>
            ) : (
              sidePanelOpen && sidePanel && (
                <aside className="w-80 overflow-y-auto border-l border-gray-200 bg-gray-50 p-4">
                  {sidePanel}
                </aside>
              )
            )}
          </div>
        )}
      </div>

      {/* Settings panel */}
      {settingsOpen && <SettingsPanel onClose={() => setSettingsOpen(false)} />}
    </div>
  );
}
