import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { oneDark } from 'react-syntax-highlighter/dist/esm/styles/prism';
import { Link } from 'react-router-dom';
import type { Components } from 'react-markdown';

export interface MarkdownRendererProps {
  content: string;
}

const DASHBOARD_ROUTE_PATTERN = /^\/(systems|capabilities|boundaries)\b/;
const NIST_CONTROL_PATTERN = /^[A-Z]{2}-\d+$/;

const components: Components = {
  a({ href, children }) {
    const text = String(children);

    // Internal dashboard routes
    if (href && DASHBOARD_ROUTE_PATTERN.test(href)) {
      return (
        <Link to={href} className="text-indigo-600 underline hover:text-indigo-800">
          {children}
        </Link>
      );
    }

    // NIST control ID links (rendered by server as markdown links)
    if (href && NIST_CONTROL_PATTERN.test(text)) {
      return (
        <Link to={href} className="rounded bg-indigo-50 px-1 py-0.5 text-xs font-medium text-indigo-700 hover:bg-indigo-100">
          {children}
        </Link>
      );
    }

    return (
      <a href={href} target="_blank" rel="noopener noreferrer" className="text-indigo-600 underline hover:text-indigo-800">
        {children}
      </a>
    );
  },
  code({ className, children, ...props }) {
    const match = /language-(\w+)/.exec(className || '');
    const codeString = String(children).replace(/\n$/, '');

    if (match) {
      return (
        <SyntaxHighlighter
          style={oneDark}
          language={match[1]}
          PreTag="div"
          className="rounded-md text-sm"
        >
          {codeString}
        </SyntaxHighlighter>
      );
    }

    return (
      <code className="rounded bg-gray-100 px-1.5 py-0.5 text-sm text-gray-800" {...props}>
        {children}
      </code>
    );
  },
  table({ children }) {
    return (
      <div className="my-2 overflow-x-auto">
        <table className="min-w-full border-collapse border border-gray-300 text-sm">
          {children}
        </table>
      </div>
    );
  },
  th({ children }) {
    return (
      <th className="border border-gray-300 bg-gray-100 px-3 py-1.5 text-left font-medium text-gray-700">
        {children}
      </th>
    );
  },
  td({ children }) {
    return (
      <td className="border border-gray-300 px-3 py-1.5 text-gray-600">
        {children}
      </td>
    );
  },
};

export default function MarkdownRenderer({ content }: MarkdownRendererProps) {
  return (
    <div className="prose prose-sm max-w-none prose-headings:mb-2 prose-headings:mt-3 prose-p:my-1.5 prose-ul:my-1.5 prose-ol:my-1.5 prose-li:my-0.5">
      <ReactMarkdown remarkPlugins={[remarkGfm]} components={components}>
        {content}
      </ReactMarkdown>
    </div>
  );
}
