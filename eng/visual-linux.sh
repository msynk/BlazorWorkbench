#!/usr/bin/env bash
# Runs the visual regression suite (Server and WebAssembly sample hosts) inside the Linux container used by CI, so
# Linux baselines are reproducible on any machine with Docker and network access:
#
#   docker run --rm -v "$PWD:/src" mcr.microsoft.com/dotnet/sdk:10.0-noble bash /src/eng/visual-linux.sh
#   dotnet run --project src/BlazorWorkbench.Cli -- approve -o artifacts/visual-linux-server        # after review
#   dotnet run --project src/BlazorWorkbench.Cli -- approve -o artifacts/visual-linux-webassembly
#
# SRC defaults to /src (the mounted repository). Reports are written to $SRC/artifacts/visual-linux-*.
set -euo pipefail
SRC="${SRC:-/src}"
WORK="${WORK:-/tmp/workbench}"

# Build from a clean copy so Windows bin/obj folders in the mounted tree do not interfere.
rm -rf "$WORK" && mkdir -p "$WORK"
tar -C "$SRC" --exclude=./artifacts --exclude=./.git --exclude=bin --exclude=obj -cf - . | tar -C "$WORK" -xf -
cd "$WORK"

dotnet build BlazorWorkbench.Web.slnf -c Debug
dotnet run --project src/BlazorWorkbench.Cli --no-build -- install-browsers --with-deps

# Bootstrap: until Linux baselines are committed for a hosting model, report missing screenshots without failing so
# the uploaded report can be reviewed and approved. Once baselines exist, every difference fails the run.
mode() {
  if [ -d "samples/SampleWorkbench/baselines/$1" ]; then
    echo ""
  else
    echo "No baselines in samples/SampleWorkbench/baselines/$1 yet: approve this run's screenshots to enable strict checks." >&2
    echo "--allow-missing-baselines"
  fi
}

status=0
dotnet run --project src/BlazorWorkbench.Cli --no-build -- test --no-build --visual --no-interactions $(mode linux) \
  -o "$SRC/artifacts/visual-linux-server" || status=$?
dotnet run --project src/BlazorWorkbench.Cli --no-build -- test --no-build --visual --no-interactions $(mode linux-webassembly) \
  -p samples/SampleWorkbench.Wasm/SampleWorkbench.Wasm.csproj -o "$SRC/artifacts/visual-linux-webassembly" || status=$?
exit $status
