# USDM_MindMap_Editor データモデル設計書

作成日: 2026-07-15

## 1. 目的

本書は、USDM_MindMap_Editor で扱う内部データモデルと YAML 保存形式を定義する。

## 2. 基本方針

- 保存形式は YAML とする
- Git 差分で読みやすい構造にする
- 同じ文書からは常に同じ YAML を出力する
- ID は自動採番する
- 親子関係は ID で明示して検証可能にする
- 上位要求は1つの背景、下位要求は1つの上位要求、仕様は1つの下位要求にのみ属する
- 仕様と所属先以外の下位要求との関係は、親子関係ではなく関連情報として保存する
- ノード座標は保存せず、読み込み時に自動レイアウトする

## 3. 文書モデル

YAML はノードを種別ごとの一覧として保存する。親子関係は親 ID の項目で表すため、読み込み時に関係の検証とツリー構築を行う。

```text
保存モデル
  UsdmDocument
    ├─ Backgrounds[]
    ├─ Requirements[]
    └─ Specifications[]

表示モデル
  Background
    └─ Top-level Requirement
         └─ Lower-level Requirement
              └─ Specification
                   └─ Related lower requirements[] (reference only)
```

`Requirements` と `Specifications` の一覧は保存上の形式であり、画面表示時は親 ID を使って上記の階層に組み立てる。関連する下位要求はツリーに加えない参照である。

## 4. エンティティ

### UsdmDocument

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `version` | string | 必須 | 最終保存時のソフトウェアバージョン |
| `title` | string | 任意 | 文書タイトル |
| `backgrounds` | BackgroundItem[] | 必須 | 背景一覧 |
| `requirements` | RequirementNode[] | 必須 | 上位要求と下位要求の一覧 |
| `specifications` | SpecificationNode[] | 必須 | 仕様一覧 |

### BackgroundItem

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `id` | string | 必須 | 背景 ID |
| `text` | string | 必須 | 背景本文 |

### RequirementNode

上位要求と下位要求は同じ構造で保存する。ID の形式により種別を判定する。

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `id` | string | 必須 | 要求 ID |
| `request` | string | 必須 | 要求本文 |
| `reason` | string | 任意 | 理由 |
| `parent_background_id` | string | 上位要求では必須 | 所属する背景 ID |
| `parent_requirement_id` | string | 下位要求では必須 | 所属する上位要求 ID |

上位要求では `parent_background_id` のみを設定し、下位要求では `parent_requirement_id` のみを設定する。両方の設定や未設定は認めない。

### SpecificationNode

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `id` | string | 必須 | 仕様 ID |
| `specification` | string | 必須 | 仕様本文 |
| `parent_requirement_id` | string | 必須 | 所属する下位要求 ID |
| `related_requirement_ids` | string[] | 任意 | 関連する下位要求 ID の一覧 |

`related_requirement_ids` はトレーサビリティの補助情報である。仕様の所属先を変更せず、接続線による親子関係も作らない。

## 5. 関係と ID 形式

| 種別 | ID 形式 | 親 | 例 | 上限 |
|---|---|---|---|---|
| 背景 | 3〜5桁 | なし | `001` | 65535件 |
| 上位要求 | 3〜5桁 | 背景 1件 | `001` | 65535件 |
| 下位要求 | 上位要求 ID + 3〜5桁 | 上位要求 1件 | `001-001` | 親ごと65535件 |
| 仕様 | 下位要求 ID + 3〜5桁 | 下位要求 1件 | `001-001-001` | 親ごと65535件 |

各 ID セグメントは `001` から `65535` の範囲で使用する。`001` から `999` は3桁ゼロ埋めとし、`1000` から `65535` はそのまま表記する。背景 ID は `001` から開始する。`000` はアプリケーションでは使用しない。

## 6. YAML 形式

初期推奨形式:

```yaml
version: "0.1.0"
title: USDM_MindMap_Editor

backgrounds:
  - id: "001"
    text: 顧客が簡単に操作できること

requirements:
  - id: "001"
    request: 操作はマウスだけで完結する
    reason: 誰でも利用可能にするため
    parent_background_id: "001"

  - id: "001-001"
    request: 左クリックのみで編集可能
    reason: 学習コスト削減
    parent_requirement_id: "001"

  - id: "002"
    request: 編集内容を失わない
    reason: 入力途中の作業を保護するため
    parent_background_id: "001"

  - id: "002-001"
    request: 編集内容を失わない
    reason: 入力途中の作業を保護するため
    parent_requirement_id: "002"

specifications:
  - id: "001-001-001"
    specification: 左クリックで入力ダイアログ表示
    parent_requirement_id: "001-001"

  - id: "001-001-002"
    specification: 編集内容を一定時間ごとに保存する
    parent_requirement_id: "001-001"
    related_requirement_ids:
      - "002-001"
```

## 7. YAML 保存ルール

- トップレベルキーは `version`、`title`、`backgrounds`、`requirements`、`specifications` の順で出力する
- `version` には保存時に実行中のソフトウェアバージョンを出力する
- ID は文字列として保存し、先頭ゼロを維持する
- ノード一覧と `related_requirement_ids` は ID の昇順で安定して出力する
- ノード項目は ID、本文、親 ID、関連 ID の順で出力する
- 未入力の任意項目は出力しない。理由が空の場合も `reason` は出力しない
- `related_requirement_ids` は関連先がある場合のみ配列として出力する
- インデントは半角スペース2個、改行コードは LF とする
- 保存日時や自動生成 ID など、文書内容と無関係な情報は出力しない
- ノード座標は保存しない。読み込み時とノード構造の変更時に自動レイアウトする
- ファイル形式の変更に備えて `version` を付与する

## 8. 旧バージョン YAML の読み込み

YAML の `version` を確認し、そのファイルバージョンに対応する読み取り用クラスでデシリアライズする。
ファイル形式を変更するバージョンアップでは、その形式に対応する読み取り用クラスと、現在の内部モデルへ変換する処理を追加する。

変換済み文書は現在の内部モデルとして画面に表示する。保存時には現在のソフトウェアバージョンで `version` を更新し、現行形式の YAML として出力する。

## 9. 検証ルール

読み込み時と保存前に以下を検証する。

| ルール | 内容 |
|---|---|
| 必須項目 | `text`、`request`、`specification` が空でないこと |
| バージョン | `version` が空でなく、保存時のソフトウェアバージョンと一致すること |
| ID 形式 | 種別ごとの ID 形式に一致し、各 ID セグメントが `001`〜`65535` の範囲であること |
| ID 重複 | 同じ種別の ID が重複しないこと |
| 上限件数 | 各採番範囲で65535件上限を超えないこと |
| 上位要求の親 | 既存の背景 ID を `parent_background_id` に1つだけ設定すること |
| 下位要求の親 | 既存の上位要求 ID を `parent_requirement_id` に1つだけ設定すること |
| 仕様の親 | 既存の下位要求 ID を `parent_requirement_id` に1つだけ設定すること |
| ID と親の整合 | 下位要求と仕様の ID 接頭辞が親 ID と一致すること |
| 関連要求 | `related_requirement_ids` が既存の下位要求 ID を重複なく参照すること |

理由が空であることは YAML 保存時の検証エラーにしない。USDM 表、Excel などのファイルエクスポート時だけ、理由が空の要求を警告として収集する。

親子関係の循環は、背景から仕様までの固定4階層と単一親制約により発生しない。

## 10. C# モデル案

```csharp
public sealed class UsdmDocument
{
    public string Version { get; set; } = "";
    public string? Title { get; set; }
    public List<BackgroundItem> Backgrounds { get; set; } = new();
    public List<RequirementNode> Requirements { get; set; } = new();
    public List<SpecificationNode> Specifications { get; set; } = new();
}

public sealed class BackgroundItem
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class RequirementNode
{
    public string Id { get; set; } = "";
    public string Request { get; set; } = "";
    public string? Reason { get; set; }
    public string? ParentBackgroundId { get; set; }
    public string? ParentRequirementId { get; set; }
}

public sealed class SpecificationNode
{
    public string Id { get; set; } = "";
    public string Specification { get; set; } = "";
    public string ParentRequirementId { get; set; } = "";
    public List<string> RelatedRequirementIds { get; set; } = new();
}
```

## 11. レイアウト関連状態

レイアウト関連状態は、文書として YAML に保存する値、実行中だけ保持する派生値、Web UI が管理する表示状態に分離する。座標系と配置規則の詳細は [LayoutDesign.md](LayoutDesign.md) を参照する。

### YAML に保存する値

初期版では、背景、上位要求、下位要求、仕様の本文と親子関係だけを文書データとして保存する。ノード座標、接続線座標、キャンバス範囲は保存しない。

### 実行中だけ保持する派生値

次の値は文書構造と表示条件から計算できるため、YAML には含めない。

- ノードごとのキャンバス座標
- ノード内容、種類、既定幅、表示状態から算出した予測サイズ
- 必要なノードについて DOM から取得した実測サイズ
- 有効な実測サイズがあれば実測値を、なければ予測値を採用した有効サイズ
- 実測時のノード内容、表示状態、幅、フォントなどを含むサイズキャッシュの有効性情報
- キャンバス全体の範囲
- 接続線の始点、終点、制御点
- 現在のビューポートに対する描画対象ノードと接続線
- 自動レイアウトの中間計算結果とキャッシュ

派生値は ID をキーとして文書ノードと対応付けるが、ID 自体を座標や表示順として扱わない。

DOM 実測サイズはブラウザのフォントや表示条件に依存するため、実行時の派生情報とし、原則として YAML へ保存しない。サイズ条件が変化した場合は該当する実測サイズキャッシュを無効化する。

### アプリケーションのレイアウト設定

初期版では次の値を文書ごとのデータではなく、アプリケーションのレイアウト設定として一元管理する。各処理へ個別に埋め込まず、YAML には保存しない。

| 設定 | 初期値 | 状態 |
|---|---:|---|
| 兄弟ノード間の最小間隔（複数背景を含む） | 32px | 確定 |
| 親子階層間の間隔 | 96px | 確定 |
| キャンバス外周余白 | 96px | 確定 |
| 最小ズーム倍率 | 25% | 確定 |
| 初期ズーム倍率 | 100% | 確定 |
| 最大ズーム倍率 | 200% | 確定 |

ノード既定幅、予測高さの計算パラメーター、DOM 実測差分の判定閾値、ズーム倍率の1操作あたりの変化量も同じ設定群で管理する。ただし具体値は未決定であり、現行 CSS のノード幅 `240px` は既定幅の暫定候補とする。将来これらを文書単位またはユーザー単位で保存するかは未決定とする。

### Web UI が管理する表示状態

ズーム倍率、パン位置、ビューポート、選択状態、理由の展開状態は、現行の YAML と C# 文書モデルには含まれない。初期ズーム倍率は100%とするが、現在値とパン位置をブラウザ内だけで保持するか、将来永続化するかは未決定とする。

サブツリーの折り畳み状態、手動配置状態、明示的な兄弟表示順も初期版の保存対象には含めない。初期版は常に自動レイアウトを使用する。これらの将来機能の保存方法は未決定である。

### YAML 互換性への影響

今回の整理による YAML スキーマ変更はない。ノード座標、予測サイズ、DOM 実測サイズ、実測サイズキャッシュ、レイアウト設定、ズーム倍率、パン位置は追加しない。将来、手動配置、折り畳み状態、明示的な表示順などを YAML へ追加する場合は、ファイル形式の変更としてバージョン別読み取りと現在モデルへの変換を追加する。

## 12. 将来拡張

以下は現時点で未決定だが、将来対応を考慮する。

- ノードの表示状態
- サブツリーの折り畳み状態
- ユーザーによるノード配色設定
- コメント
- 変更履歴
- 関連要求の種別や説明
- 手動レイアウトと座標保存

複数親や仕様の共有は採用しない。将来方針を変更する場合は、対応する読み取り用クラスと変換処理を追加し、既存文書を現在の内部モデルへ移行できるようにする。
