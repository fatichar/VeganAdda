#!/usr/bin/env bash
set -euo pipefail

publish_dir="${1:?Supply the dotnet publish directory}"
layers_dir="${2:?Supply an empty layer output directory}"
[[ -d "$publish_dir" ]] || { echo "Publish directory does not exist: $publish_dir" >&2; exit 1; }
[[ ! -e "$layers_dir" ]] || { echo "Layer output directory already exists: $layers_dir" >&2; exit 1; }

mkdir -p "$layers_dir"/{dependencies,framework,assets,application}
file_count=0
while IFS= read -r -d '' file; do
  relative="${file#"$publish_dir"/}"
  name="${relative##*/}"

  case "$relative" in
    VeganAdda.*|*/VeganAdda.*)
      layer=application
      ;;
    wwwroot/_framework/*)
      case "$name" in
        dotnet.native.*|dotnet.runtime.*)
          layer=framework
          ;;
        VeganAdda.*|blazor.boot*|dotnet.js*|dotnet.*.js*)
          layer=application
          ;;
        *)
          layer=framework
          ;;
      esac
      ;;
    wwwroot/*)
      layer=assets
      ;;
    runtimes/*|*/runtimes/*|*.dll|*.pdb|*.xml)
      layer=dependencies
      ;;
    *)
      layer=application
      ;;
  esac

  destination="$layers_dir/$layer/$relative"
  mkdir -p "$(dirname -- "$destination")"
  cp -p -- "$file" "$destination"
  ((file_count += 1))
done < <(find "$publish_dir" -type f -print0)

(( file_count > 0 )) || { echo "Publish directory is empty: $publish_dir" >&2; exit 1; }
echo "Split $file_count published files into reusable Docker layers."
