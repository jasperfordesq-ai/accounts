# Choose a FilingBridge deployment mode

FilingBridge has three deliberately separate operating modes. Choose the guide from the
exposure and operator model, not from which command happens to start successfully.

| Question | Use |
| --- | --- |
| Are you changing source code, running tests, or exploring seeded sample data? | [Development](../../LOCAL_SETUP.md) |
| Is one person or small organisation running a compiled copy on a trusted computer, with optional access only through Tailscale Serve? | [Private Server](private-server.md) |
| Is the service internet-reachable or operated for unrelated organisations? | [Public Production](public-production.md) |

## Supported-mode contract

| Property | Development | Private Server | Public Production |
| --- | --- | --- | --- |
| Runtime | Development or compiled | Compiled, production-optimised | Exact promoted production images |
| Data expectation | Disposable | Durable named volume | Durable managed storage |
| Exposure | Contributor localhost | Exact loopback and optional tailnet HTTPS | Approved public HTTPS ingress |
| API/database host ports | Allowed for development | Forbidden | Forbidden |
| Demo users/data | Allowed and labelled | Forbidden | Forbidden |
| External providers | Optional | Optional and off by default | Required by the public contract |
| PostgreSQL transport | Development policy | Explicit plaintext on an isolated internal bridge | Certificate-verified TLS |
| Backup | May be disposable | Verified recovery workflow | Encrypted off-host evidence and drills |
| Filing boundary | No direct CRO/ROS submission | No direct CRO/ROS submission | No direct CRO/ROS submission |

Private Server and Public Production both run production builds. Private Server is not
Development with a different password, and it is not Public Production with arbitrary checks
disabled. Only the exact `PrivateServer` deployment marker selects its bounded policy.

## Availability and threat boundaries

Private Server trusts the Windows host, its Docker administrator, and the installation owner. It
does not defend against a hostile host administrator. The machine must be powered, awake, signed
in far enough for Docker Desktop to run, and connected to Tailscale for remote access. It is
appropriate for low-traffic use by a small board, not high availability.

Tailscale is a network boundary, not FilingBridge authorization. Every trustee, accountant or
reviewer needs an individual application account and an existing FilingBridge role. Never enable
Tailscale Funnel, router port forwarding, a public firewall rule, or host ports for Kestrel or
PostgreSQL.

## Moving between modes

A Private Server database uses the same PostgreSQL schema, migrations, tenant model and RLS
contract as Public Production. Migration to Public Production is therefore a controlled backup,
restore, key, provider, ingress and acceptance exercise—not an export to a different product.
Public operation still requires its own monitoring, database TLS, encrypted off-host recovery,
release evidence and named human/external acceptance.

## Filing and professional boundary

Operational success in any mode does not establish legal or accounting correctness. FilingBridge
does not directly submit to CRO or ROS. Real filing reliance remains blocked until the exact
artifacts receive all required source-law, external validation, auditor/manual-handoff and named
qualified-accountant acceptance recorded by the live platform audit.
