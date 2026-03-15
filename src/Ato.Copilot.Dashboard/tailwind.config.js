/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        severity: {
          green: '#22c55e',
          yellow: '#eab308',
          red: '#ef4444',
          expired: '#1f2937',
          gray: '#9ca3af',
        },
      },
    },
  },
  plugins: [require('@tailwindcss/typography')],
};
