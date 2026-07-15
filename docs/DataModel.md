# USDM MindMap Editor データモデル設計書

作成日: 2026-07-15

## 1. 目的

本書は、USDM MindMap Editor で扱う内部データモデルと YAML 保存形式を定義する。

## 2. 基本方針

- 保存形式は YAML とする
- Git 差分で読みやすい構造にする
- ID は自動採番する
- 要求から仕様までの親子関係を保持する
- 将来の座標保存、複数親、共有仕様に備えて拡張余地を残す

## 3. 文書モデル

```text
UsdmDocument
  ├─ Metadata
  ├─ Background[]
  └─ Requirements[]
       ├─ Children[]
       │    └─ Specs[]
       └─ Position
```

## 4. エンティティ

### UsdmDocument

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `version` | string | 任意 | YAML スキーマバージョン |
| `title` | string | 任意 | 文書タイトル |
| `background` | BackgroundItem[] | 任意 | 背景一覧 |
| `requirements` | RequirementNode[] | 必須 | 上位要求一覧 |

### BackgroundItem

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `id` | string | 必須 | 背景 ID |
| `text` | string | 必須 | 背景本文 |
| `position` | NodePosition | 任意 | キャンバス座標 |

### RequirementNode

上位要求と下位要求は同じ構造で扱う。

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `id` | string | 必須 | 要求 ID |
| `request` | string | 必須 | 要求本文 |
| `reason` | string | 必須 | 理由 |
| `position` | NodePosition | 任意 | キャンバス座標 |
| `children` | RequirementNode[] | 任意 | 下位要求 |
| `specs` | SpecificationNode[] | 任意 | 仕様一覧 |

### SpecificationNode

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `id` | string | 必須 | 仕様 ID |
| `spec` | string | 必須 | 仕様本文 |
| `position` | NodePosition | 任意 | キャンバス座標 |

### NodePosition

| 項目 | 型 | 必須 | 説明 |
|---|---|---|---|
| `x` | number | 必須 | キャンバス X 座標 |
| `y` | number | 必須 | キャンバス Y 座標 |

## 5. ID 形式

| 種別 | 形式 | 例 | 上限 |
|---|---|---|---|
| 背景 | 3桁 | `001` | 255件 |
| 上位要求 | 3桁 | `001` | 255件 |
| 下位要求 | 親ID + 3桁 | `001-001` | 親ごと255件 |
| 仕様 | 親ID + 3桁 | `001-001-001` | 親ごと255件 |

背景 ID の開始値は要求仕様書内で `000～255` と記載されている一方、例では `001` が使用されている。開始値は未決定事項として扱う。

## 6. YAML 形式

初期推奨形式:

```yaml
version: "1"
title: USDM MindMap

background:
  - id: "001"
    text: 顧客が簡単に操作できること
    position:
      x: 80
      y: 80

requirements:
  - id: "001"
    request: 操作はマウスだけで完結する
    reason: 誰でも利用可能にするため
    position:
      x: 320
      y: 120
    children:
      - id: "001-001"
        request: 左クリックのみで編集可能
        reason: 学習コスト削減
        position:
          x: 320
          y: 320
        specs:
          - id: "001-001-001"
            spec: 左クリックで入力ダイアログ表示
            position:
              x: 680
              y: 320
          - id: "001-001-002"
            spec: ダブルクリック不要
            position:
              x: 680
              y: 440
```

## 7. YAML 保存ルール

- ID は文字列として保存する
- ID の先頭ゼロを維持する
- 配列順は画面上の並び、または作成順を維持する
- 未入力の任意項目は保存しない
- `position` は座標保存を採用する場合のみ保存する
- YAML スキーマ変更に備えて `version` を付与する

## 8. 検証ルール

読み込み時と保存前に以下を検証する。

| ルール | 内容 |
|---|---|
| 必須項目 | `request`、`reason`、`spec`、`text` が空でないこと |
| ID 形式 | 種別ごとの ID 形式に一致すること |
| ID 重複 | 同一階層内で重複しないこと |
| 上限件数 | 255件上限を超えないこと |
| 親子整合性 | 下位要求、仕様の ID が親 ID と一致すること |
| 座標 | 保存する場合、数値であること |

## 9. C# モデル案

```csharp
public sealed class UsdmDocument
{
    public string Version { get; set; } = "1";
    public string? Title { get; set; }
    public List<BackgroundItem> Background { get; set; } = new();
    public List<RequirementNode> Requirements { get; set; } = new();
}

public sealed class BackgroundItem
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public NodePosition? Position { get; set; }
}

public sealed class RequirementNode
{
    public string Id { get; set; } = "";
    public string Request { get; set; } = "";
    public string Reason { get; set; } = "";
    public NodePosition? Position { get; set; }
    public List<RequirementNode> Children { get; set; } = new();
    public List<SpecificationNode> Specs { get; set; } = new();
}

public sealed class SpecificationNode
{
    public string Id { get; set; } = "";
    public string Spec { get; set; } = "";
    public NodePosition? Position { get; set; }
}

public sealed record NodePosition(double X, double Y);
```

## 10. 将来拡張

以下は現時点で未決定だが、将来対応を考慮する。

- 複数親を持つ要求
- 複数要求から共有される仕様
- ノードの表示状態
- 折り畳み状態
- ノード色
- コメント
- 変更履歴

複数親や共有仕様を採用する場合、階層型 YAML だけでは表現が難しくなるため、ノード一覧とリンク一覧を分ける形式への移行を検討する。
