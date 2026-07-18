# USDM MindMap Editor アーキテクチャ設計書

作成日: 2026-07-15

## 1. 目的

本書は、USDM MindMap Editor のアプリケーション構成、主要コンポーネント、責務分担、処理方針を定義する。

## 2. 技術前提

- 実装言語: C#
- Web フレームワーク: ASP.NET Core + Blazor Web App
- 対象 .NET: .NET 10
- UI: Blazor コンポーネント、HTML、CSS、SVG
- 保存形式: YAML
- 実行方式: ローカル Web アプリケーション
- 開発 OS: macOS / Apple Silicon

## 3. ソリューション構成

推奨構成:

```text
USDM_Maker/
  USDM_Maker.sln
  global.json
  src/
    USDM_Maker.Core/
    USDM_Maker.Web/
  tests/
    USDM_Maker.Core.Tests/
  docs/
```

| プロジェクト | 役割 |
|---|---|
| `USDM_Maker.Core` | USDM データモデル、ID 採番、YAML 入出力、検証、レイアウト計算 |
| `USDM_Maker.Web` | Blazor Web App、画面、ユーザー操作、ファイル入出力 UI |
| `USDM_Maker.Core.Tests` | Core 層の単体テスト |

## 4. レイヤー構成

```text
Web UI
  ↓
Application Service
  ↓
Domain Model
  ↓
Persistence
```

### Web UI

Blazor コンポーネントで画面を構成する。

責務:

- キャンバス表示
- ノード表示
- 入力ダイアログ表示
- ユーザー操作の受付
- YAML ファイルの読み込み、保存操作

### Application Service

UI とドメイン処理の間をつなぐ。

責務:

- ノード追加、編集、削除のユースケース実行
- コピー、ペースト、検索のユースケース実行
- ID 採番サービスの呼び出し
- YAML 入出力サービスの呼び出し
- USDM 表エクスポートの実行
- 検証結果の UI 返却

### Domain Model

USDM の構造を表現する。

責務:

- 背景
- 上位要求
- 下位要求
- 仕様
- 単一親の親子関係
- 仕様から関連下位要求への参照
- ID ルール
- 自動レイアウトの対象となるツリー構造

### Persistence

YAML 形式で保存、読み込みを行う。

責務:

- YAML へのシリアライズ
- YAML からのデシリアライズ
- ブラウザのダウンロードとしての YAML 出力
- 保存時のソフトウェアバージョンの記録
- ファイルバージョンに応じた読み込みと現在の内部モデルへの変換

## 5. 主要コンポーネント

| コンポーネント | 配置 | 役割 |
|---|---|---|
| `UsdmDocument` | Core | 1つの USDM 文書全体 |
| `BackgroundItem` | Core | 背景情報 |
| `RequirementNode` | Core | 上位要求、下位要求と単一の親 ID |
| `SpecificationNode` | Core | 仕様、所属先の下位要求 ID、関連下位要求 ID |
| `IdGenerator` | Core | ID 自動採番 |
| `AutoLayoutService` | Core | ツリー構造からノード位置を自動計算 |
| `UsdmYamlSerializer` | Core | 固定順 YAML の保存、読み込み |
| `UsdmYamlReaderFactory` | Core | YAML の `version` から対応する読み取り用クラスを選択 |
| `IUsdmVersionedReader` | Core | 特定のファイルバージョンを現在の内部モデルへ読み込む |
| `UsdmDocumentMigrator` | Core | 旧バージョンの文書を現在の内部モデルへ変換 |
| `UsdmTableExporter` | Core | USDM 表への変換とエクスポート用データの生成 |
| `DocumentValidator` | Core | ID 重複、必須項目、上限件数の検証 |
| `ExportWarningService` | Core | 理由未入力など、エクスポート時の警告を収集 |
| `NodeClipboardService` | Web | macOS のコピー、ペースト状態を管理 |
| `NodeSearchService` | Web | 要求、理由、仕様の検索と一致ノードの特定 |
| `CanvasState` | Web | ズーム、パン、選択状態 |
| `MindMapCanvas` | Web | マインドマップ描画 |
| `NodeEditorDialog` | Web | ノード入力、編集 |

## 6. データフロー

### 新規ノード追加

```text
ユーザー操作
  ↓
Blazor コンポーネント
  ↓
Application Service
  ↓
IdGenerator
  ↓
UsdmDocument 更新
  ↓
AutoLayoutService
  ↓
画面再描画
```

### YAML 保存

```text
保存操作
  ↓
DocumentValidator
  ↓
UsdmYamlSerializer
  ↓
ブラウザのダウンロード
```

### YAML 読み込み

```text
ファイル選択
  ↓
UsdmYamlReaderFactory
  ↓
IUsdmVersionedReader
  ↓
UsdmDocumentMigrator
  ↓
DocumentValidator
  ↓
AutoLayoutService
  ↓
CanvasState 初期化
  ↓
画面表示
```

### USDM 表エクスポート

```text
エクスポートメニュー
  ↓
DocumentValidator
  ↓
ExportWarningService
  ↓
UsdmTableExporter
  ↓
ブラウザのダウンロード
```

## 7. ID 採番方針

要求仕様書の定義に従い、初期値は以下とする。

| 種別 | 形式 | 上限 |
|---|---|---|
| 背景 | `001` | 255件 |
| 上位要求 | `001` | 255件 |
| 下位要求 | `001-001` | 親ごと255件 |
| 仕様 | `001-001-001` | 親ごと255件 |

背景 ID は `001` から開始し、`000` は使用しない。初期版の上限は各階層あたり255件とする。

## 8. 親子関係と関連要求の方針

- 背景は最上位ノードとし、上位要求は必ず1つの背景に属する。
- 下位要求は必ず1つの上位要求に属する。
- 仕様は必ず1つの下位要求に属する。
- 上位要求、下位要求、仕様の複数親は認めない。
- 仕様に関連する下位要求がある場合は、`related_requirement_ids` に ID を保存する。
- 関連下位要求は参照情報であり、仕様の親子関係、ID 採番、標準のツリー接続線を変更しない。

`DocumentValidator` は、親 ID の存在、親の種別、ID 接頭辞、関連下位要求 ID の存在と重複を検証する。理由の未入力は YAML 保存時のエラーにせず、エクスポート時に `ExportWarningService` が警告として扱う。

## 9. レイアウト方針

初期版は自動整列かつ自動配置とする。ノードの追加、削除、YAML 読み込み後に `AutoLayoutService` が位置を再計算する。

ノード位置は YAML に保存せず、ユーザーによる手動移動も提供しない。手動配置と座標保存は将来拡張とする。

## 10. 描画方針

マインドマップのノードと接続線は Blazor + SVG で描画し、接続線はベジェ曲線とする。

理由:

- ノード間接続線を描画しやすい
- ズーム、パンとの相性がよい
- 親子関係をベジェ曲線で視覚的に追いやすい
- Canvas より DOM 要素として扱いやすく、初期実装の保守性が高い

ノード本体は HTML 要素として重ねる方式、または SVG 内に描画する方式のどちらも可能とする。入力やダイアログとの相性を考えると、初期実装では HTML ノード + SVG 接続線を推奨する。

## 11. エラーハンドリング

想定する検証エラー:

- YAML の構文エラー
- 未対応のファイルバージョン
- 旧ファイル形式からの変換エラー
- 必須項目不足
- ID 重複
- ID 形式不正
- 親参照の不整合
- 複数または未設定の親
- 関連下位要求 ID の参照先不正
- 上限件数超過

エクスポート時の理由未入力は警告であり、YAML 保存時のエラーにはしない。

検証エラーは読み込み時に一覧表示し、可能であれば該当ノードへ移動できるようにする。

## 12. テスト方針

Core 層を中心にテストする。

- ID 採番
- YAML 読み込み
- YAML 保存
- 読み込み後に保存して構造が維持されること
- 必須項目検証
- 上限件数検証
- 親子関係検証
- 関連下位要求の参照検証
- 自動レイアウトが同一入力から同一配置を再現すること
- USDM 表への変換結果が要求・理由・仕様の関係を保持すること
- 理由未入力時に YAML 保存は成功し、エクスポート時だけ警告されること
- 同じ文書を繰り返し保存しても YAML のバイト列が変わらないこと
- 現在のソフトウェアバージョンが `version` に保存されること
- 旧バージョンの YAML を現在の内部モデルへ変換できること
- 変換後に保存すると `version` が現在のソフトウェアバージョンへ更新されること

初期段階では Core 層の単体テストを優先し、UI 操作は手動確認で進める。キャンバス操作が安定した段階で Playwright による主要操作の UI 自動テストを追加する。

性能面は基本機能の実装後に再評価する。100、500、1,000ノード程度の測定用データを用意し、描画、自動レイアウト、YAML 読み書き、メモリ使用量を計測する。
