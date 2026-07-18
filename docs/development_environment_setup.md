# USDM_MindMap_Editor Mac(M2) 開発環境構築手順書

作成日: 2026-07-15

## 1. 目的

本書は、USDM_MindMap_Editor を macOS / Apple Silicon(M2) 環境で開発・実行するための環境構築手順を定義する。

USDM_MindMap_Editor は、USDM の要求仕様をマインドマップ形式で整理・編集し、YAML 形式で保存する ASP.NET Core + Blazor Web App として開発する。

## 2. 前提

- OS: macOS
- CPU: Apple Silicon / M2
- シェル: zsh
- 開発方式: ローカルの ASP.NET Core Web アプリケーションとして起動し、ブラウザで閲覧する
- 初期開発では Docker を必須にしない
- プロジェクトルートは `/Users/Taichiro/work/USDM_Maker/USDM_Maker` とする
- 初期実装の保存形式は YAML とする
- 開発初期は `dotnet run` で起動する
- 配布段階では self-contained publish を検討する

## 3. 役割分担

| 担当 | 役割 |
|---|---|
| Agent | コーディング、ドキュメント作成、ローカルで可能な範囲のビルド・テスト確認 |
| たなか | Agent 成果物の内容検証 |
| たなか | 実環境での動作確認 |

## 4. 推奨ツール構成

| 種別 | 推奨 | 用途 |
|---|---|---|
| .NET SDK | .NET SDK 10.x Arm64 | ASP.NET Core / Blazor Web App の開発・実行 |
| エディタ | Visual Studio Code | C# / Razor / Markdown 編集 |
| VS Code 拡張 | C# Dev Kit、C#、EditorConfig for VS Code | C# 開発支援 |
| Git | macOS Command Line Tools 付属 Git または Homebrew Git | バージョン管理 |
| ブラウザ | Safari / Chrome | UI 確認 |
| Docker Desktop | 任意 | 将来の環境固定・配布検証用 |

## 5. .NET SDK の選定

USDM_MindMap_Editor の Web アプリ本体は `.NET 10` を推奨する。

理由:

- .NET 10 は LTS。
- ASP.NET Core / Blazor Web App の開発対象として扱いやすい。
- C# と Blazor で UI とロジックを同一言語中心に実装できる。
- 個人開発のローカル Web アプリとして構成を軽く保てる。

プロジェクト作成後は `global.json` を配置し、使用 SDK 系列を固定する。

## 6. インストール手順

### 6.1 CPU アーキテクチャ確認

```zsh
uname -m
```

期待値:

```text
arm64
```

`x86_64` と表示される場合、Rosetta 経由のターミナルを使用している可能性がある。通常の Apple Silicon ネイティブターミナルで作業する。

### 6.2 Command Line Tools / Git 確認

Git が未導入の場合は、Command Line Tools をインストールする。

```zsh
xcode-select --install
```

インストール後、Git を確認する。

```zsh
git --version
```

既に Git が利用できる場合、この手順は不要。

### 6.3 .NET SDK 10 Arm64 のインストール

Microsoft 公式の .NET ダウンロードページから、macOS Arm64 版の .NET SDK 10.x をインストールする。

手順:

1. [Download .NET](https://dotnet.microsoft.com/download/dotnet) を開く。
2. `.NET 10` を選ぶ。
3. SDK の macOS `Arm64` インストーラーをダウンロードする。
4. `.pkg` を開いてインストールする。

インストール確認:

```zsh
which dotnet
dotnet --info
dotnet --list-sdks
dotnet --list-runtimes
```

Apple Silicon の通常インストールでは、`dotnet` は主に以下の配下を参照する。

```text
/usr/local/share/dotnet/
```

注意:

- Arm64 版と x64 版の SDK を混在させない。
- `dotnet --info` の `Architecture` が `arm64` であることを確認する。
- SDK は最新パッチを利用する。
- `global.json` の `rollForward` は `latestFeature` を推奨する。

### 6.4 Visual Studio Code のインストール

Visual Studio Code を利用する場合は、公式サイトから macOS 版をインストールする。

手順:

1. [Visual Studio Code on macOS](https://code.visualstudio.com/docs/setup/mac) を開く。
2. macOS 版をダウンロードする。
3. `.dmg` を開き、`Visual Studio Code.app` を `Applications` へ配置する。
4. VS Code を起動する。
5. コマンドパレットで `Shell Command: Install 'code' command in PATH` を実行する。

確認:

```zsh
code --version
```

推奨拡張:

- C# Dev Kit
- C#
- EditorConfig for VS Code
- Markdown All in One

## 7. リポジトリ確認

作業ルートへ移動する。

```zsh
cd /Users/Taichiro/work/USDM_Maker/USDM_Maker
```

現時点の主な構成:

```text
/Users/Taichiro/work/USDM_Maker/USDM_Maker/
  docs/
    requirements_specification.md
    issues.md
```

## 8. プロジェクト作成予定手順

実装開始時点で、以下のような構成を作成する想定。

```text
/Users/Taichiro/work/USDM_Maker/USDM_Maker/
  USDM_MindMap_Editor.sln
  global.json
  src/
    USDM_MindMap_Editor.Core/
    USDM_MindMap_Editor.Web/
  tests/
    USDM_MindMap_Editor.Core.Tests/
```

作成コマンド案:

```zsh
cd /Users/Taichiro/work/USDM_Maker/USDM_Maker
mkdir -p src tests

dotnet new globaljson --sdk-version 10.0.103 --roll-forward latestFeature
dotnet new sln -n USDM_MindMap_Editor
dotnet new classlib -n USDM_MindMap_Editor.Core -o src/USDM_MindMap_Editor.Core -f net10.0
dotnet new blazor -n USDM_MindMap_Editor.Web -o src/USDM_MindMap_Editor.Web -f net10.0 --interactivity Server --auth None
dotnet new mstest -n USDM_MindMap_Editor.Core.Tests -o tests/USDM_MindMap_Editor.Core.Tests -f net10.0

dotnet sln add src/USDM_MindMap_Editor.Core/USDM_MindMap_Editor.Core.csproj
dotnet sln add src/USDM_MindMap_Editor.Web/USDM_MindMap_Editor.Web.csproj
dotnet sln add tests/USDM_MindMap_Editor.Core.Tests/USDM_MindMap_Editor.Core.Tests.csproj

dotnet add src/USDM_MindMap_Editor.Web/USDM_MindMap_Editor.Web.csproj reference src/USDM_MindMap_Editor.Core/USDM_MindMap_Editor.Core.csproj
dotnet add tests/USDM_MindMap_Editor.Core.Tests/USDM_MindMap_Editor.Core.Tests.csproj reference src/USDM_MindMap_Editor.Core/USDM_MindMap_Editor.Core.csproj
```

作成される `global.json` の想定:

```json
{
  "sdk": {
    "version": "10.0.103",
    "rollForward": "latestFeature"
  }
}
```

## 9. YAML ライブラリ候補

YAML 読み書きには `YamlDotNet` を候補とする。

追加コマンド案:

```zsh
dotnet add src/USDM_MindMap_Editor.Core/USDM_MindMap_Editor.Core.csproj package YamlDotNet
```

採用前に、出力 YAML の読みやすさ、プロパティ順、不要項目の省略方法を確認する。

## 10. 起動・確認手順

プロジェクト作成後の基本確認コマンド。

```zsh
cd /Users/Taichiro/work/USDM_Maker/USDM_Maker
dotnet restore
dotnet build
dotnet test
dotnet run --project src/USDM_MindMap_Editor.Web
```

起動後、コンソールに表示される URL をブラウザで開く。

例:

```text
http://localhost:5000
https://localhost:5001
```

ポート競合時は、アプリ側の `launchSettings.json` または実行時引数でポートを変更する。

## 11. 開発用データ

初期開発では、`docs/requirements_specification.md` の YAML 例をもとにサンプルデータを作成する。

配置候補:

```text
/Users/Taichiro/work/USDM_Maker/USDM_Maker/sample/
  simple.usdm.yaml
```

サンプルデータは、以下の確認に利用する。

- YAML 読み込み
- ノード表示
- ID 採番
- YAML 保存
- 読み込み後保存時の差分確認
- 旧版 YAML の変換確認

Core 層の単体テストを初期段階から実行する。キャンバス操作が安定するまでは UI を手動確認し、主要操作が固まった段階で Playwright による UI 自動テストを追加する。

## 12. Docker Desktop について

初期開発では Docker Desktop を必須にしない。

用途:

- 実行環境固定
- 別環境での再現性確認
- 将来の配布検証

注意:

- 個人開発のローカル Web アプリ用途では、初期段階から Docker 化するメリットは小さい。
- Docker Desktop の利用条件はライセンス上の制約があるため、利用前に公式条件を確認する。

## 13. 配布方針

開発初期段階は `dotnet run` で起動する。

```zsh
dotnet run --project src/USDM_MindMap_Editor.Web
```

ローカル実装が安定してから、self-contained publish を検討する。

macOS Apple Silicon 向けの publish コマンド案:

```zsh
dotnet publish src/USDM_MindMap_Editor.Web/USDM_MindMap_Editor.Web.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true
```

publish 後に成果物ディレクトリを zip 化する。zip に同梱する内容は以下を基本とする。

- self-contained publish 成果物
- 起動スクリプト
- README
- サンプル YAML

## 14. トラブルシューティング

### `dotnet` が見つからない

確認:

```zsh
which dotnet
echo $PATH
```

公式インストーラーで導入した場合は通常自動設定される。手動インストールや install script を使った場合は、`PATH` と `DOTNET_ROOT` の追加が必要になる場合がある。

### `dotnet --info` が x64 になる

Rosetta 経由のターミナル、または x64 SDK を参照している可能性がある。

確認:

```zsh
uname -m
dotnet --info
```

Apple Silicon 開発では `arm64` を前提にする。

### HTTPS 証明書警告が出る

開発用 HTTPS 証明書を信頼する。

```zsh
dotnet dev-certs https --trust
```

ローカル確認のみであれば、HTTP 起動でもよい。

### YAML の日本語が文字化けする

保存時の文字コードを UTF-8 にする。VS Code の右下に表示されるエンコーディングが `UTF-8` であることを確認する。

## 15. 参考リンク

- [.NET releases, patches, and support](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)
- [.NET and .NET Core Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [Install .NET on macOS](https://learn.microsoft.com/en-us/dotnet/core/install/macos)
- [Build a Blazor app](https://learn.microsoft.com/en-us/aspnet/core/blazor/tutorials/build-a-blazor-app)
- [Visual Studio Code on macOS](https://code.visualstudio.com/docs/setup/mac)
- [YamlDotNet](https://github.com/aaubry/YamlDotNet)

## 16. 未確定事項

- self-contained publish 時の起動スクリプト内容
- 配布 README の内容
- サンプル YAML の配置場所
- YAML ライブラリの正式採用
- Playwright による UI 自動テストの具体的な対象操作
