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
- ズーム、パン、理由の展開状態は、要求データと分離したプロジェクトのビュー状態として保存する
- 予測サイズ、DOM 実測値、レイアウトキャッシュは保存しない

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
| `format_version` | int | 現行形式では必須 | YAML 構造の形式バージョン。Application Version とは独立して管理する |
| `title` | string | 任意 | 文書タイトル |
| `backgrounds` | BackgroundItem[] | 必須 | 背景一覧 |
| `requirements` | RequirementNode[] | 必須 | 上位要求と下位要求の一覧 |
| `specifications` | SpecificationNode[] | 必須 | 仕様一覧 |
| `view` | DocumentViewState | 旧ファイルでは任意 | 要求データと分離したプロジェクトのビュー状態。現行形式の保存時は出力する |

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

### DocumentViewState

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `zoom` | number | `view` 内で必須 | ズーム倍率。`0.25`〜`2.0` |
| `pan` | PanState | `view` 内で必須 | キャンバスのパン位置 |
| `nodes` | map<string, NodeViewState> | 任意 | ノード単位のビュー状態 |

### PanState

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `x` | number | 必須 | ビューポート上の水平方向パンオフセット |
| `y` | number | 必須 | ビューポート上の垂直方向パンオフセット |

### NodeViewState

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `reason_expanded` | bool | 任意 | 理由を展開している場合は `true`。省略時は `false` |

`nodes` のキーは、背景 ID と上位要求 ID が同じ値を取り得ることを考慮し、`<node-kind>:<id>` 形式の一意な `NodeKey` とする。ノード種別は小文字の `background`、`requirement`、`specification` を使用する。理由の展開状態を持つキーの例は `requirement:001`、`requirement:001-001` である。

### 将来の手動表示順

手動並べ替えを将来追加する場合、背景、上位要求、下位要求、仕様へ任意の表示順情報を持たせる。C# 上の概念名は `DisplayOrder`、型は `int?`、YAML キーは `display_order` とする。

- `display_order` が設定されたノードは値の昇順で並べる。
- 同じ値の場合は ID 昇順で並べる。
- 未設定ノードは設定済みノードの後で ID 昇順に並べる。
- 手動並べ替えを行っていないノードには `display_order` を出力しない。
- v1.0 では手動並べ替え機能と `display_order` の保存を実装しない。

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

次は `view` 対応形式の概念例である。`version` の値は説明用プレースホルダーであり、実際の Application Version はリリース計画で決定する。

```yaml
version: "<Application Version>"
format_version: 2
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

view:
  zoom: 1.0
  pan:
    x: 0
    y: 0
  nodes:
    "requirement:001":
      reason_expanded: true
```

## 7. YAML 保存ルール

- トップレベルキーは `version`、`format_version`、`title`、`backgrounds`、`requirements`、`specifications`、`view` の順で出力する
- `version` には保存時に実行中のソフトウェアバージョンを出力する
- `format_version` には保存時の YAML 形式バージョンを整数で出力する
- ID は文字列として保存し、先頭ゼロを維持する
- ノード一覧と `related_requirement_ids` は、ID セグメントを数値比較した昇順で安定して出力する
- ノード項目は ID、本文、親 ID、関連 ID の順で出力する
- 未入力の任意項目は出力しない。理由が空の場合も `reason` は出力しない
- `related_requirement_ids` は関連先がある場合のみ配列として出力する
- インデントは半角スペース2個、改行コードは LF とする
- 保存日時や自動生成 ID など、文書内容と無関係な情報は出力しない
- ノード座標は保存しない。読み込み時とノード構造の変更時に自動レイアウトする
- `view` は要求データと分離したプロジェクトのビュー状態として保存する
- `view.nodes` はノード種別、ID セグメントの数値順で安定して出力する。デフォルトの折りたたみ状態は省略し、原則として `reason_expanded: true` のノードだけを出力する
- 予測サイズ、DOM 実測サイズ、`LayoutRevision`、サイズキャッシュ、ビューポート判定結果は出力しない
- `version` と `format_version` は役割を混同せず、Application Version の更新だけで `format_version` を変更しない

## 8. 旧バージョン YAML の読み込み

YAML の `format_version` を確認し、その形式バージョンに対応する読み取り用クラスでデシリアライズする。`format_version` が存在しない既存 YAML は形式バージョン1として扱う。

YAML 構造に互換性上意味のある変更を加える場合は `format_version` を更新し、その形式に対応する読み取り用クラスと、現在の内部モデルへ変換する処理を追加する。アプリケーション機能の追加だけでは `format_version` を更新しない。

変換済み文書は現在の内部モデルとして画面に表示する。保存時には `version` を現在の Application Version、`format_version` を現行形式の値へ更新して出力する。

`view` の追加は YAML 構造に意味のある変更であるため、`view` 対応形式を形式バージョン2とする。形式バージョン1または `view` が存在しないファイルは、ズーム `1.0`、パン位置 `(0, 0)`、すべての理由を折りたたんだ状態へ変換する。新しい読み取り処理が旧形式を補完することで後方互換性を維持する。

`view` 状態の永続化を初めて実装するリリースでは Application Version の Minor を上げる。具体的な番号はリリース計画で決定し、`format_version` の値とは連動させない。

## 9. 検証ルール

読み込み時と保存前に以下を検証する。

| ルール | 内容 |
|---|---|
| 必須項目 | `text`、`request`、`specification` が空でないこと |
| Application Version | 読み込み時は `version` が有効な SemVer であること。保存時は実行中のソフトウェアバージョンを出力すること |
| YAML 形式バージョン | `format_version` が対応可能な整数であること。未記載の旧形式は1として読み取ること |
| ID 形式 | 種別ごとの ID 形式に一致し、各 ID セグメントが `001`〜`65535` の範囲であること |
| ID 重複 | 同じ種別の ID が重複しないこと |
| 上限件数 | 各採番範囲で65535件上限を超えないこと |
| 上位要求の親 | 既存の背景 ID を `parent_background_id` に1つだけ設定すること |
| 下位要求の親 | 既存の上位要求 ID を `parent_requirement_id` に1つだけ設定すること |
| 仕様の親 | 既存の下位要求 ID を `parent_requirement_id` に1つだけ設定すること |
| ID と親の整合 | 下位要求と仕様の ID 接頭辞が親 ID と一致すること |
| 関連要求 | `related_requirement_ids` が既存の下位要求 ID を重複なく参照すること |
| ズーム | `view.zoom` が `0.25`〜`2.0` の有限値であること |
| パン | `view.pan.x` と `view.pan.y` が有限値であること |
| ノードビュー | `view.nodes` のキーが既存ノードを種別込みで参照し、理由展開状態は要求ノードだけに設定されること |

理由が空であることは YAML 保存時の検証エラーにしない。USDM 表、Excel などのファイルエクスポート時だけ、理由が空の要求を警告として収集する。

親子関係の循環は、背景から仕様までの固定4階層と単一親制約により発生しない。

## 10. C# モデル案

```csharp
public sealed class UsdmDocument
{
    public string Version { get; set; } = "";
    public int FormatVersion { get; set; } = 2;
    public string? Title { get; set; }
    public List<BackgroundItem> Backgrounds { get; set; } = new();
    public List<RequirementNode> Requirements { get; set; } = new();
    public List<SpecificationNode> Specifications { get; set; } = new();
    public DocumentViewState View { get; set; } = new();
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

public sealed class DocumentViewState
{
    public double Zoom { get; set; } = 1.0;
    public PanState Pan { get; set; } = new();
    public Dictionary<string, NodeViewState> Nodes { get; set; } = new();
}

public sealed class PanState
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class NodeViewState
{
    public bool ReasonExpanded { get; set; }
}
```

## 11. レイアウト関連状態

レイアウト関連状態は、文書として YAML に保存する値、実行中だけ保持する派生値、Web UI が管理する表示状態に分離する。座標系と配置規則の詳細は [LayoutDesign.md](LayoutDesign.md) を参照する。

### YAML に保存する値

初期版では、背景、上位要求、下位要求、仕様の本文と親子関係に加え、要求データから分離した `view` をプロジェクトデータとして保存する。`view` にはズーム倍率、パン位置、理由の展開状態を含める。ノード座標、接続線座標、キャンバス範囲は保存しない。

### 実行中だけ保持する派生値

次の値は文書構造と表示条件から計算できるため、YAML には含めない。

- ノードごとのキャンバス座標
- ノード内容、種類、既定幅、表示状態から算出した予測サイズ
- 必要なノードについて DOM から取得した実測サイズ
- 有効な実測サイズがあれば実測値を、なければ予測値を採用した有効サイズ
- 実測時のノード内容、表示状態、幅、フォントなどを含むサイズキャッシュの有効性情報
- ノード内容変更時に更新する `LayoutRevision`
- キャンバス全体の範囲
- 接続線の接続アンカー、始点、終点、制御点
- 現在のビューポートに対する描画対象ノードと接続線
- 自動レイアウトの中間計算結果とキャッシュ

派生値はノード種別と ID からなる一意な `NodeKey` で文書ノードと対応付ける。ID 自体を座標や表示順として扱わない。

接続アンカーはノード矩形と [LayoutDesign.md](LayoutDesign.md) の接続規則から算出する。親の出力位置を下辺へ変更しても、ノードごとの接続面や曲線情報をYAMLへ追加せず、既存文書を同じデータモデルで読み込む。この表示変更による `format_version` の更新は不要とする。

DOM 実測サイズはブラウザのフォントや表示条件に依存するため、実行時の派生情報とし、YAML へ保存しない。キャッシュは、ノード種別と ID からなる `NodeKey`、`LayoutRevision`、ノード幅、理由展開状態を識別情報とする。サイズ条件が変化した場合は該当するキャッシュを無効化する。

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
| ズーム操作量 | 10% | 確定 |
| DOM 実測差分閾値 | `max(8px, 予測高さの10%)` | 確定 |
| 高さ予測の1行文字数 | 18文字 | 初期予測値 |
| 操作領域高さ | 32px | 初期設定値 |
| 最小ノード高さ | 110px | 確定 |

v1.0 のノード幅は全種別共通の固定値として同じ設定群で管理する。正式初期値は UI 比較後に確定し、`280px` を暫定実装値かつ第一候補、`240px` と `320px` を比較対象とする。高さ予測の左右余白 `16px`、上下余白 `12px`、行高 `22px`、ID 領域 `24px`、セクション間隔 `8px` は暫定設計である。`18文字` は表示制限ではなく予測係数であり、実際の折り返しは DOM 実測で補正する。DOM 実測後もノード高さは `110px` を下回らない。

### Web UI が管理する表示状態

ズーム倍率、パン位置、理由の展開状態は Web UI で操作し、要求データと分離した `view` として YAML へ保存する。ビューポート矩形、選択状態、ホバー状態など一時的な UI 状態は保存しない。

サブツリーの折り畳み状態、手動配置状態、明示的な兄弟表示順も初期版の保存対象には含めない。初期版は常に自動レイアウトを使用する。将来の手動表示順には任意の `display_order` を使用し、それ以外の将来機能の保存方法は未決定である。

### YAML 互換性への影響

今回の `view` 追加は YAML スキーマ変更であり、形式バージョン2として扱う。新しい読み取り処理は `format_version` がない形式バージョン1と、`view` のない旧ファイルを既定のビュー状態へ変換できる。現行実装の旧デシリアライザーは未知の `view` と `format_version` を無視できるが、旧アプリケーションで再保存するとビュー状態が失われるため、前方互換性は保証しない。

ノード座標、予測サイズ、DOM 実測サイズ、実測サイズキャッシュ、`LayoutRevision`、レイアウト設定、ビューポート判定結果は追加しない。将来、手動配置、サブツリー折り畳み状態、`display_order` などを YAML へ追加する場合も、互換性への影響を判定し、必要な場合だけ `format_version` と形式別読み取り処理を追加する。

## 12. 将来拡張

以下は現時点で未決定だが、将来対応を考慮する。

- ノードの表示状態
- サブツリーの折り畳み状態
- ユーザーによるノード配色設定
- コメント
- 変更履歴
- 関連要求の種別や説明
- 手動レイアウトと座標保存
- `display_order` を編集する手動並べ替え UI と値の割り当て方法

複数親や仕様の共有は採用しない。将来方針を変更する場合は、対応する読み取り用クラスと変換処理を追加し、既存文書を現在の内部モデルへ移行できるようにする。
