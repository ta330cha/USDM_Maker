# USDM_MindMap_Editor ドキュメント

作成日: 2026-07-15

## 概要

USDM_MindMap_Editor は、USDM（Universal Specification Describing Manner）の要求仕様をマインドマップ形式で整理・編集する Web アプリケーションである。

要求、理由、仕様をノードとして追加し、要求から仕様までの関係を視覚的に扱えるようにする。保存形式は YAML とし、Git 管理しやすい人間可読なテキストとして保持する。

## 目的

- 要求整理を視覚的に行えるようにする
- 上位要求、下位要求、仕様の階層関係を明確にする
- 要求から仕様へのトレーサビリティを維持する
- Git で差分確認しやすい YAML 形式で保存する
- ID 採番を自動化し、編集時の手作業を減らす

## 想定アプリケーション

- 種別: ローカル Web アプリケーション
- 技術: ASP.NET Core + Blazor Web App
- 実行環境: macOS / Apple Silicon を主対象
- 初期開発: `dotnet run` によるローカル起動
- 保存形式: YAML ファイル

## ローカル動作確認（macOS）

.NET 10 SDKを用意し、Git・作業・ソリューション共通のルートから起動する。

```bash
cd /Users/Taichiro/work/USDM_Maker
./run-usdm.sh
```

Safariで `http://127.0.0.1:5223` を開く。起動対象は確認専用ホストではなく通常のWebアプリで、既定では復元・ビルドも実行する。停止は同じターミナルで `Control+C` とする。既存の別サーバーは停止せず、ポートが使用中の場合は `--port 5230` 等を指定する。

```bash
# 既存の標準出力・標準エラーをリポジトリ直下の logs へ保存
./run-usdm.sh --log

# ビルド済みのアプリを別ポートで起動
./run-usdm.sh --no-build --port 5230

./run-usdm.sh --help
```

`--log` の保存先は `/Users/Taichiro/work/USDM_Maker/logs/usdm-日時-PID.log`。起動ごとに新規作成し、既存ログは上書きしない。端末にはURLと保存先を表示し、その後の動作ログはファイルだけへ出力する。起動・ビルド・ASP.NET Coreのコンソールログ・標準エラーを記録し、ログフォルダ・ファイル作成失敗や起動失敗は非ゼロ終了する。実行途中のディスク容量監視、ログの自動削除・ローテーションは追加していない。

現在のログ設定は `Default=Information`、`Microsoft.AspNetCore=Warning`。ノード操作ごとの監査ログやブラウザのJavaScriptコンソール収集は実装されておらず、本ランチャーも追加しない。ログは例外情報・ローカルパス等を含む可能性があるためGit対象外とする。

既存のHTTPSリダイレクト処理により、HTTP起動では `Failed to determine the https port for redirect.` という警告が記録される場合がある。ランチャー追加時の確認では両モードでHTTP 200とControl+C後のポート解放を確認した。ランチャーの追加に伴うアプリ側のHTTPS設定変更は行っていない。CLI自動試験はリポジトリルートで `node --test tests/shell/*.test.mjs` を実行する。

Git管理するランチャーはリポジトリ直下の [run-usdm.sh](../run-usdm.sh) 本体だけで、旧配置の呼出し用補助シェルは廃止した。本体は呼び出し元のカレントフォルダに依存せず、リポジトリ内のWebプロジェクトを起動する。HTTPのループバック接続・Development環境に限定した動作確認用であり、外部公開や本番運用用ではない。

## フォルダ配置（2026-10-28整理）

`/Users/Taichiro/work/USDM_Maker` をGit・作業・ソリューション共通のルートとし、`.git`、`USDM_MindMap_Editor.sln`、`Directory.Build.props`、`global.json`、`src`、`tests`、`sample`、`docs`、`run-usdm.sh` を直下へ統合した。ソリューション・プロジェクト名、名前空間は変更していない。構成図は [architecture.md](architecture.md) §3を参照する。

文書内の `src/`、`tests/`、`sample/`、`artifacts/`、`logs/` の記載は、このルートを基準とする。過去の画面・計測成果物はルートの `artifacts/` へ衝突なく統合し、検証時点の数値・結果は維持した。フォルダ移動は過去試験の再実施を意味しない。確認用ホストの手順は [VisualTests/README.md](../tests/USDM_MindMap_Editor.VisualTests/README.md) に記載する。

## ドキュメント構成

| ファイル | 内容 |
|---|---|
| `requirements_specification.md` | 要求仕様の元資料 |
| `issues.md` | 検討事項、決定内容、対応状態 |
| `architecture.md` | アプリケーション構成、責務分担 |
| `UI.md` | 画面、操作、ノード表示仕様 |
| `DataModel.md` | YAML と内部データモデル |
| `LayoutDesign.md` | ノード配置、座標系、描画、性能方針 |
| `development_environment_setup.md` | macOS 開発環境構築手順 |
| `development_environment_options.md` | 開発環境・技術選定メモ |

## 初期スコープ

初期実装では、以下を対象とする。

- キャンバス上でのノード表示
- 背景、上位要求、下位要求、仕様の追加
- 要求、理由、仕様本文の編集
- 単一親の親子関係の管理
- 仕様に対する関連下位要求の記録
- ID 自動採番
- 各採番範囲65535件を上限とする ID 管理
- 自動整列・自動配置
- 固定配色と理由の折りたたみ表示
- Undo / Redo (`Cmd+Z` / `Cmd+Y`)
- macOS のコピー＆ペースト (`Cmd+C` / `Cmd+V`)
- 要求、理由、仕様を対象とするノード検索
- 親ノード削除時の配下全削除と削除確認
- YAML のブラウザダウンロード保存
- YAML からの読み込み
- 最終保存ソフトウェアバージョンと YAML 形式バージョンの分離記録、旧形式 YAML の変換
- ズーム、パン
- エクスポートメニューからの USDM 表ダウンロード
- エクスポート時の理由未入力警告

UI 自動テストは、初期段階では Core 層の単体テストと手動確認を優先する。キャンバス操作が安定した段階で、Playwright による主要操作の自動テストを追加する。

親子関係は単一親とする。上位要求は1つの背景、下位要求は1つの上位要求、仕様は1つの下位要求にのみ属する。仕様に関連する下位要求がある場合は、親子関係にせず関連 ID として保存する。

背景単位の配置位置を保存し、ツリー内部は自動整列・自動配置で再現する。2026-10-27に、空白ダブルクリックによる背景追加、背景本体のドラッグによるツリー全体移動、重複先から開始位置への復元、子追加・理由展開・サイズ補正による他ツリーの自動移動を実装した。`DocumentEditingSession` が文書・ビュー・確定位置を一括反映し、背景単位の再計算では未変更ツリーを再利用する。細粒度の局所再計算とRevision付き実測キャッシュは残課題である。詳細は [LayoutDesign.md](LayoutDesign.md) §7・9.5、今回の実UI確認範囲と制約は [QA_Q_sheet.md](QA_Q_sheet.md) を参照する。

2026-10-28には、自動重複解消を変更前のツリー外接矩形に基づく右・下の押し出しへ変更した。操作元を固定し、連鎖する各衝突で方向を選ぶ。斜め・変更前情報なし等の補完は暫定ルールとしてIssue #005で利用評価を続ける。背景ドラッグの重複拒否・開始位置復元と形式3は変更せず、新方式の確認結果は過去の下方のみの試験と区別する。

接続線はベジェ曲線とし、YAML の保存はブラウザダウンロードとして行う。理由は任意だが、USDM 表などのファイルエクスポート時には未入力を警告する。Google Drive 連携とユーザーによる配色変更は将来対応とする。

YAML の `version` には最終保存時の Application Version、`format_version` には YAML 構造の形式バージョンを記録する。現行Coreは形式1・2・3を読み込み、形式3を保存する。形式別Readerは構造を復元し、`DocumentEditingSession` が旧形式や位置欠落を初期配置で補完して、必要な重複補正を通知する。Serializerは全背景の確定位置が揃った文書だけを保存し、自らレイアウトを行わない。Application Versionは `0.1.0` を維持する。

今回のYAML対応はCoreまでであり、ツールバーの「保存」「開く」は未接続のままとする。上記の初期スコープは完成済み一覧ではない。本文編集・削除・検索などの未実装機能を今回一括実装せず、Undo / Redoの履歴範囲（Issue #026）、ビューポートだけの変更や離脱確認（Issue #027）も未決定事項として維持する。

`view` 状態の永続化を初めて導入するリリースでは Application Version の Minor を上げる。具体的な番号はリリース計画で決定する。

## 開発方針

ASP.NET Core + Blazor Web App を前提に、C# 中心で実装する。マインドマップ描画は Blazor コンポーネントと SVG / HTML / CSS を組み合わせて実現する。

データ処理、ID 採番、YAML 入出力、検証処理は Core 層へ分離し、UI 層から独立してテストできる構成とする。
