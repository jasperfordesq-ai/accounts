import type { NextConfig } from "next";

const apiUrl = process.env.NEXT_PUBLIC_API_URL || process.env.API_URL || "http://localhost:5090";

const nextConfig: NextConfig = {
  output: "standalone",
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${apiUrl}/api/:path*`,
      },
      {
        source: "/health",
        destination: `${apiUrl}/health`,
      },
    ];
  },
};

export default nextConfig;
