interface Parameter { id: string; definition: string }
export function parameterPresentation(parameter: Parameter, index: number): { name: string; warning?: string } {
  try {
    const definition: unknown = JSON.parse(parameter.definition);
    if (definition && typeof definition === 'object' && 'label' in definition
      && typeof definition.label === 'string' && definition.label.trim())
      return { name: definition.label.trim().replace(/^./, letter => letter.toUpperCase()) };
  } catch (error) {
    if (!(error instanceof SyntaxError)) throw error;
    return { name: `Organization-defined value ${index + 1}`, warning: 'The original parameter definition is unstructured. Inspect its source before assigning a value.' };
  }
  return { name: `Organization-defined value ${index + 1}`, warning: 'The source does not provide a readable parameter label. Inspect the original definition.' };
}
export const parameterName = (parameter: Parameter, index: number) => parameterPresentation(parameter, index).name;
export function readableRequirement(text: string, parameters: Parameter[], values: Record<string, string>): string {
  return text.replace(/\{\{\s*insert:\s*param,\s*([^}\s]+)\s*\}\}/gi, (_match: string, id: string) => {
    if (values[id]?.trim()) return values[id].trim();
    const index = parameters.findIndex(p => p.id === id);
    return index >= 0 ? `[${parameterName(parameters[index]!, index)} — not recorded]` : '[Organization-defined value — source definition unavailable]';
  });
}
