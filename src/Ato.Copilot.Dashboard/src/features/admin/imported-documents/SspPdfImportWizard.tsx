import Step4SspPdfImport from '../../onboarding/steps/Step4SspPdfImport';

export default function SspPdfImportWizard({ onClose }: { onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-50 overflow-y-auto bg-black/40 p-4">
      <section role="dialog" aria-modal="true" aria-label="Review SSP PDF import"
        className="mx-auto my-8 max-w-4xl rounded-xl bg-white p-6 shadow-xl">
        <div className="mb-4 flex justify-end">
          <button type="button" onClick={onClose}
            className="rounded border border-gray-300 px-3 py-2 text-sm">Close import</button>
        </div>
        <Step4SspPdfImport />
      </section>
    </div>
  );
}
