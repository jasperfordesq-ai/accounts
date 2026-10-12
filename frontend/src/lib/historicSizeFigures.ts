import { z } from "zod";
import { getApiJson, postApiJson, putApiJson, type SizeClassification } from "./api.ts";
import { companySizeSchema, isoDateTimeSchema, sizeClassificationSchema } from "./apiContracts.ts";
import { assertEvidenceUploadFits, readEvidenceFile } from "./evidenceUpload.ts";

// Historic figures keyed in from a preceding period's filed accounts (Companies Act 2014, s.280A).
// They root the next period's size test; the system did not compute them, and the period they are
// recorded on is never classified or filed from them.

export const HISTORIC_FIGURES_BADGE = "Historic figures (from filed accounts)";
export const HISTORIC_FIGURES_PROVENANCE =
  "These figures are keyed in from accounts filed elsewhere; the system did not compute them.";

/** The provenance fields the API returns for a historic-figures classification. */
export type HistoricSizeClassification = SizeClassification & {
  historicSourceReference?: string;
  historicAttestedBy?: string;
  historicAttestedRole?: string;
  historicAttestedEffectiveClass?: string;
  historicRetiredAt?: string;
  historicRetiredBy?: string;
  historicRetiredReason?: string;
};

export function isHistoricFigures(classification: SizeClassification | null | undefined): classification is HistoricSizeClassification {
  return classification?.inputBasis === "HistoricAnchor";
}

/** Historic figures an Owner retired: kept as evidence, never a classification or a chain root. */
export function isRetiredHistoricFigures(classification: SizeClassification | null | undefined): classification is HistoricSizeClassification {
  return classification?.inputBasis === "RetiredHistoricAnchor";
}

/** The label to show for a period's size: never a computed class for keyed-in historic figures. */
export function sizeClassificationLabel(classification: SizeClassification | null | undefined, fallback: string): string {
  return isHistoricFigures(classification) ? HISTORIC_FIGURES_BADGE : classification?.calculatedClass ?? fallback;
}

export interface HistoricSizeFiguresInput {
  turnover: number;
  balanceSheetTotal: number;
  avgEmployees: number;
  thresholdElectionEffectiveFrom: "2023-01-01" | "2024-01-01";
  sourceReference: string;
  evidence: File;
  /** The size class the filed accounts relied on; null means none is attested. */
  attestedEffectiveClass: "Micro" | "Small" | "Medium" | "Large" | null;
}

const PDF_SIGNATURE = [0x25, 0x50, 0x44, 0x46, 0x2d]; // "%PDF-"

/** Records (or replaces) the keyed-in figures with the PDF of the filed accounts as evidence. */
export async function recordHistoricSizeFigures(companyId: number, periodId: number, input: HistoricSizeFiguresInput) {
  assertEvidenceUploadFits([input.evidence]);
  const header = new Uint8Array(await input.evidence.slice(0, PDF_SIGNATURE.length).arrayBuffer());
  if (header.length < PDF_SIGNATURE.length || PDF_SIGNATURE.some((byte, index) => header[index] !== byte)) {
    throw new Error("Choose a PDF copy of the filed accounts.");
  }
  const evidence = await readEvidenceFile(input.evidence);
  return putApiJson<HistoricSizeClassification>(
    `/api/companies/${companyId}/periods/${periodId}/classification/historic-anchor`,
    {
      turnover: input.turnover,
      balanceSheetTotal: input.balanceSheetTotal,
      avgEmployees: input.avgEmployees,
      thresholdElectionEffectiveFrom: input.thresholdElectionEffectiveFrom,
      sourceReference: input.sourceReference,
      evidenceArtifact: evidence.base64,
      evidenceSha256: evidence.sha256,
      attestedEffectiveClass: input.attestedEffectiveClass,
    },
    sizeClassificationSchema,
    "historic size figures",
  );
}

/**
 * Retires the keyed-in figures (Owner only, with recent verification). They are kept as evidence; the
 * following periods' classifications become out of date and this period is classified normally again.
 */
export async function retireHistoricSizeFigures(companyId: number, periodId: number, reason: string) {
  return postApiJson<HistoricSizeClassification>(
    `/api/companies/${companyId}/periods/${periodId}/classification/historic-anchor/retire`,
    { reason },
    sizeClassificationSchema,
    "retired historic size figures",
  );
}

/** Archived historic evidence: kept, append-only, whenever figures are retired or replaced. */
export const ARCHIVED_HISTORIC_FIGURES_TITLE = "Earlier historic figures (retired or replaced)";

const archivedHistoricFiguresSchema = z.array(z.object({
  id: z.number().int().positive(),
  event: z.enum(["Retired", "Replaced"]),
  archivedAt: isoDateTimeSchema,
  archivedBy: z.string().min(1),
  turnover: z.number().finite().nonnegative(),
  balanceSheetTotal: z.number().finite().nonnegative(),
  avgEmployees: z.number().int().nonnegative(),
  sourceReference: z.string().min(1),
  evidenceSha256: z.string().regex(/^[a-f0-9]{64}$/i),
  attestedBy: z.string().min(1),
  attestedRole: z.string().min(1),
  attestedAt: isoDateTimeSchema,
  attestedEffectiveClass: companySizeSchema.nullable(),
  retiredAt: isoDateTimeSchema.nullable(),
  retiredBy: z.string().nullable(),
  retiredReason: z.string().nullable(),
}).superRefine((record, context) => {
  if (record.event === "Retired" && (!record.retiredAt || !record.retiredBy || !record.retiredReason)) {
    context.addIssue({ code: "custom", path: ["retiredReason"], message: "Retired figures must record who retired them, when and why" });
  }
}));

export type ArchivedHistoricFigures = z.infer<typeof archivedHistoricFiguresSchema>[number];

/** Owner, Reviewer and Accountant: the period's archived historic evidence, newest first (no PDF bytes). */
export function getArchivedHistoricFigures(companyId: number, periodId: number) {
  return getApiJson<ArchivedHistoricFigures[]>(
    `/api/companies/${companyId}/periods/${periodId}/classification/historic-anchor/archive`,
    archivedHistoricFiguresSchema,
    "archived historic size figures",
  );
}

/** The PDF of one archived record (downloaded with fetchDocumentBlob). */
export const archivedHistoricFiguresPdfUrl = (companyId: number, periodId: number, recordId: number) =>
  `/api/companies/${companyId}/periods/${periodId}/classification/historic-anchor/archive/${recordId}/evidence`;
