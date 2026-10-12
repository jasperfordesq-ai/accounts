"use client";

import { Button, Card, Chip } from "@heroui/react";
import { toast } from "sonner";
import { fetchDocumentBlob } from "@/lib/api";
import {
  ARCHIVED_HISTORIC_FIGURES_TITLE,
  archivedHistoricFiguresPdfUrl,
  type ArchivedHistoricFigures,
} from "@/lib/historicSizeFigures";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(amount);
}

function formatDate(value: string): string {
  return new Date(value).toLocaleDateString("en-IE");
}

/**
 * Historic figures that were retired or replaced. They are kept as evidence (append-only), stay
 * visible after the period is classified again, and their PDF can be downloaded.
 */
export function ArchivedHistoricFiguresCard({
  companyId,
  periodId,
  records,
}: {
  companyId: number;
  periodId: number;
  records: ArchivedHistoricFigures[];
}) {
  if (records.length === 0) return null;

  async function download(record: ArchivedHistoricFigures) {
    try {
      const blob = await fetchDocumentBlob(archivedHistoricFiguresPdfUrl(companyId, periodId, record.id));
      const objectUrl = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = objectUrl;
      anchor.download = `historic-figures-period-${periodId}-${record.id}.pdf`;
      anchor.click();
      URL.revokeObjectURL(objectUrl);
    } catch (failure) {
      toast.error(failure instanceof Error ? failure.message : "The PDF could not be downloaded");
    }
  }

  return (
    <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 mb-6">
      <Card.Header>
        <Card.Title>{ARCHIVED_HISTORIC_FIGURES_TITLE}</Card.Title>
        <Card.Description>
          Kept as evidence only. These figures are not this period&apos;s classification and are not used for any size test.
        </Card.Description>
      </Card.Header>
      <Card.Content>
        <ul className="space-y-4">
          {records.map((record) => (
            <li key={record.id} className="space-y-2 rounded-lg border border-gray-200 p-3 text-sm text-gray-700 dark:border-neutral-700 dark:text-gray-300">
              <div className="flex flex-wrap items-center gap-2">
                <Chip color="default" variant="soft" size="sm">{record.event === "Retired" ? "Retired" : "Replaced"}</Chip>
                <span>
                  {record.event === "Retired" && record.retiredBy && record.retiredAt
                    ? `Retired by ${record.retiredBy} on ${formatDate(record.retiredAt)}`
                    : `Replaced by new figures recorded by ${record.archivedBy} on ${formatDate(record.archivedAt)}`}
                </span>
              </div>
              {record.retiredReason && <p>Reason: {record.retiredReason}</p>}
              <p>
                Source: {record.sourceReference} (recorded by {record.attestedBy}, {record.attestedRole}, on {formatDate(record.attestedAt)}).
                Turnover {formatCurrency(record.turnover)}, balance sheet {formatCurrency(record.balanceSheetTotal)},{" "}
                {record.avgEmployees} average employees
                {record.attestedEffectiveClass ? `; attested class ${record.attestedEffectiveClass}` : ""}.
              </p>
              <Button
                size="sm"
                variant="outline"
                aria-label={`Download the PDF of the ${record.event === "Retired" ? "retired" : "replaced"} figures recorded on ${formatDate(record.attestedAt)}`}
                onPress={() => void download(record)}
              >
                Download PDF
              </Button>
            </li>
          ))}
        </ul>
      </Card.Content>
    </Card>
  );
}
