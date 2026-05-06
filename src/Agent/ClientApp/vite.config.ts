import react from "@vitejs/plugin-react";
import { defineConfig, Plugin } from "vite";
import { VitePWA } from "vite-plugin-pwa";

const apiTarget = process.env.VITE_API_TARGET ?? "http://127.0.0.1:5213";

function mainAgentDevBoundary(): Plugin {
  return {
    name: "mainagent-dev-boundary",
    configureServer(server) {
      server.middlewares.use((request, response, next) => {
        if (request.url === "/__mainagent_vite_health") {
          response.statusCode = 200;
          response.setHeader("Content-Type", "application/json; charset=utf-8");
          response.end(JSON.stringify({ status: "ok", apiTarget }));

          return;
        }

        next();
      });

      return () => {
        server.middlewares.use((request, response, next) => {
          if (request.url?.startsWith("/api/") || request.url?.startsWith("/openapi/")) {
            response.statusCode = 502;
            response.setHeader("Content-Type", "application/json; charset=utf-8");
            response.end(JSON.stringify({
              error: "The Vite dev server did not proxy this API request to ASP.NET.",
              apiTarget,
              path: request.url
            }));

            return;
          }

          next();
        });
      };
    }
  };
}

export default defineConfig({
  plugins: [
    react(),
    mainAgentDevBoundary(),
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
