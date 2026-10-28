#!/usr/bin/env bash
# Build Monarch locally and install it to ~/.monarch/bin/<version>/monarch.
set -euo pipefail

repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
command -v jq >/dev/null || { echo "jq is required" >&2; exit 1; }
command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 1; }

version=$(jq -r .version "$repo/plugin/.claude-plugin/plugin.json")
[ -n "$version" ] && [ "$version" != null ] || { echo "no version in plugin.json" >&2; exit 1; }

case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) rid=osx-arm64 ;;
  Linux-x86_64) rid=linux-x64 ;;
  *) echo "unsupported platform: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
esac

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

echo "Publishing Monarch $version ($rid) ..."
dotnet publish "$repo/src/Monarch.Cli" -c Release -r "$rid" -o "$tmp/out"
[ -f "$tmp/out/monarch" ] || { echo "publish produced no 'monarch' binary (check AssemblyName)" >&2; exit 1; }

dest="$HOME/.monarch/bin/$version"
mkdir -p "$dest"
cp "$tmp/out/monarch" "$dest/monarch.new"
chmod +x "$dest/monarch.new"
mv -f "$dest/monarch.new" "$dest/monarch"
rm -f "$dest/.verified"

if command -v sha256sum >/dev/null; then
  hash=$(sha256sum "$dest/monarch" | cut -d' ' -f1)
else
  hash=$(shasum -a 256 "$dest/monarch" | cut -d' ' -f1)
fi
printf '%s  monarch\n' "$hash" > "$dest/SHA256SUMS"
ln -sfn "$dest/monarch" "$HOME/.monarch/bin/monarch"

cat <<EOT

Installed: $dest/monarch
SHA-256:   $hash
Symlink:   $HOME/.monarch/bin/monarch

Next steps:
  1. In Claude Code:  /plugin marketplace add $repo
                      /plugin install monarch@monarch-local
  2. In your project: ~/.monarch/bin/monarch awaken
  3. Restart Claude Code (hooks and statusline load at startup).
EOT
