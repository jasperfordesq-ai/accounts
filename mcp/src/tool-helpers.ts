import type { ToolDefinition } from './tools.js';
import type { FieldSpec } from './tool-body.js';
import type { ModelName } from './field-policy.js';
import type { ToolGroup } from './toolsets.js';

/** The two route roots almost everything hangs from. */
export const CO = '/api/companies/{companyId}';
export const PER = `${CO}/periods/{periodId}`;

/** Why most payloads need no gate. Each is a claim a reviewer can check. */
export const NR = {
  figures: 'Accounting figures, categories and statuses of one company; no records about individuals.',
  statements: 'Financial statements computed from the company\'s ledger; no records about individuals.',
  workflow: 'Workflow state, readiness checks, dates and references; no records about individuals.',
  reference: 'Reference data and configuration; no records about individuals.',
  deadlines: 'Statutory deadlines and their status; no records about individuals.',
  operations: 'Operational counters and queue state; no records about individuals.',
  officerNames:
    'Officer names and roles are on the public CRO register; addresses and identity evidence are not part of this payload.',
  ack: 'An acknowledgement of the change; no records about individuals.',
} as const;

export const REPLACES =
  ' Replaces the whole record: read it first and send every field you want to keep, because an omitted field is cleared or reset.';

export const APPROVAL_NOTE =
  ' FilingBridge asks the person to approve this exact request (password, and an authenticator code if two-factor sign-in is on) before it happens;'
  + 'see the approval flow in the server instructions.';

interface CrudOptions {
  /** snake_case entity, such as debtor. */
  entity: string;
  /** Human noun, such as "debtor". */
  noun: string;
  group: ToolGroup;
  /** The collection path, such as `${PER}/debtors`. */
  collection: string;
  body: readonly FieldSpec[];
  bodySchema: string;
  model?: ModelName;
  noRecordsBecause?: string;
  listDescription: string;
  createDescription: string;
  updateDescription?: string;
  deleteDescription: string;
  /** The PUT would erase personal fields a Withheld session cannot see. */
  updateNeedsFullScope?: string;
  /** Omit the update tool (the route has none). */
  noUpdate?: boolean;
}

function gate(options: CrudOptions): Pick<ToolDefinition, 'model' | 'noRecordsBecause'> {
  return options.model ? { model: options.model } : { noRecordsBecause: options.noRecordsBecause ?? NR.figures };
}

/** list, create, update and delete for one year-end record type. */
export function crud(options: CrudOptions): ToolDefinition[] {
  const item = `${options.collection}/{id}`;
  const tools: ToolDefinition[] = [
    {
      name: `${options.entity}_list`,
      group: options.group,
      description: options.listDescription,
      path: options.collection,
      ...gate(options),
    },
    {
      name: `${options.entity}_create`,
      group: options.group,
      description: options.createDescription,
      method: 'POST',
      path: options.collection,
      level: 'write',
      body: options.body,
      bodySchema: options.bodySchema,
      ...gate(options),
    },
  ];
  if (!options.noUpdate) {
    tools.push({
      name: `${options.entity}_update`,
      group: options.group,
      description: `${options.updateDescription ?? `Change a ${options.noun}.`}${REPLACES}`,
      method: 'PUT',
      path: item,
      level: 'write',
      body: options.body,
      bodySchema: options.bodySchema,
      ...gate(options),
      ...(options.updateNeedsFullScope ? { requiresFullScope: options.updateNeedsFullScope } : {}),
    });
  }
  tools.push({
    name: `${options.entity}_delete`,
    group: options.group,
    description: `${options.deleteDescription}${APPROVAL_NOTE}`,
    method: 'DELETE',
    path: item,
    level: 'admin',
    approval: true,
    noRecordsBecause: NR.ack,
  });
  return tools;
}
