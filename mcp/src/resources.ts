import { ConnectorError } from './errors.js';
import { REFERENCE_SCHEME } from './references.js';
import { ABOUT_ATTRIBUTION_LINES, CONTRIBUTORS, COPYRIGHT_NOTICE, CREATOR_NAME, LICENSE_NAME, PRODUCT_NAME, REPOSITORY_URL } from './attribution.js';
import { LEGAL_FRAMEWORK } from './legal-framework.js';
import { FILING_BOUNDARY_SENTENCE } from './instructions.js';
import { CONNECTOR_VERSION } from './version.js';

/**
 * Context a client can attach without being asked.
 *
 * Three fixed resources: who made FilingBridge and on what licence, the legal
 * framework it applies, and the filing boundary. Records are offered through
 * templates, and reading one goes through the server's dispatch, so it meets
 * the level check, the toolset check and the personal-data gate exactly as
 * the matching tool would. A resource that skipped them would be a way round.
 */
export interface ResourceDescriptor {
  uri: string;
  name: string;
  title: string;
  description: string;
  mimeType: string;
}

export const ABOUT_URI = `${REFERENCE_SCHEME}about`;
export const LEGAL_FRAMEWORK_URI = `${REFERENCE_SCHEME}legal-framework`;
export const FILING_BOUNDARY_URI = `${REFERENCE_SCHEME}filing-boundary`;

export const ABOUT = {
  product: PRODUCT_NAME,
  connectorVersion: CONNECTOR_VERSION,
  creator: CREATOR_NAME,
  contributors: [...CONTRIBUTORS],
  licence: LICENSE_NAME,
  spdx: 'AGPL-3.0-or-later',
  copyright: COPYRIGHT_NOTICE,
  source: REPOSITORY_URL,
  attribution: [...ABOUT_ATTRIBUTION_LINES],
};

export const FILING_BOUNDARY = {
  statement: FILING_BOUNDARY_SENTENCE,
  meaning: [
    'FilingBridge prepares the financial statements, the CRO filing pack and signature page, the iXBRL file and the CT1 support; it never transmits them.',
    'The person files the B1 and financial statements in CRO CORE, the CT1 and iXBRL in ROS, and the charity annual return with the Charities Regulator, themselves.',
    'FilingBridge then records what happened (filed, paid, accepted, sent back or rejected) with the external reference and evidence, and its release gate checks those records.',
    'No tool in this connector, and no route in FilingBridge, submits anything to the CRO or to Revenue.',
  ],
};

export const RESOURCES: readonly ResourceDescriptor[] = [
  {
    uri: ABOUT_URI,
    name: 'about',
    title: 'About FilingBridge',
    description: 'Product, creator, contributors, licence (AGPL-3.0-or-later) and source repository.',
    mimeType: 'application/json',
  },
  {
    uri: LEGAL_FRAMEWORK_URI,
    name: 'legal-framework',
    title: 'Irish legal framework',
    description: 'Companies Act 2014 size thresholds and test, micro exclusions, filing regimes, audit exemption and the tax rates FilingBridge applies.',
    mimeType: 'application/json',
  },
  {
    uri: FILING_BOUNDARY_URI,
    name: 'filing-boundary',
    title: 'Filing boundary',
    description: 'What FilingBridge prepares and records, and what the person files themselves with the CRO and Revenue.',
    mimeType: 'application/json',
  },
];

export const RESOURCE_TEMPLATES = [
  {
    uriTemplate: `${REFERENCE_SCHEME}company/{companyId}`,
    name: 'company',
    title: 'A company',
    description: 'One company with its officers and periods, as company_get returns it.',
    mimeType: 'application/json',
  },
  {
    uriTemplate: `${REFERENCE_SCHEME}company/{companyId}/period/{periodId}`,
    name: 'period',
    title: 'An accounting period',
    description: 'One accounting period with its status, size classification and regime, as period_get returns it.',
    mimeType: 'application/json',
  },
  {
    uriTemplate: `${REFERENCE_SCHEME}company/{companyId}/period/{periodId}/readiness`,
    name: 'readiness',
    title: 'Filing readiness',
    description: 'The period\'s completeness and filing-readiness score, as statement_readiness returns it.',
    mimeType: 'application/json',
  },
  {
    uriTemplate: `${REFERENCE_SCHEME}company/{companyId}/period/{periodId}/filing-status`,
    name: 'filing-status',
    title: 'Filing status',
    description: 'The period\'s CRO, Revenue and charity filing workflow, as filing_status returns it.',
    mimeType: 'application/json',
  },
];

const TEMPLATE_PATTERNS: { pattern: RegExp; tool: string; args: (m: RegExpExecArray) => Record<string, unknown> }[] = [
  {
    pattern: /^filingbridge:\/\/company\/([1-9]\d{0,14})\/period\/([1-9]\d{0,14})\/readiness$/,
    tool: 'statement_readiness',
    args: (m) => ({ companyId: Number(m[1]), periodId: Number(m[2]) }),
  },
  {
    pattern: /^filingbridge:\/\/company\/([1-9]\d{0,14})\/period\/([1-9]\d{0,14})\/filing-status$/,
    tool: 'filing_status',
    args: (m) => ({ companyId: Number(m[1]), periodId: Number(m[2]) }),
  },
  {
    pattern: /^filingbridge:\/\/company\/([1-9]\d{0,14})\/period\/([1-9]\d{0,14})$/,
    tool: 'period_get',
    args: (m) => ({ companyId: Number(m[1]), id: Number(m[2]) }),
  },
  {
    pattern: /^filingbridge:\/\/company\/([1-9]\d{0,14})$/,
    tool: 'company_get',
    args: (m) => ({ id: Number(m[1]) }),
  },
];

const FIXED: Record<string, unknown> = {
  [ABOUT_URI]: ABOUT,
  [LEGAL_FRAMEWORK_URI]: LEGAL_FRAMEWORK,
  [FILING_BOUNDARY_URI]: FILING_BOUNDARY,
};

export interface ResourceContents {
  uri: string;
  mimeType: string;
  text: string;
}

/**
 * Reads one resource. A record is handed to `run`, which the server wires to
 * its own dispatch (tools by name, and fetch for filingbridge:// references
 * search returned).
 */
export async function readResource(
  uri: string,
  run: (tool: string, args: Record<string, unknown>) => Promise<unknown>,
): Promise<ResourceContents> {
  const fixed = FIXED[uri];
  if (fixed !== undefined) {
    return { uri, mimeType: 'application/json', text: JSON.stringify(fixed, null, 2) };
  }
  for (const template of TEMPLATE_PATTERNS) {
    const match = template.pattern.exec(uri);
    if (match) {
      const value = await run(template.tool, template.args(match));
      return { uri, mimeType: 'application/json', text: JSON.stringify(value, null, 2) };
    }
  }
  if (uri.startsWith(REFERENCE_SCHEME)) {
    const value = await run('fetch', { id: uri });
    return { uri, mimeType: 'application/json', text: JSON.stringify(value, null, 2) };
  }
  throw new ConnectorError('RESOURCE_UNKNOWN', `"${uri}" is not a FilingBridge resource. List the resources to see what there is.`);
}
