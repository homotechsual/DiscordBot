# Changelog

All notable changes to HomotechsualBot will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.20] - 2026-06-24

### Changed

* Guarantee deleted-message avatar thumbnail fallback

## [1.0.19] - 2026-06-24

### Changed

* Stabilize deleted message attribution with early capture and author profile fallback

## [1.0.18] - 2026-06-24

### Changed

* Add receive-time snapshot fallback for deleted message author attribution

## [1.0.17] - 2026-06-24

### Changed

* Enable message cache for deleted-message author attribution

## [1.0.16] - 2026-06-24

### Changed

* Improve deleted message author attribution from audit logs

## [1.0.15] - 2026-06-24

### Changed

* Add moderation event audit logging and deploy secret wiring

## [1.0.14] - 2026-06-20

### Changed

* show offending image in cross-channel spam log embed

## [1.0.13] - 2026-06-18

### Changed

* Show username and user ID in /singlemessage list output

## [1.0.12] - 2026-06-18

### Changed

* Add EF migration for persistent single-message backfill startup fix

## [1.0.11] - 2026-06-18

### Changed

* Add persistent background single-message history backfill

## [1.0.10] - 2026-06-18

### Changed

* Fix single-message enable interaction timeout by deferring response

## [1.0.9] - 2026-06-15

### Changed

* Add configurable cross-channel spam enforcement defaults (delete+timeout on)

## [1.0.8] - 2026-06-15

### Changed

* Fix live test detection race via TCS; make content optional (attachment-only test now supported)

## [1.0.7] - 2026-06-15

### Changed

* Fix cross-channel live test detection state and add attachment-aware spam test support

## [1.0.6] - 2026-06-15

### Changed

* Improve cross-channel spam detection fingerprinting, logging, and add cleanup-enabled live testing

## [1.0.5] - 2026-06-15

### Changed

* Add moderation exemptions, command access controls, and forum log resolution fallback

## [1.0.4] - 2026-06-15

### Changed

* `SingleMessageService` is now fully DB-backed — channel registration no longer requires an `appsettings.json`/env-var config entry; `/singlemessage enable` and `/singlemessage disable` operate directly on the database at runtime with no redeploy needed
* `/singlemessage enable` gains a `scan_history` parameter (default `true`) replacing the old per-channel config flag
* `/singlemessage list` now shows enforcement status (active / disabled) alongside posted users

### Added

* `/spam test` command (requires Manage Messages) — dry-runs the cross-channel spam detector against any text, showing the computed fingerprint, current config, trigger conditions, and enforcement actions without taking any real action

## [1.0.3] - 2026-06-15

### Fixed

* `YoutubeFeedUrlsEndpointHostedService` is no longer registered when the YouTube monitor is disabled — previously it started unconditionally and crashed with `ObjectDisposedException` at startup
* Fixed hardcoded `"hudu-bot"` service label in `YoutubeFeedUrlsEndpointHostedService` payload — now correctly reports `"homotechsual-bot"`

## [1.0.2] - 2026-06-15

### Fixed

* YouTube forum post title now truncates to Discord's 100-character limit; guards against orphaned surrogate pairs at the truncation boundary
* YouTube forum post title falls back to `[{ChannelName}] {VideoId}` if template substitution produces an empty or whitespace-only result
* Log the resolved post title (with length) at Info level before posting, to aid diagnosis of future `BASE_TYPE_BAD_LENGTH` rejections

## [1.0.1] - 2026-06-15

### Fixed

* Status monitor no longer enables itself by default when `STATUS_MONITOR_ENABLED` secret is absent — deploy workflow default changed from `true` to `false`
* Removed hardcoded Halo Services Solutions feed URL from `StatusMonitorConfig` default; `FeedUrl` now defaults to empty so misconfigured deployments fail clearly
* Deploy workflows for all four bots now include `ModerationLog` and `CrossChannelSpam` environment variables
* Fixed Metrics port conflict: `appsettings.json` now uses port `9094` (Metrics) and `9194` (FeedUrlsPort) instead of `9092`/`9192` which conflicted with HuduCommunityBot

## [1.0.0] - 2026-06-14

### Added

* Initial release
* Slash commands via Discord.Net's InteractionService
* Moderation tools: ban, kick, mute, warn, clear, purge, slowmode, lock/unlock
* General utilities: avatar, userinfo, serverinfo, reminders, fun commands
* YouTube channel monitor with configurable forum channel posting
* Single-message channel enforcement with slash commands
* Moderation action logging to a configurable forum channel
* Cross-channel spam detection with moderator ban/dismiss buttons
* Uptime heartbeat monitor
* Prometheus metrics endpoint
