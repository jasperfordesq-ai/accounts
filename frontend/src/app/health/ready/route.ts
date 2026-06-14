import { NextResponse } from "next/server";
import { withStrictTransportSecurity } from "@/lib/securityHeaders";
import { getFrontendReadiness } from "./readiness";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET() {
  const result = await getFrontendReadiness();
  return withStrictTransportSecurity(NextResponse.json(result.body, { status: result.status }));
}
