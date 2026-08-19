import React from 'react';
// Jest stub for react-syntax-highlighter and its sub-paths (ESM-only).
export const Prism = ({ children }: { children: React.ReactNode }) => <pre>{children}</pre>;
export const Light = ({ children }: { children: React.ReactNode }) => <pre>{children}</pre>;
export default Prism;
// Stub for style imports (e.g. oneDark, etc.)
export const oneDark = {};
export const vs = {};
