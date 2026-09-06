# Phase 4 — Backup and Resilience

**Duration:** 2 weeks · **Tasks:** 9 · **Exit:** the business is protected off-site and restore is proven into a clean VM/container. Timed restore on real replacement hardware is `HW-T08`.

## Scope

Cloud upload pipeline, retention, the backup web portal, guided restore from cloud, the monthly restore self-test, and the resilience hardening that AC-13/AC-15/AC-16 demand.

## Why this phase is not optional

The SRS says it directly: an unbacked-up POS is the single largest risk the shop carries. Local and USB backup already exist from P1-T15. This phase adds the off-site copy, which is what survives fire, theft and ransomware.

**Blockers:** Q-D (which cloud target) and Q-E (owner's signed acknowledgement that a lost passphrase means unrecoverable backups).

---

### P4-T01 — Backup target abstraction
**Depends on:** P1-T15 · **Est:** 1.5d · **SRS:** FR-11.5, Q-09, SAD ADR-003

**Context.** The cloud store is passive and interchangeable. Keeping it behind one narrow interface means the choice in Q-D is reversible and the terminal holds the least possible privilege.

**Do this.**
1. `Backup/Targets/IBackupTarget`: `UploadAsync(stream, key, metadata)`, `ListAsync(prefix)`, `DownloadAsync(key)`, `DeleteAsync(key)`, `TestConnectionAsync()`.
2. Implementations:
   - `S3CompatibleTarget` (GCS / R2 / B2 / any S3 API)
   - `GoogleDriveTarget` (OAuth device-code flow at setup, refresh token only)
   - `LocalFolderTarget` (for testing and for a NAS if the owner prefers)
3. Credentials stored in Windows Credential Manager, never in a config file (NFR-S6). Config holds a reference key only.
4. Connection test button in settings with a clear success/failure message.
5. TLS enforced; certificate validation never disabled, not even behind a debug flag.

**Deliverables.** Target interface, three implementations, credential storage, connection test.

**Risks.** The terminal being given delete permission. It must not have it — see P4-T02. Also: OAuth refresh tokens expiring silently. Surface an "authorisation expired, re-connect" state rather than a generic upload failure.

**Done when.**
- [ ] Each target uploads, lists and downloads a test file
- [ ] Credentials are retrievable only from Credential Manager; no secret appears in any config file or log
- [ ] A wrong credential produces a clear message naming the problem
- [ ] Connection test works from the settings screen
- [ ] Switching targets in settings does not require a restart

---

### P4-T02 — Least-privilege and immutability setup
**Depends on:** P4-T01 · **Est:** 1d · **SRS:** NFR-S4, SAD §4 (ransomware)

**Context.** If the till is compromised, credentials on it can destroy every off-site copy. This is a configuration and documentation task as much as a code one, and it is the difference between a backup strategy and a backup theatre.

**Do this.**
1. Document and script the provisioning of a **create-only** credential: on GCS, a service account with `storage.objects.create` and nothing else; on S3, a policy with `s3:PutObject` only.
2. Enable object versioning and a retention/lock policy (default 35 days) on the bucket, provisioned by script.
3. Retention pruning (FR-11.9) runs **from the portal service**, never from the terminal.
4. If the target is consumer Google Drive, document plainly that immutability is not available and recommend the bucket option — this is a real difference in protection, and the owner should choose it knowingly.
5. Startup check: attempt a delete against the configured target; if it succeeds, raise a warning that the terminal has more privilege than it should.

**Deliverables.** Provisioning scripts, retention policy, portal-side pruning, privilege self-check, a written owner-facing explanation.

**Risks.** Treating this as paperwork. Run the check: a terminal that can delete its own backups has not been protected against the most likely total-loss scenario.

**Done when.**
- [ ] Provisioning script creates a create-only credential and a versioned, retention-locked bucket
- [ ] The terminal cannot delete or overwrite an uploaded object (verified by attempting it)
- [ ] Pruning from the portal removes only objects outside the retention window
- [ ] The privilege self-check warns when the credential is over-scoped
- [ ] Owner-facing explanation written into the admin manual

---

### P4-T03 — Upload worker with retry and backoff
**Depends on:** P4-T01 · **Est:** 2d · **SRS:** FR-11.5, FR-11.6, FR-11.8, C-05

**Context.** The upload must be completely invisible to trading. Intermittent internet is the assumed condition (A-04), not an exception.

**Do this.**
1. `UploadWorker` `BackgroundService` polling `backup_record` where `cloud_status = 'PENDING'`.
2. Exponential backoff with jitter, capped (e.g. 1 m → 2 m → 5 m → 15 m → 1 h). Attempt count and last error persisted, so retries resume across restarts.
3. Bandwidth throttle setting so an upload cannot saturate a slow shop connection during trading hours; optional upload window (e.g. after closing).
4. Checksum verified after upload by re-reading the object's metadata or a HEAD request (FR-11.8).
5. Status surfaced in the status bar and dashboard; escalating warning after N days without a successful upload (FR-11.7, default 3).
6. Failure categories distinguished in the UI: no internet, credentials expired, storage full, checksum mismatch. Each with a different suggested next step (UI-06).

**Deliverables.** Upload worker, backoff, throttle, verification, status surfacing.

**Risks.** The worker holding the write lock while uploading. It must read `backup_record` and update status in short transactions only — never hold a transaction open across a network call.

**Done when.**
- [ ] With the network disabled, the app trades normally and the backup queues; scan latency is unaffected (measured)
- [ ] Re-enabling the network uploads the queued backups automatically without user action
- [ ] Killing the app mid-upload and restarting resumes correctly, with no duplicate or corrupt object
- [ ] Checksum mismatch is detected and the upload retried
- [ ] Each failure category shows a distinct, actionable message
- [ ] After 3 days without an upload the dashboard warning escalates

---

### P4-T04 — Retention and pruning
**Depends on:** P4-T02, P4-T03 · **Est:** 1d · **SRS:** FR-11.9

**Context.** Grandfather-father-son: last 14 daily, last 8 weekly, last 12 monthly. Applies to local, USB and cloud, with different actors.

**Do this.**
1. Retention policy engine deciding, for a given set of backup timestamps, which to keep and which to prune. Pure function, heavily tested.
2. Local and USB pruning runs on the terminal.
3. Cloud pruning runs in the portal service (per P4-T02).
4. Configurable counts; a floor that refuses to prune below 3 retained copies regardless of configuration.
5. Never prune the most recent successful verified backup, ever.

**Deliverables.** Retention engine, local pruner, portal pruner, safety floor.

**Risks.** A clock change or timezone bug pruning everything. The engine takes an explicit "now" and is tested against DST transitions and a backdated system clock.

**Done when.**
- [ ] Given 400 daily timestamps, the engine retains exactly the GFS set and the result is stable across repeated runs
- [ ] The most recent verified backup is never pruned under any configuration
- [ ] Setting retention to zero is rejected
- [ ] Pruning across a DST boundary retains the correct set
- [ ] Local, USB and cloud pruning all use the same engine

---

### P4-T05 — Backup web portal
**Depends on:** P4-T02 · **Est:** 2.5d · **SRS:** FR-11.10, FR-11.11, OS-13

**Context.** Small, read-only, and deliberately boring. Every feature added here erodes the guarantee that the cloud cannot affect trading.

**Do this.**
1. Single service (Cloud Run or equivalent): owner sign-in, list backups with date, size, checksum, schema version and verification status, and a time-limited signed download URL per object.
2. Runs the cloud retention pruning job on a schedule.
3. **No business data.** The service never decrypts anything and has no access to the passphrase. It cannot show a product, a sale or a customer (FR-11.11) — enforced structurally, since it only ever sees ciphertext.
4. Authentication: single owner account, strong password, rate-limited, MFA if the platform makes it easy. Access attempts logged.
5. Infrastructure as code (Terraform or gcloud script) checked into `portal/`, so it is reproducible at handover.
6. Cost note in the admin manual: expected monthly spend at this volume.

**Deliverables.** Portal service, IaC, deployment docs, cost note.

**Risks.** Scope creep into a dashboard. OS-13 excludes it and C-02 depends on it. If the owner asks for live sales visibility, that is a change request against the architecture, not a portal feature.

**Done when.**
- [ ] The owner can sign in, see the backup list with integrity status, and download a file
- [ ] An unauthenticated request to any endpoint is rejected
- [ ] The portal cannot display any business data (no code path exists to decrypt)
- [ ] Retention pruning runs on schedule and logs what it removed
- [ ] The whole portal deploys from scratch using the checked-in IaC

---

### P4-T06 — Guided restore from cloud
**Depends on:** P4-T05, P1-T15 · **Est:** 2d · **SRS:** FR-11.12, FR-11.13, AC-14

**Context.** The one workflow that has to work perfectly the first time it is ever used in anger, by a non-technical person, on a bad day.

**Do this.**
1. Restore wizard, plain language, one decision per screen:
   1. choose source — local folder, USB, or a file downloaded from the portal
   2. verify checksum, show file date, size and schema version
   3. prompt for the passphrase (with a clear message if it is wrong: "this passphrase does not open this file")
   4. decrypt to a scratch location and run `PRAGMA integrity_check` plus hash-chain verification plus row counts
   5. **show exactly what date and time the data will be restored to, and what will be lost**
   6. require the owner to type a confirmation phrase
   7. back up the current database first, automatically
   8. swap and restart
2. Schema version handling: if the backup is older than the current schema, run migrations after restore; if it is newer, refuse with a clear message.
3. Owner-only; fully audited (FR-11.13).
4. Restore also works on a **clean environment** with no prior installation — this is the actual disaster case. A fresh VM/container proves the path here; a real replacement machine on site is `HW-T08`.

**Deliverables.** Restore wizard, schema-version handling, clean-environment path, audit entries.

**Risks.** A restore that half-completes and leaves neither database usable. Restore into a scratch file, verify fully, then swap atomically — never write into the live file.

**Done when.**
- [ ] AC-14: a backup is taken, uploaded, downloaded from the portal, verified, and restored into a **clean VM/container**, and the restored data matches exactly
- [ ] A wrong passphrase fails with a plain-language message and changes nothing
- [ ] A corrupted file is detected at checksum stage and changes nothing
- [ ] An older-schema backup restores and migrates; a newer-schema backup is refused clearly
- [ ] The pre-restore backup of the current database exists and is itself restorable
- [ ] Restore completes well within the 4-hour RTO in the clean VM (NFR-R5; the same timing on real replacement hardware is `HW-T08`)

---

### P4-T07 — Monthly restore self-test
**Depends on:** P4-T06 · **Est:** 1d · **SRS:** FR-11.14

**Context.** An unverified backup is a rumour. This is the cheapest possible insurance against silent corruption in the pipeline.

**Do this.**
1. Scheduled monthly job: take the latest backup, restore it into a scratch location, open it, run `integrity_check`, verify the hash chains, compare row counts against expectations, then delete the scratch copy.
2. Result written to `backup_record.verified_at` and surfaced on the dashboard.
3. Failure raises a persistent, escalating dashboard warning that cannot be dismissed permanently.
4. Runs outside trading hours; skipped and rescheduled if the machine is busy.

**Deliverables.** Self-test job, verification reporting, dashboard state.

**Risks.** The self-test consuming the disk or running during trading. Cap the scratch space, check free disk first, and hard-skip during shift hours.

**Done when.**
- [ ] The self-test runs unattended and records a verification timestamp
- [ ] A deliberately corrupted backup fails the self-test and raises the warning
- [ ] The scratch copy is always cleaned up, including after a failure
- [ ] The job does not run during an open shift
- [ ] Insufficient disk space is detected and reported rather than crashing

---

### P4-T08 — Resilience hardening
**Depends on:** P4-T03 · **Est:** 1.5d · **SRS:** NFR-R1–R5, C-05, AC-13, AC-15, AC-16

**Context.** Systematically prove that no peripheral, network or power event can stop the shop selling. Phase 1 proved individual cases; this hardens the whole surface — in software, against the fakes. Repeating the scenarios by physically pulling cables on the real peripherals is `HW-T08`.

**Do this.**
1. Failure-injection test harness driving the **fakes**: printer disconnected (`FileReceiptPrinter` throws), printer error mid-job, scanner input stops, drawer call fails, `NullScale` absent, USB path missing, network down, cloud credentials revoked, disk full, DB file locked by another process.
2. For each: assert the sale path still completes, a specific plain-language warning appears, and the event is logged.
3. Disk-space monitor: warn at a configurable free-space threshold; refuse to start a backup that would fill the disk.
4. Watchdog: if the app crashes, restart to the sales screen and recover the open shift with no data loss.
5. Graceful shutdown: flush the print queue state, `PRAGMA optimize`, checkpoint the WAL.
6. Power-loss test: 100 process kills at random points during the sale transaction; assert integrity and last-committed-bill survival every time.

**Deliverables.** Failure-injection harness, disk monitor, watchdog, shutdown handling, power-loss suite.

**Risks.** A failure mode that blocks the UI thread — e.g. a device call with no timeout. Every device call gets an explicit timeout and runs off the UI thread.

**Done when.**
- [ ] Every injected failure (against the fakes) leaves the sale path working, with a specific warning
- [ ] AC-13: a full simulated trading day with the network disabled in-process, zero functional loss (physical cable-out on the terminal is `HW-T09`)
- [ ] AC-15: 100 mid-transaction process kills, database intact and last committed bill present every time (on-terminal power cuts are `HW-T09`)
- [ ] AC-16: `FileReceiptPrinter` fails mid-transaction, sale completes, bill queued for reprint (real printer unplug is `HW-T01`)
- [ ] Disk-full is detected before it corrupts anything
- [ ] No device call can block the UI thread (verified by injecting a 30 s device stall)

> The same failure set, performed by physically disconnecting the real peripherals on the terminal, is **`HW-T08`**.

---

### P4-T09 — Restore drill with the owner
**Depends on:** P4-T06, P4-T07 · **Est:** 0.5d · **SRS:** FR-11.15, NFR-R5, Q-10, Q-E

**Context.** Not a development task — a rehearsal. The owner performs the restore, following the manual, on a spare machine, with the developer watching and not touching the keyboard. Anything they cannot do alone is a documentation defect.

**Do this.**
1. Write the restore procedure in the user manual: plain language, screenshots, no jargon (FR-11.15).
2. Owner performs, unaided: sign in to the portal, download a backup, run the restore wizard, confirm the shop's data is back.
3. Time it end to end and compare against the 4-hour RTO.
4. Confirm passphrase custody per Q-10: who holds it, where the off-premises written copy is kept.
5. Obtain the signed acknowledgement (Q-E) that a lost passphrase means unrecoverable backups.
6. Log every point where the owner hesitated and fix the manual accordingly.

**Deliverables.** Restore procedure documentation, a completed drill record, signed passphrase acknowledgement.

**Risks.** Demonstrating the restore *at* the owner instead of having them do it. The whole value of this task is finding out what they cannot do without help.

**Done when.**
- [ ] The owner completes a full restore unaided, following only the manual
- [ ] Elapsed time recorded and within the RTO
- [ ] Passphrase custody documented and an off-premises copy confirmed to exist
- [ ] The Q-E acknowledgement is signed
- [ ] Every hesitation point is fixed in the manual and the fix re-checked
