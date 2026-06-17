#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
output_path="${1:-$repo_root/stadia_bridge}"

cxx="${CXX:-g++}"

"$cxx" \
  -std=c++17 \
  -O2 \
  -pthread \
  "$script_dir/stadia_bridge.cpp" \
  -o "$output_path"

chmod +x "$output_path"
echo "Built $output_path"
