import actionCatalog from "./permission-action-catalog.json" with { type: "json" };

export type PlatformRole = "Owner" | "Accountant" | "Reviewer" | "Client";

export interface RolePermissions {
  canRead: boolean;
  canCreateCompany: boolean;
  canDeleteCompany: boolean;
  canManageUsers: boolean;
  canWriteWorkingPapers: boolean;
  canReadInternalWorkingPapers: boolean;
  canReview: boolean;
  canApprove: boolean;
  canReviewReleaseEvidence: boolean;
}

export type PermissionCapability = keyof RolePermissions;

export interface AuthenticatedRoutePolicy {
  id: string;
  pathTemplate: string;
  requiredPermission: PermissionCapability;
  matches: (pathname: string) => boolean;
}

export interface PermissionActionDefinition {
  id: string;
  routeId: string;
  requiredPermission: PermissionCapability;
  method: "GET" | "POST" | "PUT" | "DELETE" | "LOCAL";
  path: string;
}

export const permissionActionCatalog = actionCatalog as readonly PermissionActionDefinition[];

const readOnly: RolePermissions = {
  canRead: true,
  canCreateCompany: false,
  canDeleteCompany: false,
  canManageUsers: false,
  canWriteWorkingPapers: false,
  canReadInternalWorkingPapers: false,
  canReview: false,
  canApprove: false,
  canReviewReleaseEvidence: false,
};

const permissionMatrix: Record<PlatformRole, RolePermissions> = {
  Owner: {
    canRead: true,
    canCreateCompany: true,
    canDeleteCompany: true,
    canManageUsers: true,
    canWriteWorkingPapers: true,
    canReadInternalWorkingPapers: true,
    canReview: true,
    canApprove: true,
    canReviewReleaseEvidence: true,
  },
  Accountant: {
    ...readOnly,
    canWriteWorkingPapers: true,
    canReadInternalWorkingPapers: true,
  },
  Reviewer: {
    ...readOnly,
    canReview: true,
    canApprove: true,
    canReviewReleaseEvidence: true,
    canReadInternalWorkingPapers: true,
  },
  Client: readOnly,
};

const numericId = "[1-9][0-9]*";
const periodRoot = new RegExp(`^/companies/${numericId}/periods/${numericId}$`);
const periodChild = new RegExp(
  `^/companies/${numericId}/periods/${numericId}/(?:classify|year-end|statements|working-papers|notes|charity)$`,
);

/**
 * User-facing authenticated routes. Backend GET/HEAD access is read-only for every firm role;
 * company creation is the sole Owner-only deep link. Keep this list exhaustive so route additions
 * must make an explicit permission decision in the regression suite.
 */
export const authenticatedRoutePolicies: readonly AuthenticatedRoutePolicy[] = [
  {
    id: "dashboard",
    pathTemplate: "/",
    requiredPermission: "canRead",
    matches: (pathname) => pathname === "/",
  },
  {
    id: "new-company",
    pathTemplate: "/companies/new",
    requiredPermission: "canCreateCompany",
    matches: (pathname) => pathname === "/companies/new",
  },
  {
    id: "company-detail",
    pathTemplate: "/companies/:companyId",
    requiredPermission: "canRead",
    matches: (pathname) => new RegExp(`^/companies/${numericId}$`).test(pathname),
  },
  {
    id: "period-workspace",
    pathTemplate: "/companies/:companyId/periods/:periodId",
    requiredPermission: "canRead",
    matches: (pathname) => periodRoot.test(pathname),
  },
  {
    id: "classification",
    pathTemplate: "/companies/:companyId/periods/:periodId/classify",
    requiredPermission: "canRead",
    matches: (pathname) => periodChild.test(pathname) && pathname.endsWith("/classify"),
  },
  {
    id: "year-end",
    pathTemplate: "/companies/:companyId/periods/:periodId/year-end",
    requiredPermission: "canRead",
    matches: (pathname) => periodChild.test(pathname) && pathname.endsWith("/year-end"),
  },
  {
    id: "statements",
    pathTemplate: "/companies/:companyId/periods/:periodId/statements",
    requiredPermission: "canRead",
    matches: (pathname) => periodChild.test(pathname) && pathname.endsWith("/statements"),
  },
  {
    id: "working-papers",
    pathTemplate: "/companies/:companyId/periods/:periodId/working-papers",
    requiredPermission: "canReadInternalWorkingPapers",
    matches: (pathname) => periodChild.test(pathname) && pathname.endsWith("/working-papers"),
  },
  {
    id: "notes",
    pathTemplate: "/companies/:companyId/periods/:periodId/notes",
    requiredPermission: "canRead",
    matches: (pathname) => periodChild.test(pathname) && pathname.endsWith("/notes"),
  },
  {
    id: "charity",
    pathTemplate: "/companies/:companyId/periods/:periodId/charity",
    requiredPermission: "canRead",
    matches: (pathname) => periodChild.test(pathname) && pathname.endsWith("/charity"),
  },
  {
    id: "production-readiness",
    pathTemplate: "/production-readiness",
    requiredPermission: "canReviewReleaseEvidence",
    matches: (pathname) => pathname === "/production-readiness",
  },
  {
    id: "user-administration",
    pathTemplate: "/settings/users",
    requiredPermission: "canManageUsers",
    matches: (pathname) => pathname === "/settings/users",
  },
  {
    id: "workbench-preview",
    pathTemplate: "/workbench-preview",
    requiredPermission: "canRead",
    matches: (pathname) => pathname === "/workbench-preview",
  },
  {
    id: "change-password",
    pathTemplate: "/change-password",
    requiredPermission: "canRead",
    matches: (pathname) => pathname === "/change-password",
  },
] as const;

export function permissionsForRole(role: string | null | undefined): RolePermissions {
  const normalised = role?.trim().toLowerCase();
  const knownRole = (Object.keys(permissionMatrix) as PlatformRole[])
    .find((candidate) => candidate.toLowerCase() === normalised);

  return knownRole ? permissionMatrix[knownRole] : { ...readOnly };
}

export function routePolicyForPath(pathname: string): AuthenticatedRoutePolicy | undefined {
  const normalised = pathname.length > 1 ? pathname.replace(/\/+$/, "") : pathname;
  return authenticatedRoutePolicies.find((policy) => policy.matches(normalised));
}

export function canAccessRoute(role: string | null | undefined, pathname: string): boolean {
  const policy = routePolicyForPath(pathname);
  if (!policy) return permissionsForRole(role).canRead;
  return permissionsForRole(role)[policy.requiredPermission];
}

export function canPerformAction(role: string | null | undefined, actionId: string): boolean {
  const action = permissionActionCatalog.find((candidate) => candidate.id === actionId);
  if (!action) return false;
  return permissionsForRole(role)[action.requiredPermission];
}
