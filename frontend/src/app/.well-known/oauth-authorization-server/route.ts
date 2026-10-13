import { OAUTH_METADATA_API_PATHS, proxyOAuthMetadata } from "@/lib/oauthMetadataProxy";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export function GET(request: Request) {
  return proxyOAuthMetadata(request, OAUTH_METADATA_API_PATHS.authorizationServer);
}
