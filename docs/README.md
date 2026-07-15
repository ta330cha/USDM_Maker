# USDM MindMap Editor ドキュメント

作成日: 2026-07-15

## 概要

USDM MindMap Editor は、USDM（Universal Specification Describing Manner）の要求仕様をマインドマップ形式で整理・編集する Web アプリケーションである。

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

## ドキュメント構成

| ファイル | 内容 |
|---|---|
| `requirements_specification.md` | 要求仕様の元資料 |
| `issues.md` | 未決定事項、確認事項 |
| `architecture.md` | アプリケーション構成、責務分担 |
| `UI.md` | 画面、操作、ノード表示仕様 |
| `DataModel.md` | YAML と内部データモデル |
| `development_environment_setup.md` | macOS 開発環境構築手順 |
| `development_environment_options.md` | 開発環境・技術選定メモ |

## 初期スコープ

初期実装では、以下を対象とする。

- キャンバス上でのノード表示
- 上位要求、下位要求、仕様の追加
- 要求、理由、仕様本文の編集
- ID 自動採番
- YAML への保存
- YAML からの読み込み
- ズーム、パン、ドラッグ移動

以下は未決定事項を整理したうえで段階的に実装する。

- 背景ノードの画面上の扱い
- 要求や仕様の複数親対応
- Undo / Redo
- 自動レイアウト
- ノード座標保存

## 開発方針

ASP.NET Core + Blazor Web App を前提に、C# 中心で実装する。マインドマップ描画は Blazor コンポーネントと SVG / HTML / CSS を組み合わせて実現する。

データ処理、ID 採番、YAML 入出力、検証処理は Core 層へ分離し、UI 層から独立してテストできる構成とする。
