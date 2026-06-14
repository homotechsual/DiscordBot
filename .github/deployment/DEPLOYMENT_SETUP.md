# HomotechsualBot Deployment Setup Guide

This guide covers setting up HomotechsualBot on a DigitalOcean droplet (or any Linux host).

## Prerequisites

* Linux host (Ubuntu 20.04+)
* SSH access with sudo privileges
* SSH key pair for deployment

## Initial Setup (One-time)

### 1. Create Deployment User

```bash
sudo useradd -m -s /bin/bash deployer
sudo usermod -aG sudo deployer
```

### 1.1 Create Runtime User

The service runs as a dedicated non-login account named `homotechsualbot`.

```bash
sudo useradd -r -s /usr/sbin/nologin homotechsualbot
```

### 2. Setup Directory Structure

```bash
sudo mkdir -p /opt/homotechsualbot
sudo chown deployer:deployer /opt/homotechsualbot
```

The deploy workflow temporarily grants `deployer` write access for file sync, then sets final runtime ownership back to `homotechsualbot` before starting the service.

### 3. Create `.env` File

The deployment workflow automatically creates/updates this file with secrets from GitHub Actions.

Manual creation (if needed):

```bash
sudo tee /opt/homotechsualbot/.env > /dev/null << 'EOF'
HOMOTECHSUALBOT_Bot__Token=your_discord_token_here
HOMOTECHSUALBOT_Bot__StatusMonitor__Enabled=true
HOMOTECHSUALBOT_Bot__StatusMonitor__ChannelId=your_channel_id_here
HOMOTECHSUALBOT_Bot__StatusMonitor__RoleId=your_role_id_here
HOMOTECHSUALBOT_Bot__YoutubeMonitor__YouTubeDataApiKey=your_youtube_data_api_key_here
EOF

sudo chmod 600 /opt/homotechsualbot/.env
```

Environment variable names use the `HOMOTECHSUALBOT_` prefix and `__` (double-underscore) as the config section separator:

| Variable | Config key | Purpose |
| --- | --- | --- |
| `HOMOTECHSUALBOT_Bot__Token` | `Bot:Token` | Discord bot token |
| `HOMOTECHSUALBOT_Bot__StatusMonitor__Enabled` | `Bot:StatusMonitor:Enabled` | Enable status RSS monitor |
| `HOMOTECHSUALBOT_Bot__StatusMonitor__ChannelId` | `Bot:StatusMonitor:ChannelId` | Channel to post status updates |
| `HOMOTECHSUALBOT_Bot__StatusMonitor__RoleId` | `Bot:StatusMonitor:RoleId` | Optional role to mention for status updates |
| `HOMOTECHSUALBOT_Bot__YoutubeMonitor__YouTubeDataApiKey` | `Bot:YoutubeMonitor:YouTubeDataApiKey` | Optional YouTube Data API key for resolving channel names |

### 4. Install .NET 10 Runtime

```bash
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/local/dotnet
sudo ln -sf /usr/local/dotnet/dotnet /usr/bin/dotnet
```

Verify the installed runtime:

```bash
/usr/local/dotnet/dotnet --list-runtimes | grep Microsoft.AspNetCore.App
```

> **Important:** `dotnet --version` will report "No .NET SDKs were found" on a runtime-only install — that is expected and fine. The bot binary locates the runtime via `DOTNET_ROOT=/usr/local/dotnet`, which is set in the systemd service unit (see step 5).

### 5. Install Systemd Service

The service unit file is committed at `.github/deployment/homotechsualbot.service` in this repo.
It sets `DOTNET_ROOT=/usr/local/dotnet` so the framework-dependent binary can find the runtime.

```bash
sudo cp .github/deployment/homotechsualbot.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable homotechsualbot.service
```

### 6. Allow Non-Interactive Deploy Commands

The GitHub Actions deploy workflow runs `sudo` over SSH without a TTY/password prompt.
Grant `deployer` passwordless access to only the commands needed by the workflow:

```bash
sudo tee /etc/sudoers.d/homotechsualbot-deploy > /dev/null << 'EOF'
deployer ALL=(root) NOPASSWD: /bin/systemctl start homotechsualbot.service, /bin/systemctl stop homotechsualbot.service, /bin/systemctl status homotechsualbot.service, /bin/systemctl daemon-reload, /bin/chown, /bin/chmod, /usr/bin/tee, /bin/mkdir, /bin/mv
EOF
sudo chmod 440 /etc/sudoers.d/homotechsualbot-deploy
sudo visudo -cf /etc/sudoers.d/homotechsualbot-deploy
```

## Deployment Workflow

The GitHub Actions workflow (`deploy.yml`) handles:

1. Building the project
2. Publishing a Release build
3. Uploading files via SSH/rsync
4. Re-deploying the systemd service unit and reloading systemd
5. Writing the `.env` file from GitHub secrets
6. Managing the systemd service (stop → deploy → start)
7. Checking service status post-deploy

### Required GitHub Secrets

Set these in your repository settings (Settings → Secrets and variables → Actions):

| Secret | Purpose |
| ------ | ------- |
| `DEPLOY_SSH_KEY` | Private SSH key for the deployer user |
| `DEPLOY_HOST` | IP/hostname of your server |
| `DISCORD_TOKEN` | Discord bot token |
| `STATUS_MONITOR_CHANNEL_ID` | Discord channel ID for status updates (optional) |
| `STATUS_MONITOR_ROLE_ID` | Discord role ID to mention in status updates (optional) |

## Managing the Service

After initial setup, manage the bot with:

```bash
# Check status
sudo systemctl status homotechsualbot.service

# View logs
sudo journalctl -u homotechsualbot.service -f

# Restart manually
sudo systemctl restart homotechsualbot.service

# Stop
sudo systemctl stop homotechsualbot.service

# Start
sudo systemctl start homotechsualbot.service
```

## Updating Environment Variables

### Option 1: Via Deployment

Update the workflow in `.github/workflows/deploy.yml` and push to main branch. All keys in the generated `.env` must use the `HOMOTECHSUALBOT_` prefix to be picked up by the app.

### Option 2: Manual SSH

```bash
ssh deployer@your-host
sudo nano /opt/homotechsualbot/.env
# Edit the file, save and exit
sudo systemctl restart homotechsualbot.service
```

## Troubleshooting

### Service won't start

```bash
sudo journalctl -u homotechsualbot.service -n 50
```

### .env file not found

```bash
ls -la /opt/homotechsualbot/.env
# Should show: -rw------- (600 permissions)
```

### Permission denied errors

```bash
sudo chown -R homotechsualbot:homotechsualbot /opt/homotechsualbot
```

### Check runtime user exists

```bash
id homotechsualbot
getent passwd homotechsualbot
```

### Check if .NET is installed

```bash
dotnet --version
```

## Database

SQLite database is stored at `/opt/homotechsualbot/homotechsualbot.db`. This persists between deployments.

To backup:

```bash
cp /opt/homotechsualbot/homotechsualbot.db /opt/homotechsualbot/homotechsualbot.db.backup
```

