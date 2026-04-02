import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: "http://localhost:5187/api/:path*",
      },
      {
        source: "/health",
        destination: "http://localhost:5187/health",
      },
    ];
  },
};

export default nextConfig;
