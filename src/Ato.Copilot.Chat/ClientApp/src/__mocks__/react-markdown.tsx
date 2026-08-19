import React from 'react';
// Jest stub for react-markdown (ESM-only; cannot be CJS-transformed by Jest).
const ReactMarkdown = ({ children }: { children: React.ReactNode }) => <>{children}</>;
export default ReactMarkdown;
