# 接続線の実 UI 確認ホスト

本番の `Home` コンポーネント、CSS、JavaScript、Interactive Server、Core の接続線計算を再利用する確認専用ホストです。確認用の文書は Core テストと共有します。本番にシナリオ選択機能は追加しません。

プロジェクトルート `USDM_MindMap_Editor` から起動します。

```bash
dotnet run --project tests/USDM_MindMap_Editor.VisualTests/USDM_MindMap_Editor.VisualTests.csproj --urls http://127.0.0.1:5189
```

`http://127.0.0.1:5189/scenarios/single` をブラウザで開きます。末尾を次の名前に変更して各ケースを確認します。

- `single`: 単一の子
- `multiple`: 複数の子
- `upward`: 親より上側の子
- `four-level`: 4 階層
- `multiple-backgrounds`: 複数背景
- `long-text`: 長文と折りたたまれた理由
- `long-expanded`: 理由を展開した状態

各画面で理由の展開・折りたたみ、ズーム、パン、ズームリセットを操作します。接続線の始点・終点とノードの実矩形、曲線の張り出し、ノード内部の横切り、SVG の描画範囲を確認します。

このホストはソリューションへ追加していません。通常のアプリ起動と単体テストには影響しません。

## ブラウザ回帰確認

Playwright（Chromiumをインストール済み）が必要です。ホスト起動中に別のターミナルで、プロジェクトルートから実行します。通常のNodeモジュール解決でPlaywrightを読み込める場合、環境変数は省略できます。

```bash
PLAYWRIGHT_MODULE=/path/to/node_modules/playwright node tests/js/connectors.browser.cjs
```

7ケースの実DOM境界、曲線の全ノード非交差、SVG範囲、25%・200%ズーム、パン、リセット、長い理由の開閉3往復を検証します。`artifacts/connection-lines/` にスクリーンショットと `results.json` を出力します。この生成物は既存の `.gitignore` により除外されます。

関連する自動テスト:

```bash
dotnet test USDM_MindMap_Editor.sln
node --test tests/js/mindmapCanvas.test.mjs
```
