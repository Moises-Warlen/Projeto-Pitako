import type { Config } from "tailwindcss";

const config: Config = {
  content: [
    "./src/pages/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/components/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/app/**/*.{js,ts,jsx,tsx,mdx}"
  ],
  theme: {
    extend: {
      colors: {
        pitako: {
          red: "#DC2626",
          dark: "#09090B",
          panel: "#18181B"
        }
      }
    }
  },
  plugins: []
};

export default config;
