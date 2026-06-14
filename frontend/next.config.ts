import type { NextConfig } from "next";

const enableBuildWorkerThreads = process.env.NEXT_BUILD_WORKER_THREADS === "1";

const nextConfig: NextConfig = {
  ...(enableBuildWorkerThreads
    ? {
        experimental: {
          workerThreads: true,
        },
      }
    : {}),
  output: "standalone",
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Frame-Options", value: "DENY" },
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "no-referrer" },
          { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), payment=()" },
        ],
      },
    ];
  },
};

export default nextConfig;
