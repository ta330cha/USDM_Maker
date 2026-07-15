# USDM MindMap Editor 開発環境検討メモ

作成日: 2026-07-15

## 前提

- 開発 OS: macOS
- CPU: Apple Silicon / M2
- 目的: USDM の要求仕様をマインドマップ形式で編集し、YAML として保存する
- 方針: ASP.NET Core + Blazor Web App を第一候補とする
- 初期開発では Docker を必須にしない
- 個人開発を前提に、構成を重くしすぎない

## 推奨案

### ASP.NET Core + Blazor Web App

第一候補。

構成案:

- `USDM_Maker.Core`
  - USDM データモデル
  - ID 採番
  - YAML 読み書き
  - 検証処理
  - レイアウト計算
- `USDM_Maker.Web`
  - Blazor Web App
  - マインドマップ UI
  - ファイル読み込み、保存
  - ダイアログ、キャンバス操作
- `USDM_Maker.Core.Tests`
  - ID 採番、YAML、検証処理のテスト

利点:

- C# 中心で開発できる。
- ASP.NET Core と Blazor の範囲で Web アプリを完結しやすい。
- Core 層を分離すれば、UI 以外を単体テストしやすい。
- `dotnet run` でローカル起動できる。
- self-contained publish による配布へ移行しやすい。

注意点:

- 高度な図形編集 UI は実装設計が必要。
- React 系に比べると、マインドマップ向け UI 部品の選択肢は少ない。
- SVG、HTML、CSS、JavaScript 連携を一部使う可能性がある。

## 描画方式の候補

### HTML ノード + SVG 接続線

推奨。

利点:

- ノード内のテキスト、ボタン、入力操作を作りやすい。
- SVG で接続線を描きやすい。
- Blazor コンポーネントとして分割しやすい。

欠点:

- 座標変換、ズーム、パンの整合性を丁寧に扱う必要がある。

### SVG 完結

利点:

- ノードと線を同一座標系で扱いやすい。
- 拡大縮小や線描画が自然。

欠点:

- テキスト編集、ボタン、ダイアログとの相性は HTML より劣る。

### Canvas

利点:

- 大量ノードの描画性能を確保しやすい。

欠点:

- ノード内 UI を自前実装する範囲が増える。
- アクセシビリティや選択状態管理が複雑になる。
- 初期実装としては重い。

## YAML ライブラリ候補

### YamlDotNet

推奨候補。

利点:

- .NET で広く使われている。
- シリアライズ、デシリアライズに対応している。
- カスタム変換や命名規則の調整が可能。

注意点:

- Git 差分の読みやすさを重視するため、出力順や null 項目の扱いを調整する必要がある。

## 代替案

### ASP.NET Core API + React / TypeScript

UI の自由度を優先する場合の候補。

利点:

- 図形編集やマインドマップ UI の実装例、ライブラリが多い。
- フロントエンドの表現力が高い。

欠点:

- C# と TypeScript の2系統を保守する必要がある。
- 個人開発では構成がやや重い。
- API とフロントエンドの境界設計が必要になる。

### Blazor WebAssembly

ブラウザ単体実行を重視する場合の候補。

利点:

- クライアント側でも C# を使える。
- 静的ホスティングと相性がよい。

欠点:

- ローカルファイル保存や任意パス操作にブラウザ制約がある。
- 初期ロードが重くなりやすい。
- ローカルツールとしては Blazor Server / Web App より扱いづらい場面がある。

### Avalonia UI

Web ではなく C# デスクトップアプリとして作る場合の候補。

利点:

- C# のみで完結しやすい。
- ローカルファイルアクセスが自然。
- macOS アプリとして配布しやすい。

欠点:

- Web アプリケーション前提から外れる。
- ブラウザで使う構成にはならない。
- Web UI としての将来展開はしにくい。

### Electron / Tauri + Web フロントエンド

デスクトップアプリ配布を強く意識する場合の候補。

利点:

- ローカルアプリらしい配布ができる。
- ファイルアクセスやメニュー統合を作りやすい。

欠点:

- 初期構成が重くなる。
- C# 中心の開発方針から外れやすい。
- 個人開発の初期段階では過剰。

### Docker / Rancher

実行環境の固定化には有効だが、初期開発の第一候補にはしない。

Docker の利点:

- .NET SDK やランタイムの差異を吸収しやすい。
- 将来の配布・検証環境を固定できる。

Docker の注意点:

- ローカルファイル保存やファイル選択との相性を考える必要がある。
- Apple Silicon では arm64 イメージ前提で考える必要がある。
- 個人開発の初期段階では手間が増えやすい。

Rancher / Kubernetes 系を初期開発に使う理由は薄い。ローカル Web アプリとしては過剰。

## 推奨する初期方針

1. Docker を使わず、macOS 上で `.NET 10 LTS` の ASP.NET Core / Blazor Web App として作る。
2. Core 層と Web 層を分ける。
3. YAML 読み書きは `YamlDotNet` を候補に検証する。
4. マインドマップ描画は HTML ノード + SVG 接続線を第一候補にする。
5. まずは上位要求、下位要求、仕様の追加と YAML 保存を実装する。
6. 背景ノード、複数親、共有仕様、Undo / Redo は仕様決定後に段階的に実装する。
7. 環境固定が必要になった段階で Dockerfile を追加する。

## 初期対応が必要な点

- YAML スキーマを確定する。
- 背景 ID の開始値を確定する。
- ノード座標を YAML に保存するか決定する。
- 複数親と共有仕様を初期スコープに含めるか決定する。
- Undo / Redo の対象操作を決定する。
- ノード削除時の子ノード、仕様の扱いを決定する。

## 推奨ターゲット

| 対象 | Target Framework |
|---|---|
| Core | `net10.0` |
| Web | `net10.0` |
| Tests | `net10.0` |

## 参考情報

- Microsoft Learn: [.NET releases, patches, and support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)
- Microsoft Learn: [Install .NET on macOS](https://learn.microsoft.com/en-us/dotnet/core/install/macos)
- Microsoft Learn: [ASP.NET Core Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/)
- GitHub: [YamlDotNet](https://github.com/aaubry/YamlDotNet)
