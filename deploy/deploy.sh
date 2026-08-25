#!/usr/bin/env bash
# Atomic deploy for a single systemd --user service on this host.
# Runs as the service's own unprivileged user - no sudo needed.
#
# Usage: deploy.sh <service-name> <git-sha> <path-to-release-tar.gz>
set -euo pipefail

SERVICE_NAME="${1:?service name required}"
SHA="${2:?git sha required}"
TARBALL="${3:?path to release tarball required}"
KEEP_RELEASES=5

APP_DIR="$HOME/app"
RELEASES_DIR="$APP_DIR/releases"
NEW_RELEASE="$RELEASES_DIR/$SHA"
CURRENT_LINK="$APP_DIR/current"

mkdir -p "$NEW_RELEASE"
tar xzf "$TARBALL" -C "$NEW_RELEASE"

PREV_RELEASE=""
if [ -L "$CURRENT_LINK" ]; then
  PREV_RELEASE="$(readlink -f "$CURRENT_LINK")"
fi

ln -sfn "$NEW_RELEASE" "$CURRENT_LINK"

systemctl --user daemon-reload
systemctl --user restart "$SERVICE_NAME"

# Give the service a moment to crash on bad config/connectivity before we
# call this a success.
sleep 3
if ! systemctl --user is-active --quiet "$SERVICE_NAME"; then
  echo "Health check failed for $SERVICE_NAME after deploying $SHA" >&2
  if [ -n "$PREV_RELEASE" ]; then
    echo "Rolling back to $PREV_RELEASE" >&2
    ln -sfn "$PREV_RELEASE" "$CURRENT_LINK"
    systemctl --user restart "$SERVICE_NAME"
  fi
  exit 1
fi

echo "Deployed $SERVICE_NAME @ $SHA"

# Prune old releases, keep the most recent $KEEP_RELEASES.
cd "$RELEASES_DIR"
ls -1t | tail -n "+$((KEEP_RELEASES + 1))" | xargs -r rm -rf --
