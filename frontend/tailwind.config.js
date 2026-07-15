/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        // Subtle hover shade sitting between gray-700 and gray-800. Several pages
        // already use `hover:bg-gray-750`; without this it was undefined and those
        // hovers generated no CSS (dead affordance).
        gray: {
          750: '#2d3544',
        },
      },
      // Note: the `shimmer` keyframes + `.animate-shimmer` utility live in
      // index.css (single source of truth). Do not redeclare them here - two
      // definitions of the same class name compile to an ambiguous winner.
    },
  },
  plugins: [],
}
