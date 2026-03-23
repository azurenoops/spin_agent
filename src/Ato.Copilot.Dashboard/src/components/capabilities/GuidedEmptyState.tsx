interface GuidedEmptyStateProps {
  onCreateManually: () => void;
  onImportCsp: () => void;
  onImportCrm: () => void;
}

export default function GuidedEmptyState({ onCreateManually, onImportCsp, onImportCrm }: GuidedEmptyStateProps) {
  const cards = [
    {
      title: 'Create Manually',
      description: 'Define a new security capability with name, provider, category, and description.',
      action: onCreateManually,
      buttonText: 'Create Capability',
      color: 'blue' as ColorKey,
    },
    {
      title: 'Import CSP Profile',
      description: 'Import capabilities from a Cloud Service Provider profile (e.g., Azure FedRAMP High).',
      action: onImportCsp,
      buttonText: 'Import CSP Profile',
      color: 'indigo' as ColorKey,
    },
    {
      title: 'Import CRM File',
      description: 'Upload a Customer Responsibility Matrix (CSV or Excel) to bulk-create capabilities.',
      action: onImportCrm,
      buttonText: 'Import CRM',
      color: 'green' as ColorKey,
    },
  ];

  const colorMap = {
    blue: { bg: 'bg-blue-50', border: 'border-blue-200', button: 'bg-blue-600', hover: 'hover:bg-blue-700' },
    indigo: { bg: 'bg-indigo-50', border: 'border-indigo-200', button: 'bg-indigo-600', hover: 'hover:bg-indigo-700' },
    green: { bg: 'bg-green-50', border: 'border-green-200', button: 'bg-green-600', hover: 'hover:bg-green-700' },
  } as const;

  type ColorKey = keyof typeof colorMap;

  return (
    <div className="py-12">
      <div className="text-center mb-8">
        <h2 className="text-xl font-semibold text-gray-900">Get Started with Security Capabilities</h2>
        <p className="mt-2 text-sm text-gray-500">
          Security capabilities connect components to controls. Choose how you'd like to begin.
        </p>
      </div>
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6 max-w-4xl mx-auto">
        {cards.map(card => {
          const c = colorMap[card.color] ?? colorMap.blue;
          return (
            <div key={card.title} className={`rounded-xl border ${c.border} ${c.bg} p-6 flex flex-col`}>
              <h3 className="text-lg font-semibold text-gray-900 mb-2">{card.title}</h3>
              <p className="text-sm text-gray-600 flex-1 mb-4">{card.description}</p>
              <button
                onClick={card.action}
                className={`w-full rounded-lg ${c.button} px-4 py-2.5 text-sm font-medium text-white ${c.hover}`}
              >
                {card.buttonText}
              </button>
            </div>
          );
        })}
      </div>
    </div>
  );
}
