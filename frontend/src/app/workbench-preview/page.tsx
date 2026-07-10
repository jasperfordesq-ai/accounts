import { WorkbenchPreview } from "@/components/workbench/WorkbenchPreview";

export default async function WorkbenchPreviewPage({
  searchParams,
}: {
  searchParams: Promise<{ state?: string | string[] }>;
}) {
  const resolvedSearchParams = await searchParams;
  const canonicalState = typeof resolvedSearchParams.state === "string"
    ? resolvedSearchParams.state
    : undefined;

  return <WorkbenchPreview canonicalState={canonicalState} />;
}
