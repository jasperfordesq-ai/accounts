import { proxyMcpRequest } from "@/lib/mcpProxy";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export function GET(request: Request) {
  return proxyMcpRequest(request);
}

export function POST(request: Request) {
  return proxyMcpRequest(request);
}

export function DELETE(request: Request) {
  return proxyMcpRequest(request);
}
