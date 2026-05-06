import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";
import { VitePWA } from "vite-plugin-pwa";

const apiTarget = process.env.VITE_API_TARGET ?? "http://127.0.0.1:5027";

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: "autoUpdate",
      includeAssets: ["favicon.svg"],
      manifest: {
        name: "MainAgent Dashboard",
        short_name: "MainAgent",
        description: "Local dashboard for chat, runs, memory, graph, and settings.",
        theme_color: "#146c72",
        background_color: "#f4f6f8",
        display: "standalone",
        start_url: "/",
        icons: [
          {
            src: "/pwa-192.svg",
            sizes: "192x192",
            type: "image/svg+xml",
            purpose: "any maskable"
          },
          {
            src: "/pwa-512.svg",
            sizes: "512x512",
            type: "image/svg+xml",
            purpose: "any maskable"
          }
        ]
      },
      workbox: {
        globPatterns: ["**/*.{js,css,html,svg,png,ico,webmanifest}"],
        navigateFallbackDenylist: [/^\/api\//, /^\/openapi\//]
      }
    })
  ],
  build: {
    outDir: "../wwwroot",
    emptyOutDir: true
  },
  server: {
    proxy: {
      "/api": apiTarget,
      "/openapi": apiTarget
    }
  }
});
