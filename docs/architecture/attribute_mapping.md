# Attribute Mapping — Raw Connector Data → Staging Tables

> Companion to `ingestion_workflow.md` and ADR §12 (Ingestion & CDC). That doc explains
> the pipeline shape; this one is the field-by-field reference for what each connector's
> raw export feeds into `staging_account` / `staging_grp` / `staging_asset` /
> `staging_entitlement`.
>
> Reconstructed from `backend/workflows/Thor.Workflows.Ingestion/AttributeMapping/Config/*.json`
> and the three normalizers (`AdNormalizer.cs`, `CyberArkNormalizer.cs`,
> `WindowsNormalizer.cs`) — treat it as a current-behavior reference, not a spec; if either
> drifts from this doc, the code wins and this doc should be updated.

## 1. How mapping works

Two mechanisms feed a staging row, per connector + entity kind:

1. **Config-driven "straight rename" columns** — `IAttributeMapProvider` loads one JSON
   file per (connector type, entity kind) from `AttributeMapping/Config/*.json`. Each
   file's `columnMappings` says which raw attribute name feeds a given staging column
   verbatim (AD additionally unwraps LDAP's single-valued-attribute array convention on
   the way through). A staging column not listed in `columnMappings` is never sourced
   from raw data by this mechanism.
2. **Hardcoded transform/constant fields** — anything that needs real logic (type
   decoding, boolean derivation, edge-ref construction, a value with no raw-data
   equivalent for a given connector) is computed directly in the normalizer and
   documented below.

A raw attribute the column map didn't consume is **not dropped**: it flows into the
staging row's `raw_attributes` JSON column via `UnmappedAttributeCollector`.
`raw_attributes` also always carries the entity's `AliasKeys`/`EdgeRefs`
deferred-reference contract — every entity kind (AD included) uses the shared
`RawAttributesWithEdges` record for this.

One connector type can now emit **four** entity kinds — `ParsedAccount`, `ParsedGroup`,
`ParsedAsset` (a governed resource: currently only CyberArk's Safes), and
`ParsedEntitlement`. `content_hash` (CDC change detection, ADR §12) is computed from a
connector-and-entity-kind-specific subset of fields — listed per entity below — not from
every staged column.

**The entitlement pipeline is currently dormant.** `staging_entitlement`/
`tenant.entitlement`, `ParsedEntitlement`, `Stager`'s and `Promoter`'s entitlement paths
are all fully wired, but no connector actually produces `ParsedEntitlement` rows today —
CyberArk was the only entitlement producer and it moved safe-permission grants to
edge properties instead (see §3.3/§3.4). The path is left in place for a future connector.

Config files: `ad-account.json`, `ad-group.json`, `cyberark-account.json`,
`cyberark-safe.json`, `cyberark-member-account.json`, `cyberark-member-group.json`,
`windows-account.json`, `windows-group.json`.

## 2. Active Directory — connector_type `308`

`AdNormalizer.cs`. One raw record stream (`Accounts[]`, mixed users/computers/groups)
split by `objectClass`. AD is the only connector where `native_id` (`objectGUID`,
lowercased) is shared between the account and group derivation — it's computed once,
before the record is routed. AD produces no assets or entitlements.

### 2.1 Account → `staging_account`

| Staging column | Raw source | How |
|---|---|---|
| `native_id` | `objectGUID` | Hardcoded; lowercased; record dropped if absent |
| `account_kind` | `objectClass` | Hardcoded: `"computer"` if it contains `computer`, else `"user"` |
| `is_human` | *(derived)* | Hardcoded: `true` iff `account_kind == "user"` |
| `display_name` | `displayName`, fallback `name` | Config (`display_name`); falls back to `name[0]` if `displayName` absent |
| `sam_account_name` | `sAMAccountName` | Config |
| `upn` | `userPrincipalName` | Config |
| `email` | `mail` | Config |
| `domain_name` | `dn` | Hardcoded: tail of `dn` after its first `,DC=`, remaining `,DC=` replaced with `.` |
| `filer_name` | — | Not populated (always `""` — see §5) |
| `native_account_id` | `objectSid` | Config |
| `is_deleted` | — | Hardcoded constant `false` (no deletion signal in AD exports today) |
| `is_disabled` | `userAccountControl` | Hardcoded: bit 2 (`ACCOUNTDISABLE`) set |
| `raw_attributes` | `dn`, `objectClass`, `department`, `givenName`, `sn`, `employeeID`, `memberOf`, `manager`, + every other field not in `ad-account.json`'s column map | `RawAttributesWithEdges`: `aliasKeys` (`[dn.lower()]`); `edgeRefs` (`memberOf` → `MEMBER_OF` out to `grp`; `manager` → `REPORTS_TO` out to `account`); `extra` = generic `UnmappedAttributeCollector` output |

**Content hash inputs (`ad-account.json`):** `connector_type`, `native_id`, `account_kind`, `display_name`, `upn`, `email`, `domain_name`, `is_deleted`, `is_disabled`, `edge_refs`.

### 2.2 Group → `staging_grp`

| Staging column | Raw source | How |
|---|---|---|
| `native_id` | `objectGUID` | Same shared derivation as account |
| `group_class` | `groupType`, `IsDL` | Hardcoded: `"distribution"` if `IsDL`; else `groupType` decoded via a fixed bitmask table (`security_global`/`_domainlocal`/`_universal`, `distribution_global`/`_domainlocal`/`_universal`); default `"security_global"` if unrecognized |
| `display_name` | `displayName`, fallback `cn` | Config; falls back to `cn[0]` if `displayName` absent |
| `email` | `mail` | Config |
| `domain_name` | `dn` / `distinguishedName` | Hardcoded: every `DC=` component of the DN joined with `.` (a different algorithm from the account's — both intentionally kept independent) |
| `is_large_group` | `IsLargeGroup` | Hardcoded passthrough boolean |
| `is_deleted` | — | Hardcoded constant `false` |
| `raw_attributes` | `dn`, `member`, `memberOf`, `managedBy`, + every other field not in `ad-group.json`'s column map | `RawAttributesWithEdges`: `aliasKeys` (`[dn.lower()]`); `edgeRefs` (`member` → `MEMBER_OF` in; `memberOf` → `MEMBER_OF` out; `managedBy` → `MANAGED_BY` out); `extra` = generic `UnmappedAttributeCollector` output |

**Content hash inputs (`ad-group.json`):** `connector_type`, `native_id`, `group_class`, `display_name`, `email`, `domain_name`, `is_deleted`, `edge_refs`.

## 3. CyberArk — connector_type `401`

`CyberArkNormalizer.cs`. One clean JSON document. This connector now produces **four**
distinct entity kinds from three raw sections, and no longer produces entitlements:

- `Accounts.AccountDetails[]` → privileged-credential `ParsedAccount`s (`cyberark-account.json`).
- `Safes[]` → `ParsedAsset`s (`cyberark-safe.json`) — a safe is only ever an edge
  *target*, never a declarer of outbound refs.
- `Members[]` (safe-permission grants) — structurally CyberArk's version of a
  `PublicFolderPermission`: **edge-level properties on a `HAS_ACCESS` edge, not a
  standalone entitlement.** Each distinct `(MemberType, MemberName)` is consolidated
  across every `Members[]` row mentioning it (a member can have access to several safes)
  into one CyberArk-scoped `ParsedAccount` (`MemberType: "User"`, via
  `cyberark-member-account.json`) or `ParsedGroup` (`"Group"`, via
  `cyberark-member-group.json`) carrying one `HAS_ACCESS` edge per safe. These are a
  completely separate principal space from CyberArk's own privileged-credential
  Accounts (managed secrets, not vault users) — no cross-connector or cross-kind identity
  resolution is attempted here.

`Platforms` is read but not normalized — policy-template metadata with no Account/Group/
Asset/Entitlement fit.

### 3.1 Account (privileged credential) → `staging_account`, entity kind `Account`

| Staging column | Raw source | How |
|---|---|---|
| `native_id` | `AccountId` | Hardcoded; record dropped if absent |
| `account_kind` | — | Hardcoded constant `"privileged"` |
| `is_human` | — | Hardcoded constant `false` |
| `display_name` | `Name` | Config |
| `sam_account_name` | `UserName` | Config |
| `upn` | — | Not populated |
| `email` | — | Not populated |
| `domain_name` | `Address` | Config |
| `native_account_id` | — | Not populated |
| `is_deleted` | — | Hardcoded constant `false` |
| `is_disabled` | — | Hardcoded constant `false` (no clean enabled/disabled flag in CyberArk raw data) |
| `raw_attributes` | `SafeName` + every other `AccountDetails[]` field not in `cyberark-account.json`'s column map | `RawAttributesWithEdges`: `aliasKeys` = `[]` (a privileged account declares none); `edgeRefs` = one `STORED_IN` edge (`out`, target kind `asset`) to the safe it lives in, resolved from `SafeName` via a `SafeName → SafeNumber` lookup built while processing `Safes[]`; `extra` = generic `UnmappedAttributeCollector` output |

**Content hash inputs (`cyberark-account.json`):** `connector_type`, `native_id`, `account_kind`, `display_name`, `sam_account_name`, `domain_name`, `is_deleted`, `is_disabled`, `edge_refs`.

### 3.2 Asset (Safe) → `staging_asset`, entity kind `Asset`

| Staging column | Raw source | How |
|---|---|---|
| `native_id` | `SafeNumber` | Hardcoded; stringified; record dropped if absent |
| `asset_type` | — | Hardcoded constant `"cyberark_safe"` |
| `display_name` | `SafeName` | Config |
| `full_path` | `Location` + `SafeName` | Hardcoded: `Location` (trailing `\` trimmed) joined to `SafeName` with `\`, or just `SafeName` if `Location` is absent |
| `filer_name` | — | Not populated |
| `file_size` | — | Not populated (defaults to `0`) |
| `file_count` | — | Not populated (defaults to `0`) |
| `broken_acl` | — | Not populated (defaults to `false`) |
| `is_protected` | — | Not populated (defaults to `false`) |
| `raw_attributes` | every `Safes[]` field not in `cyberark-safe.json`'s column map | `RawAttributesWithEdges`: `aliasKeys` = `[safeNumber]` (so other entities' `STORED_IN`/`HAS_ACCESS` refs can resolve against it); `edgeRefs` = `[]` (a safe never declares outbound refs); `extra` = generic `UnmappedAttributeCollector` output |

**Content hash inputs (`cyberark-safe.json`):** `connector_type`, `native_id`, `asset_type`, `display_name`, `full_path`.

### 3.3 MemberAccount → `staging_account`, entity kind `MemberAccount`

One row per distinct `MemberName` where `MemberType == "User"`, consolidated across every
safe that member has access to.

| Staging column | Raw source | How |
|---|---|---|
| `native_id` | `MemberName` | Hardcoded (raw, not lowercased); record dropped if `MemberType`/`MemberName` absent |
| `account_kind` | — | Hardcoded constant `"cyberark_user"` |
| `is_human` | — | Hardcoded constant `true` |
| `display_name` | `MemberName` | Config |
| `sam_account_name`, `upn`, `email`, `domain_name`, `native_account_id` | — | Not populated |
| `is_deleted` | — | Hardcoded constant `false` |
| `is_disabled` | — | Not populated (defaults to `false`; not in this entity's hash fields) |
| `raw_attributes` | `SafeNumber` (per safe) + `Permissions` + every other `Members[]` field not in `cyberark-member-account.json`'s column map | `RawAttributesWithEdges`: `aliasKeys` = `[memberName.lower()]`; `edgeRefs` = one `HAS_ACCESS` edge (`out`, target kind `asset`) per safe this member has access to, each edge's `Props` a JSON string cloning that safe's `Permissions` object (~20 raw flags) plus a derived `is_admin` flag (`true` if `ManageSafe` or `ManageSafeMembers` is set); `extra` = `UnmappedAttributeCollector` output from the member's first `Members[]` row |

**Content hash inputs (`cyberark-member-account.json`):** `connector_type`, `native_id`, `account_kind`, `display_name`, `is_deleted`, `edge_refs`.

### 3.4 MemberGroup → `staging_grp`, entity kind `MemberGroup`

One row per distinct `MemberName` where `MemberType == "Group"` — otherwise identical
consolidation and edge-building logic to §3.3.

| Staging column | Raw source | How |
|---|---|---|
| `native_id` | `MemberName` | Hardcoded (raw, not lowercased) |
| `group_class` | — | Hardcoded constant `"cyberark_group"` |
| `display_name` | `MemberName` | Config |
| `email`, `domain_name` | — | Not populated |
| `is_large_group` | — | Hardcoded constant `false` |
| `is_deleted` | — | Hardcoded constant `false` |
| `raw_attributes` | Same shape as MemberAccount (§3.3) | `RawAttributesWithEdges`: `aliasKeys` = `[memberName.lower()]`; `edgeRefs` = one `HAS_ACCESS` edge per safe (same `Permissions`/`is_admin` props); `extra` = `UnmappedAttributeCollector` output |

**Content hash inputs (`cyberark-member-group.json`):** `connector_type`, `native_id`, `group_class`, `display_name`, `is_deleted`, `edge_refs`.

## 4. Windows (local accounts) — connector_type `402`

`WindowsNormalizer.cs`. One clean JSON document. Each `Filers[].LocalADPaths[]` entry is
a stringified JSON blob (same convention as AD's `AccountsPaths`) holding an `Accounts[]`
array, split into users/groups by `ObjectClass`. Like AD, `native_id` is derived once
before the record is routed to account or group handling. Windows produces no assets or
entitlements.

### 4.1 Account → `staging_account`

| Staging column | Raw source | How |
|---|---|---|
| `native_id` | `ObjectGuid`, fallback `Sid` | Hardcoded; lowercased; record dropped if both absent |
| `account_kind` | — | Hardcoded constant `"user"` (this connector only reports local user accounts) |
| `is_human` | — | Hardcoded constant `true` |
| `display_name` | `Name` | Config |
| `sam_account_name` | `SamAccountName` | Config; also seeds `AliasKeys` for edge resolution |
| `upn` | — | Not populated |
| `email` | `Email` | Config |
| `domain_name` | `DomainFQDN` | Config |
| `native_account_id` | `Sid` | Config |
| `is_deleted` | — | Hardcoded constant `false` |
| `is_disabled` | `AccountStatus` | Hardcoded: `!AccountStatus` (`AccountStatus == true` means enabled) |
| `raw_attributes` | `SamAccountName` (for `aliasKeys`) + every field not in `windows-account.json`'s column map | `RawAttributesWithEdges`: `aliasKeys` = `[samAccountName.lower()]`, `edgeRefs` = `[]` (accounts declare no outbound refs), `extra` = generic `UnmappedAttributeCollector` output |

**Content hash inputs (`windows-account.json`):** `connector_type`, `native_id`, `account_kind`, `display_name`, `sam_account_name`, `email`, `domain_name`, `is_deleted`, `is_disabled`.

### 4.2 Group → `staging_grp`

| Staging column | Raw source | How |
|---|---|---|
| `native_id` | `ObjectGuid`, fallback `Sid` | Same shared derivation as account |
| `group_class` | — | Hardcoded constant `"local_security"` |
| `display_name` | `Name` | Config |
| `email` | `Email` | Config |
| `domain_name` | `DomainFQDN` | Config |
| `is_large_group` | — | Hardcoded constant `false` |
| `is_deleted` | — | Hardcoded constant `false` |
| `raw_attributes` | `DirectMembers[].SamAccountName` + every field not in `windows-group.json`'s column map | `RawAttributesWithEdges`: `aliasKeys` = `[native_id]`, `edgeRefs` = one `MEMBER_OF` (`in`, target kind `account`) per `DirectMembers[]` entry keyed by `SamAccountName` (not a DN, unlike AD), `extra` = generic `UnmappedAttributeCollector` output |

**Content hash inputs (`windows-group.json`):** `connector_type`, `native_id`, `group_class`, `display_name`, `email`, `domain_name`, `is_deleted`, `edge_refs`.

## 5. Notes / known gaps

- **`filer_name` is currently unpopulated by every connector**, on both `staging_account`
  and `staging_asset` — `Stager` defaults it to `""`/not-set for every row, reserved for a
  future file-share-scoped connector. Not a bug in this doc's scope; flagged for
  awareness only.
- **The entitlement path is dormant** — see §1. `staging_entitlement`/`tenant.entitlement`
  are still fully wired in `Stager`/`Promoter`, but no connector produces
  `ParsedEntitlement` rows today.
- **`is_human`/`is_disabled` are staged but not always hashed** — e.g. neither is in
  `cyberark-member-account.json`'s or `cyberark-member-group.json`'s `hashFields`, so a
  change to those columns alone wouldn't trigger a CDC update for those entity kinds
  (moot today since both are hardcoded constants for those entity kinds anyway).
- **CyberArk's `HAS_ACCESS`/`STORED_IN` edges carry data beyond a plain reference** — the
  `Permissions` object (~20 flags) plus a derived `is_admin` summary flag ride along as
  the edge's `Props` (a JSON string), resolved later by the edge gate rather than staged
  as their own column. This is the mechanism that replaced CyberArk's old standalone
  Entitlement records.
