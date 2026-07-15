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
- ID 採番サービスの呼び出し
- YAML 入出力サービスの呼び出し
- 検証結果の UI 返却

### Domain Model

USDM の構造を表現する。

責務:

- 背景
- 上位要求
- 下位要求
- 仕様
- ノード位置
- 親子関係
- ID ルール

### Persistence

YAML 形式で保存、読み込みを行う。

責務:

- YAML へのシリアライズ
- YAML からのデシリアライズ
- バージョン情報の読み書き
- 読み込み時の互換性対応

## 5. 主要コンポーネント

| コンポーネント | 配置 | 役割 |
|---|---|---|
| `UsdmDocument` | Core | 1つの USDM 文書全体 |
| `BackgroundItem` | Core | 背景情報 |
| `RequirementNode` | Core | 上位要求、下位要求 |
| `SpecificationNode` | Core | 仕様 |
| `NodePosition` | Core | キャンバス上の座標 |
| `IdGenerator` | Core | ID 自動採番 |
| `UsdmYamlSerializer` | Core | YAML 保存、読み込み |
| `DocumentValidator` | Core | ID 重複、必須項目、上限件数の検証 |
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
YAML ファイル出力
```

### YAML 読み込み

```text
ファイル選択
  ↓
UsdmYamlSerializer
  ↓
DocumentValidator
  ↓
CanvasState 初期化
  ↓
画面表示
```

## 7. ID 採番方針

要求仕様書の定義に従い、初期値は以下とする。

| 種別 | 形式 | 上限 |
|---|---|---|
| 背景 | `001` | 255件 |
| 上位要求 | `001` | 255件 |
| 下位要求 | `001-001` | 親ごと255件 |
| 仕様 | `001-001-001` | 親ごと255件 |

背景 ID は要求仕様書で `000～255` と記載があるが、YAML 例では `001` が使われている。開始番号は未決定事項として `issues.md` に記録する。

## 8. レイアウト方針

初期実装では、以下の両対応を前提とする。

- 自動配置: ノード追加時に親の近くへ初期配置する
- 手動配置: ユーザーがノードをドラッグして位置調整する

座標保存の有無は未決定だが、設計上は `position` を保持できるモデルにしておく。保存対象にするかどうかは `issues.md` の決定に従う。

## 9. 描画方針

マインドマップのノードと接続線は、Blazor + SVG を第一候補とする。

理由:

- ノード間接続線を描画しやすい
- ズーム、パンとの相性がよい
- ベジェ曲線や折れ線に対応しやすい
- Canvas より DOM 要素として扱いやすく、初期実装の保守性が高い

ノード本体は HTML 要素として重ねる方式、または SVG 内に描画する方式のどちらも可能とする。入力やダイアログとの相性を考えると、初期実装では HTML ノード + SVG 接続線を推奨する。

## 10. エラーハンドリング

想定する検証エラー:

- YAML の構文エラー
- 必須項目不足
- ID 重複
- ID 形式不正
- 親参照の不整合
- 上限件数超過

検証エラーは読み込み時に一覧表示し、可能であれば該当ノードへ移動できるようにする。

## 11. テスト方針

Core 層を中心にテストする。

- ID 採番
- YAML 読み込み
- YAML 保存
- 読み込み後に保存して構造が維持されること
- 必須項目検証
- 上限件数検証
- 親子関係検証

UI 操作テストは、初期段階では手動確認を中心とし、安定後に Playwright 等の導入を検討する。
