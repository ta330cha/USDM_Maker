#!/bin/bash
# macOS's bundled Bash 3.2 is sufficient. No background server is left behind.
set -u

usage() {
    printf '%s\n' \
        'USDM MindMap Editor 動作確認用ランチャー' \
        '使い方: ./run-usdm.sh [--log] [--port PORT] [--no-build]' \
        '' \
        '  --log, --logs  標準出力・標準エラーを親フォルダの logs へ保存' \
        '                動作ログはファイルのみへ出力（通常は端末へ表示）' \
        '  --port PORT    HTTPポート番号（既定: 5223、接続先: 127.0.0.1）' \
        '  --no-build     前回のビルド結果を使う。初回・コード変更後は指定不要' \
        '  -h, --help     この説明を表示' \
        '' \
        '停止: このターミナルで Control+C。ブラウザは自動起動しません。'
}

fail() { printf 'エラー: %s\n' "$1" >&2; exit "${2:-1}"; }

save_log=false
port=5223
skip_build=false
while [ "$#" -gt 0 ]; do
    case "$1" in
        --log|--logs) save_log=true; shift ;;
        --port)
            [ "$#" -ge 2 ] || fail '--port の後にポート番号を指定してください。' 2
            port=$2
            shift 2
            ;;
        --no-build) skip_build=true; shift ;;
        -h|--help) usage; exit 0 ;;
        *) fail "不明な引数です: $1（--help で使い方を表示）" 2 ;;
    esac
done

[[ "$port" =~ ^[0-9]+$ ]] && [ "${#port}" -le 5 ] \
    || fail 'ポート番号は1〜65535の整数にしてください。' 2
port=$((10#$port))
[ "$port" -ge 1 ] && [ "$port" -le 65535 ] \
    || fail 'ポート番号は1〜65535の整数にしてください。' 2

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P) || exit 1
workspace_dir=$(CDPATH= cd -- "$script_dir/.." && pwd -P) || exit 1
project="$script_dir/USDM_MindMap_Editor/src/USDM_MindMap_Editor.Web/USDM_MindMap_Editor.Web.csproj"
[ -f "$project" ] || fail "Webプロジェクトが見つかりません: $project"
command -v dotnet >/dev/null 2>&1 || fail '.NET 10 SDKのdotnetコマンドがPATHにありません。'

url="http://127.0.0.1:$port"
command_args=(dotnet run --project "$project" --no-launch-profile)
if "$skip_build"; then command_args+=(--no-build); fi
command_args+=(-- --urls "$url")

# Explicit content root / working directory also makes invocation from any cwd
# equivalent to running inside the Web project. launchSettings is not required.
cd -- "$(dirname -- "$project")" || exit 1
printf 'USDM MindMap Editor（Development）\nURL: %s\n停止: Control+C\n' "$url"

if "$save_log"; then
    log_dir="$workspace_dir/logs"
    umask 077
    mkdir -p -- "$log_dir" || fail "ログフォルダを作成できません: $log_dir"
    log_file="$log_dir/usdm-$(TZ=Asia/Tokyo date '+%Y%m%d-%H%M%S')-$$.log"
    # Open exclusively and keep the descriptor: never truncate an existing log.
    set -C
    if ! exec 3> "$log_file"; then fail "ログファイルを新規作成できません: $log_file"; fi
    set +C
    printf 'ログ保存先: %s\n動作ログはこのファイルへ出力します。\n' "$log_file"
    exec 1>&3 2>&1 3>&-
fi

printf '[%s] 起動 URL=%s build=%s\n' "$(TZ=Asia/Tokyo date '+%Y-%m-%d %H:%M:%S %Z')" "$url" "$([ "$skip_build" = true ] && printf skip || printf enabled)"
# Replacing the shell preserves dotnet's exit status and foreground Ctrl+C.
exec env DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development "${command_args[@]}"
