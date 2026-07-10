export type ResourceStatus =
  | "loading"
  | "loaded"
  | "empty"
  | "partial-error"
  | "error"
  | "stale/retrying";

export interface ResourceState {
  status: ResourceStatus;
  error: string | null;
  failedResourceKeys: string[];
  hasRetainedData: boolean;
}

export const INITIAL_RESOURCE_STATE: ResourceState = {
  status: "loading",
  error: null,
  failedResourceKeys: [],
  hasRetainedData: false,
};

type ResourceLoaders = Record<string, () => Promise<unknown>>;

export interface ResourceGroupResult<TLoaders extends ResourceLoaders> {
  values: Partial<{ [TKey in keyof TLoaders]: Awaited<ReturnType<TLoaders[TKey]>> }>;
  errors: Partial<Record<keyof TLoaders, string>>;
  failedResourceKeys: Array<Extract<keyof TLoaders, string>>;
}

export async function loadResourceGroup<TLoaders extends ResourceLoaders>(
  loaders: TLoaders,
  resourceKeys: Array<Extract<keyof TLoaders, string>> = Object.keys(loaders) as Array<Extract<keyof TLoaders, string>>,
): Promise<ResourceGroupResult<TLoaders>> {
  const values: ResourceGroupResult<TLoaders>["values"] = {};
  const errors: ResourceGroupResult<TLoaders>["errors"] = {};

  await Promise.all(resourceKeys.map(async (key) => {
    try {
      values[key] = await loaders[key]() as ResourceGroupResult<TLoaders>["values"][typeof key];
    } catch (error) {
      errors[key] = error instanceof Error ? error.message : `Failed to load ${key}`;
    }
  }));

  return {
    values,
    errors,
    failedResourceKeys: resourceKeys.filter((key) => errors[key] != null),
  };
}

export function beginResourceLoad(previous: ResourceState, hasRetainedData: boolean): ResourceState {
  return {
    status: hasRetainedData ? "stale/retrying" : "loading",
    error: previous.error,
    failedResourceKeys: previous.failedResourceKeys,
    hasRetainedData,
  };
}

export function completeResourceLoad(isEmpty: boolean): ResourceState {
  return {
    status: isEmpty ? "empty" : "loaded",
    error: null,
    failedResourceKeys: [],
    hasRetainedData: !isEmpty,
  };
}

export function failResourceLoad(
  failures: { failedResourceKeys: string[]; errors: Record<string, string | undefined> },
  hasRetainedData: boolean,
): ResourceState {
  const messages = failures.failedResourceKeys
    .map((key) => failures.errors[key])
    .filter((message): message is string => Boolean(message));
  return {
    status: hasRetainedData ? "partial-error" : "error",
    error: messages.join(" ") || "Required data could not be loaded.",
    failedResourceKeys: [...failures.failedResourceKeys],
    hasRetainedData,
  };
}

export function partialResourceLoad(
  failedResourceKeys: string[],
  errors: Record<string, string | undefined>,
  hasRetainedData: boolean,
): ResourceState {
  return failResourceLoad({ failedResourceKeys, errors }, hasRetainedData);
}

export function canUseResourceAsEvidence(state: ResourceState): boolean {
  return state.status === "loaded" || state.status === "empty";
}

export function shouldRenderResourceEmpty(state: ResourceState): boolean {
  return state.status === "empty";
}
