const backendTarget = process.env.API_PROXY_TARGET || "http://localhost:5293";

const nextConfig = {
  reactStrictMode: true,
  async rewrites() {
    // When NEXT_PUBLIC_API_URL is not set, the dashboard calls the same-origin
    // "/api/*" path and Next.js proxies those requests to the backend host.
    if (!process.env.NEXT_PUBLIC_API_URL) {
      return [
        {
          source: "/api/:path*",
          destination: `${backendTarget}/api/:path*`,
        },
        {
          source: "/monitoring/:path*",
          destination: `${backendTarget}/monitoring/:path*`,
        },
        {
          source: "/health/:path*",
          destination: `${backendTarget}/health/:path*`,
        },
      ];
    }
    return [];
  },
};

export default nextConfig;
