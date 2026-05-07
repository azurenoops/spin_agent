import { useState, useCallback, createContext, useContext } from 'react';
import { useParams, Link, NavLink, Outlet } from 'react-router-dom';
import PageLayout from './PageLayout';
import TodoPanel from '../cards/TodoPanel';
import { usePolling } from '../../hooks/usePolling';
import { useSettings } from '../../hooks/useSettings';
import apiClient from '../../api/client';
import { getSystemDetail } from '../../api/systemDetail';
import { getProfileCompleteness } from '../../api/systemProfile';
import type { SystemDetailResponse, ProfileCompletenessResponse, TodoList } from '../../types/dashboard';

// ─── Context ────────────────────────────────────────────────────────────────

interface SystemContextValue {
  detail: SystemDetailResponse;
  refetch: () => void;
}

const SystemContext = createContext<SystemContextValue | null>(null);

export function useSystemContext() {
  const ctx = useContext(SystemContext);
  if (!ctx) throw new Error('useSystemContext must be used inside SystemLayout');
  return ctx;
}

// ─── Nav items (grouped) ────────────────────────────────────────────────────

interface NavItem {
  path: string;
  label: string;
  end?: boolean;
  d: string;
}

interface NavGroup {
  label: string;
  items: NavItem[];
  /** Roles for which this group is a primary focus area */
  primaryFor?: string[];
}

const navGroups: NavGroup[] = [
  {
    label: 'System Profile',
    primaryFor: ['ISSM', 'ISSO', 'MissionOwner', 'Engineer', 'SCA', 'AO'],
    items: [
      { path: '', label: 'Overview', end: true, d: 'M2.25 12l8.954-8.955c.44-.439 1.152-.439 1.591 0L21.75 12M4.5 9.75v10.125c0 .621.504 1.125 1.125 1.125H9.75v-4.875c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125V21h4.125c.621 0 1.125-.504 1.125-1.125V9.75M8.25 21h8.25' },
      { path: 'capability-coverage', label: 'Capabilities', d: 'M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z' },
      { path: 'components', label: 'Components', d: 'M5.25 14.25h13.5m-13.5 0a3 3 0 01-3-3m3 3a3 3 0 100 6h13.5a3 3 0 100-6m-16.5-3a3 3 0 013-3h13.5a3 3 0 013 3m-19.5 0a4.5 4.5 0 01.9-2.7L5.737 5.1a3.375 3.375 0 012.7-1.35h7.126c1.062 0 2.062.5 2.7 1.35l2.587 3.45a4.5 4.5 0 01.9 2.7m0 0a3 3 0 01-3 3m0 3h.008v.008h-.008v-.008zm0-6h.008v.008h-.008v-.008zm-3 6h.008v.008h-.008v-.008zm0-6h.008v.008h-.008v-.008z' },
      { path: 'boundaries', label: 'Boundaries', d: 'M9 12.75L11.25 15 15 9.75m-3-7.036A11.959 11.959 0 013.598 6 11.99 11.99 0 003 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285z' },
      ],
  },
  {
    label: 'Mission Profile',
    primaryFor: ['MissionOwner', 'ISSM'],
    items: [
      { path: 'profile/MissionAndPurpose', label: 'Mission & Purpose', d: 'M12 21v-8.25M15.75 21v-8.25M8.25 21v-8.25M3 9l9-6 9 6m-1.5 12V10.332A48.36 48.36 0 0012 9.75c-2.551 0-5.056.2-7.5.582V21' },
      { path: 'profile/UsersAndAccess', label: 'Users & Access', d: 'M15 19.128a9.38 9.38 0 002.625.372 9.337 9.337 0 004.121-.952 4.125 4.125 0 00-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 018.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0111.964-3.07M12 6.375a3.375 3.375 0 11-6.75 0 3.375 3.375 0 016.75 0zm8.25 2.25a2.625 2.625 0 11-5.25 0 2.625 2.625 0 015.25 0z' },
      { path: 'profile/EnvironmentAndDeployment', label: 'Environment', d: 'M2.25 15a4.5 4.5 0 004.5 4.5H18a3.75 3.75 0 001.332-7.257 3 3 0 00-3.758-3.848 5.25 5.25 0 00-10.233 2.33A4.502 4.502 0 002.25 15z' },
      { path: 'profile/DataTypes', label: 'Data Types', d: 'M20.25 6.375c0 2.278-3.694 4.125-8.25 4.125S3.75 8.653 3.75 6.375m16.5 0c0-2.278-3.694-4.125-8.25-4.125S3.75 4.097 3.75 6.375m16.5 0v11.25c0 2.278-3.694 4.125-8.25 4.125s-8.25-1.847-8.25-4.125V6.375m16.5 0v3.75m-16.5-3.75v3.75m16.5 0v3.75C20.25 16.153 16.556 18 12 18s-8.25-1.847-8.25-4.125v-3.75m16.5 0c0 2.278-3.694 4.125-8.25 4.125s-8.25-1.847-8.25-4.125' },
      { path: 'profile/PortsProtocolsAndServices', label: 'Ports & Protocols', d: 'M7.5 21L3 16.5m0 0L7.5 12M3 16.5h13.5m0-13.5L21 7.5m0 0L16.5 12M21 7.5H7.5' },
      { path: 'profile/LeveragedAuthorizations', label: 'Leveraged Auth', d: 'M13.5 10.5V6.75a4.5 4.5 0 119 0v3.75M3.75 21.75h10.5a2.25 2.25 0 002.25-2.25v-6.75a2.25 2.25 0 00-2.25-2.25H3.75a2.25 2.25 0 00-2.25 2.25v6.75a2.25 2.25 0 002.25 2.25z' },
    ],
  },
  {
    label: 'Compliance Posture',
    primaryFor: ['ISSM', 'ISSO', 'SCA'],
    items: [
      { path: 'baseline', label: 'Categorization', d: 'M4.5 12a7.5 7.5 0 0015 0m-15 0a7.5 7.5 0 1115 0m-15 0H3m16.5 0H21m-1.5 0H12m-8.457 3.077l1.41-.513m14.095-5.13l1.41-.513M5.106 17.785l1.15-.964m11.49-9.642l1.149-.964M7.501 19.795l.75-1.3m7.5-12.99l.75-1.3m-6.063 16.658l.26-1.477m2.605-14.772l.26-1.477m0 17.726l-.26-1.477M10.698 4.614l-.26-1.477M16.5 19.794l-.75-1.299M7.5 4.205L12 12m6.894 5.785l-1.149-.964M6.256 7.178l-1.15-.964m15.352 8.864l-1.41-.513M4.954 9.435l-1.41-.514M12.002 12l-3.75 6.495' },
      { path: 'inheritance', label: 'Control Inheritance', d: 'M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z' },
      { path: 'narratives', label: 'Narratives', d: 'M12 6.042A8.967 8.967 0 006 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 016 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 016-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0018 18a8.967 8.967 0 00-6 2.292m0-14.25v14.25' },
      { path: 'legal', label: 'Legal & Regulatory', d: 'M12 3v17.25m0 0c-1.472 0-2.882.265-4.185.75M12 20.25c1.472 0 2.882.265 4.185.75M18.75 4.97A48.416 48.416 0 0012 4.5c-2.291 0-4.545.16-6.75.47m13.5 0c1.01.143 2.01.317 3 .52m-3-.52l2.62 10.726c.122.499-.106 1.028-.589 1.202a5.988 5.988 0 01-2.031.352 5.988 5.988 0 01-2.031-.352c-.483-.174-.711-.703-.59-1.202L18.75 4.971zm-16.5.52c.99-.203 1.99-.377 3-.52m0 0l2.62 10.726c.122.499-.106 1.028-.589 1.202a5.989 5.989 0 01-2.031.352 5.989 5.989 0 01-2.031-.352c-.483-.174-.711-.703-.59-1.202L5.25 4.971z' },
    ],
  },
  {
    label: 'Assessment & Remediation',
    primaryFor: ['ISSM', 'ISSO', 'SCA', 'Engineer'],
    items: [
      { path: 'assessments', label: 'Assessments', d: 'M9 12h3.75M9 15h3.75M9 18h3.75m3 .75H18a2.25 2.25 0 002.25-2.25V6.108c0-1.135-.845-2.098-1.976-2.192a48.424 48.424 0 00-1.123-.08m-5.801 0c-.065.21-.1.433-.1.664 0 .414.336.75.75.75h4.5a.75.75 0 00.75-.75 2.25 2.25 0 00-.1-.664m-5.8 0A2.251 2.251 0 0113.5 2.25H15a2.25 2.25 0 012.15 1.586m-5.8 0c-.376.023-.75.05-1.124.08C9.095 4.01 8.25 4.973 8.25 6.108V8.25m0 0H4.875c-.621 0-1.125.504-1.125 1.125v11.25c0 .621.504 1.125 1.125 1.125h9.75c.621 0 1.125-.504 1.125-1.125V9.375c0-.621-.504-1.125-1.125-1.125H8.25zM6.75 12h.008v.008H6.75V12zm0 3h.008v.008H6.75V15zm0 3h.008v.008H6.75V18z' },
      { path: 'remediation', label: 'Remediation', d: 'M11.42 15.17l-4.655-5.653a.75.75 0 010-.964l.903-.994a.75.75 0 011.113 0l3.64 3.938 6.64-7.193a.75.75 0 011.113 0l.903.994a.75.75 0 010 .964l-7.543 8.166a1.5 1.5 0 01-2.114 0z' },
      { path: 'poam', label: 'POA&M', d: 'M12 9v3.75m0-10.036A11.959 11.959 0 013.598 6 11.99 11.99 0 003 9.75c0 5.592 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.57-.598-3.75h-.152c-3.196 0-6.1-1.249-8.25-3.286zM12 9v3.75m-3.75 0h7.5' },
      { path: 'evidence', label: 'Evidence', d: 'M18.375 12.739l-7.693 7.693a4.5 4.5 0 01-6.364-6.364l10.94-10.94A3 3 0 1119.5 7.372L8.552 18.32m.009-.01l-.01.01m5.699-9.941l-7.81 7.81a1.5 1.5 0 002.112 2.13' },
      { path: 'deviations', label: 'Deviations', d: 'M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z' },
    ],
  },
  {
    label: 'Planning & Delivery',
    primaryFor: ['ISSM', 'AO'],
    items: [
      { path: 'roadmap', label: 'Implementation Roadmap', d: 'M3 3v1.5M3 21v-6m0 0l2.77-.693a9 9 0 016.208.682l.108.054a9 9 0 006.086.71l3.114-.732a48.524 48.524 0 01-.005-10.499l-3.11.732a9 9 0 01-6.085-.711l-.108-.054a9 9 0 00-6.208-.682L3 4.5M3 15V4.5' },
      { path: 'documents', label: 'Documents', d: 'M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z' },
      { path: 'conmon', label: 'ConMon', d: 'M3 13.5h4.5V21H3v-7.5zm6.75-6h4.5V21h-4.5V7.5zm6.75-4.5H21V21h-4.5V3z' },
    ],
  },
];

// ─── Layout ─────────────────────────────────────────────────────────────────

export default function SystemLayout() {
  const { id } = useParams<{ id: string }>();
  const { settings } = useSettings();
  const [detail, setDetail] = useState<SystemDetailResponse | null>(null);
  const [profileCompleteness, setProfileCompleteness] = useState<ProfileCompletenessResponse | null>(null);
  const [todoCount, setTodoCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [navCollapsed, setNavCollapsed] = useState(false);
  const [sidePanelTab, setSidePanelTab] = useState<'todo' | 'details'>('todo');

  const withTimeout = useCallback(<T,>(promise: Promise<T>, timeoutMs: number): Promise<T> => {
    return Promise.race([
      promise,
      new Promise<T>((_, reject) => {
        setTimeout(() => reject(new Error(`Request timed out after ${timeoutMs}ms`)), timeoutMs);
      }),
    ]);
  }, []);

  const fetchData = useCallback(async () => {
    if (!id) return;
    try {
      const [d, comp, todoList] = await Promise.all([
        withTimeout(getSystemDetail(id), 8000),
        withTimeout(getProfileCompleteness(id), 5000).catch(() => null),
        withTimeout(
          apiClient.get<TodoList>(`/systems/${id}/todos`).then((r) => r.data),
          5000,
        ).catch(() => null),
      ]);
      setDetail(d);
      setProfileCompleteness(comp);
      setTodoCount(todoList?.items.length ?? 0);
      setError(null);
    } catch {
      setError('Failed to load system detail');
      setTodoCount(0);
    } finally {
      setLoading(false);
    }
  }, [id, withTimeout]);

  usePolling(fetchData);

  if (loading) {
    return (
      <PageLayout title="System Detail">
        <p className="text-gray-500">Loading system detail...</p>
      </PageLayout>
    );
  }

  if (error || !detail) {
    return (
      <PageLayout title="System Detail">
        <p className="text-red-500">{error ?? 'System not found'}</p>
      </PageLayout>
    );
  }

  const basePath = `/systems/${detail.systemId}`;

  // Count incomplete profile sections for notification badge on details tab
  const profileActionCount = profileCompleteness
    ? profileCompleteness.incompleteSections.filter(
        (s) => s.status !== 'Approved' && s.status !== 'UnderReview'
      ).length
    : 0;

  const sidePanelTabs = [
    { key: 'todo' as const, label: 'To do', badge: todoCount },
    { key: 'details' as const, label: 'System Details', badge: profileActionCount },
  ];

  const sidePanel = (
    <div className="flex flex-col h-full">
      {/* Tab bar */}
      <div className="flex border-b border-gray-200 bg-white px-2 pt-2">
        {sidePanelTabs.map((tab) => (
          <button
            key={tab.key}
            type="button"
            onClick={() => setSidePanelTab(tab.key)}
            className={`flex-1 px-3 py-2 text-sm font-medium transition-colors border-b-2 ${
              sidePanelTab === tab.key
                ? 'border-indigo-600 text-indigo-700'
                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
            }`}
          >
            {tab.label}
            {tab.badge > 0 && (
              <span className="ml-1.5 inline-flex items-center justify-center rounded-full bg-indigo-100 px-1.5 py-0.5 text-xs font-medium text-indigo-600">
                {tab.badge}
              </span>
            )}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-y-auto p-4 space-y-4">
        {sidePanelTab === 'todo' && (
          <TodoPanel systemId={detail.systemId} />
        )}

        {sidePanelTab === 'details' && (
          <>
            {/* Profile Summary Card */}
            {profileCompleteness && (
              <div className="rounded-xl border border-gray-200 bg-white p-4 space-y-3">
                <h3 className="text-sm font-semibold text-gray-700">Profile Completeness</h3>
                <div className="w-full bg-gray-200 rounded-full h-2">
                  <div
                    className="bg-indigo-600 h-2 rounded-full transition-all"
                    style={{ width: `${profileCompleteness.approvedPercentage}%` }}
                  />
                </div>
                <p className="text-xs text-gray-500">
                  {profileCompleteness.statusCounts['Approved'] ?? 0} / {profileCompleteness.totalSections} mandatory approved
                </p>
                {profileCompleteness.missionOwnerName && (
                  <p className="text-xs text-gray-500">
                    Mission Owner: <span className="font-medium text-gray-700">{profileCompleteness.missionOwnerName}</span>
                  </p>
                )}
                {settings.role === 'MissionOwner' && profileCompleteness.incompleteSections.length > 0 && profileCompleteness.incompleteSections[0] && (
                  <Link
                    to={`${basePath}/profile/${profileCompleteness.incompleteSections[0].sectionType}`}
                    className="inline-block text-xs text-indigo-600 hover:underline"
                  >
                    Continue editing profile →
                  </Link>
                )}
              </div>
            )}

            {/* System Summary */}
            <div className="rounded-xl border border-gray-200 bg-white">
              <div className="divide-y divide-gray-100 text-sm">
                <div className="flex items-center justify-between px-5 py-2.5">
                  <span className="text-gray-500">Name</span>
                  <span className="font-medium text-gray-900">{detail.name}</span>
                </div>
                <div className="flex items-center justify-between px-5 py-2.5">
                  <span className="text-gray-500">Acronym</span>
                  <span className="font-medium text-gray-900">{detail.acronym || '—'}</span>
                </div>
                <div className="flex items-center justify-between px-5 py-2.5">
                  <span className="text-gray-500">System Type</span>
                  <span className="font-medium text-gray-900">{detail.systemType}</span>
                </div>
                <div className="flex items-center justify-between px-5 py-2.5">
                  <span className="text-gray-500">Mission Criticality</span>
                  <span className="font-medium text-gray-900">{detail.missionCriticality}</span>
                </div>
                <div className="flex items-center justify-between px-5 py-2.5">
                  <span className="text-gray-500">Hosting</span>
                  <span className="font-medium text-gray-900">{detail.hostingEnvironment}</span>
                </div>
              </div>
            </div>

            {/* Security Categorization */}
            {detail.categorization && (
              <div className="rounded-xl border border-gray-200 bg-white">
                <div className="px-5 pt-5 pb-1">
                  <h2 className="text-lg font-semibold text-gray-900">Security Categorization</h2>
                  <p className="text-xs text-gray-400 mt-0.5">{detail.categorization.formalNotation}</p>
                </div>
                <div className="divide-y divide-gray-100 text-sm">
                  {(['confidentiality', 'integrity', 'availability'] as const).map((dim) => {
                    const val = detail.categorization![dim];
                    const color = val === 'High' ? 'bg-red-100 text-red-700' : val === 'Moderate' ? 'bg-amber-100 text-amber-700' : 'bg-green-100 text-green-700';
                    return (
                      <div key={dim} className="flex items-center justify-between px-5 py-2.5">
                        <span className="text-gray-500 capitalize">{dim}</span>
                        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${color}`}>{val}</span>
                      </div>
                    );
                  })}
                  <div className="flex items-center justify-between px-5 py-2.5">
                    <span className="text-gray-500">Overall</span>
                    <span className="font-medium text-gray-900">{detail.categorization.overall}</span>
                  </div>
                  <div className="flex items-center justify-between px-5 py-2.5">
                    <span className="text-gray-500">DoD IL</span>
                    <span className="font-medium text-gray-900">{detail.categorization.dodImpactLevel}</span>
                  </div>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );

  const leftPanel = (
    <aside
      className={`hidden md:flex flex-col flex-shrink-0 border-r border-gray-200 bg-white overflow-y-auto transition-all duration-200 ${
        navCollapsed ? 'w-14' : 'w-56'
      }`}
    >
      <div className={`flex items-center ${navCollapsed ? 'justify-center' : 'justify-between'} px-3 py-3 border-b border-gray-100`}>
        {!navCollapsed && (
          <span className="text-xs font-semibold uppercase tracking-wider text-gray-400">Navigation</span>
        )}
        <button
          type="button"
          onClick={() => setNavCollapsed(!navCollapsed)}
          className="rounded-md p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
          title={navCollapsed ? 'Expand navigation' : 'Collapse navigation'}
          aria-label={navCollapsed ? 'Expand navigation' : 'Collapse navigation'}
        >
          <svg className={`h-4 w-4 transition-transform ${navCollapsed ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5L8.25 12l7.5-7.5" />
          </svg>
        </button>
      </div>
      <nav className="flex-1 py-2 px-2 overflow-y-auto">
        {navGroups.map((group, gi) => {
          const isPrimary = !settings.role || !group.primaryFor || group.primaryFor.includes(settings.role);
          return (
          <div key={group.label} className={!isPrimary ? 'opacity-50' : ''}>
            {/* Group divider — thin line when collapsed, label when expanded */}
            {gi > 0 && navCollapsed && (
              <div className="my-2 border-t border-gray-200" />
            )}
            {!navCollapsed && (
              <div className={`px-3 ${gi === 0 ? 'pt-1' : 'pt-4'} pb-1 flex items-center gap-1.5`}>
                <span className="text-[10px] font-semibold uppercase tracking-wider text-gray-400">
                  {group.label}
                </span>
                {isPrimary && settings.role && (
                  <span className="h-1.5 w-1.5 rounded-full bg-indigo-500 flex-shrink-0" title="Primary for your role" />
                )}
              </div>
            )}
            <div className="space-y-0.5">
              {group.items.map((item) => (
                <NavLink
                  key={item.path}
                  to={`${basePath}${item.path ? `/${item.path}` : ''}`}
                  end={item.end}
                  className={({ isActive }) =>
                    `flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors ${
                      isActive
                        ? 'bg-indigo-50 text-indigo-700 font-medium'
                        : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
                    } ${navCollapsed ? 'justify-center' : ''}`
                  }
                  title={navCollapsed ? item.label : undefined}
                >
                  <svg className="h-5 w-5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d={item.d} />
                  </svg>
                  {!navCollapsed && <span className="truncate">{item.label}</span>}
                </NavLink>
              ))}
            </div>
          </div>
          );
        })}
      </nav>
    </aside>
  );

  return (
    <SystemContext.Provider value={{ detail, refetch: fetchData }}>
      <PageLayout
        title={detail.name}
        sidePanel={sidePanel}
        leftPanel={leftPanel}
      >
        {/* Breadcrumb */}
        <div className="mb-4 text-sm">
          <Link to="/" className="text-indigo-600 hover:underline">
            Portfolio
          </Link>
          <span className="mx-2 text-gray-400">/</span>
          <Link to={basePath} className="text-indigo-600 hover:underline">
            {detail.name}
          </Link>
        </div>

        <Outlet />
      </PageLayout>
    </SystemContext.Provider>
  );
}
