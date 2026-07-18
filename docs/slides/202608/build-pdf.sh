#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

INPUT_MD="${1:-"${SCRIPT_DIR}/USDM_MindMap_Editor_Presentation.md"}"
OUTPUT_PDF="${2:-"${SCRIPT_DIR}/USDM_MindMap_Editor_Presentation.pdf"}"
THEME_CSS="${SCRIPT_DIR}/a4-landscape.css"

if ! command -v node >/dev/null 2>&1; then
  cat >&2 <<'EOF'
Error: node が見つかりません。

必要なツール:
  - Node.js 20 以上を推奨
  - npm / npx

macOS での導入例:
  brew install node

EOF
  exit 1
fi

if ! command -v npx >/dev/null 2>&1; then
  cat >&2 <<'EOF'
Error: npx が見つかりません。

Node.js / npm を導入してください。

macOS での導入例:
  brew install node

EOF
  exit 1
fi

if [[ ! -f "${INPUT_MD}" ]]; then
  echo "Error: Markdownファイルが見つかりません: ${INPUT_MD}" >&2
  exit 1
fi

if [[ ! -f "${THEME_CSS}" ]]; then
  echo "Error: CSSファイルが見つかりません: ${THEME_CSS}" >&2
  exit 1
fi

mkdir -p "$(dirname "${OUTPUT_PDF}")"
NPM_CACHE_DIR="${TMPDIR:-/tmp}/usdm-maker-marp-npm-cache"
mkdir -p "${NPM_CACHE_DIR}"

export npm_config_cache="${NPM_CACHE_DIR}"

npx --yes @marp-team/marp-cli@latest \
  --pdf \
  --allow-local-files \
  --theme "${THEME_CSS}" \
  --output "${OUTPUT_PDF}" \
  "${INPUT_MD}"

echo "PDF generated: ${OUTPUT_PDF}"
