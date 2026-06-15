# Changelog

All notable changes to HomotechsualBot will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
