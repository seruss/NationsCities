/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './**/*.{razor,html,cshtml}'
  ],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        "primary": "#258cf4",
        "primary-dark": "#1a6fc0",
        "bg-light": "#f5f7f8",
        "bg-dark": "#101922",
        "surface-light": "#ffffff",
        "surface-dark": "#1A2633",
        "surface-darker": "#151e29"
      },
      fontFamily: {
        "sans": ["Inter", "sans-serif"]
      },
      borderRadius: {
        "DEFAULT": "0.5rem",
        "lg": "0.75rem",
        "xl": "1rem",
        "2xl": "1.5rem",
        "full": "9999px"
      }
    }
  },
  plugins: [],
}
