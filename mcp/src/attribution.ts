/**
 * FilingBridge's canonical attribution, as the web application states it.
 *
 * Mirrors frontend/src/lib/attribution.ts; attribution.test.ts reads that
 * file and fails if the two ever differ. The connector is part of the same
 * AGPL-3.0-or-later work and says so in the same words.
 */
export const REPOSITORY_URL = 'https://github.com/jasperfordesq-ai/accounts';
export const PRODUCT_NAME = 'FilingBridge';
export const CREATOR_NAME = 'Jasper Ford';
export const CONTRIBUTORS = [CREATOR_NAME, 'hOUR Timebank CLG (Ireland)'] as const;
export const ORGANISATIONAL_CONTRIBUTORS = ['hOUR Timebank CLG (Ireland)'] as const;
export const LICENSE_NAME = 'AGPL v3-or-later';
export const COPYRIGHT_NOTICE = 'Copyright (c) 2026 Jasper Ford.';
export const FOOTER_ATTRIBUTION = `Built on ${PRODUCT_NAME} by ${CREATOR_NAME} with ${ORGANISATIONAL_CONTRIBUTORS.join(', ')}`;
export const ABOUT_ATTRIBUTION_LINES = [
  `Powered by ${PRODUCT_NAME}`,
  `Created by ${CREATOR_NAME}`,
  `Contributor: ${ORGANISATIONAL_CONTRIBUTORS.join(', ')}`,
  `Licensed under ${LICENSE_NAME}`,
  `Source: ${REPOSITORY_URL}`,
];
