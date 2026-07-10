function networkNames(service) {
  if (Array.isArray(service?.networks)) return service.networks;
  return Object.keys(service?.networks ?? {});
}

function sorted(values) {
  return [...values].sort().join("|");
}

export function evaluateContainerHardening(config) {
  const failures = [];
  const services = config?.services ?? {};
  const requiredNetworks = {
    db: ["api_db"],
    migrate: ["api_db"],
    api: ["api_db", "api_egress", "frontend_api"],
    frontend: ["frontend_api"],
  };

  for (const [name, expectedNetworks] of Object.entries(requiredNetworks)) {
    const service = services[name];
    if (!service) {
      failures.push(`Missing service: ${name}.`);
      continue;
    }
    if (service.read_only !== true) failures.push(`${name} root filesystem is not read-only.`);
    if (!(service.security_opt ?? []).includes("no-new-privileges:true")) {
      failures.push(`${name} does not enable no-new-privileges.`);
    }
    if (!(service.cap_drop ?? []).includes("ALL")) failures.push(`${name} does not drop all capabilities.`);
    if (service.privileged === true) failures.push(`${name} is privileged.`);
    if (!(Number(service.pids_limit) > 0)) failures.push(`${name} has no positive PID limit.`);
    if (!(Number(service.mem_limit) > 0)) failures.push(`${name} has no positive memory limit.`);
    if (!(Number(service.cpus) > 0)) failures.push(`${name} has no positive CPU limit.`);
    const tmpfs = service.tmpfs ?? [];
    if (tmpfs.length === 0 || tmpfs.some((mount) => !String(mount).includes("size="))) {
      failures.push(`${name} has no bounded writable tmpfs.`);
    }
    if (sorted(networkNames(service)) !== sorted(expectedNetworks)) {
      failures.push(`${name} has unintended network reachability.`);
    }
  }

  if (config?.networks?.frontend_api?.internal !== true) {
    failures.push("frontend_api must be internal.");
  }
  if (config?.networks?.api_db?.internal !== true) failures.push("api_db must be internal.");
  if (config?.networks?.api_egress?.internal === true) failures.push("api_egress cannot provide controlled egress.");
  if ((services.db?.ports ?? []).length > 0) failures.push("Database publishes a host port.");
  if ((services.api?.ports ?? []).length > 0) failures.push("API publishes a host port.");
  const frontendPorts = services.frontend?.ports ?? [];
  if (frontendPorts.length !== 1 || frontendPorts[0]?.host_ip !== "127.0.0.1") {
    failures.push("Frontend port is not loopback-only.");
  }

  return failures;
}
