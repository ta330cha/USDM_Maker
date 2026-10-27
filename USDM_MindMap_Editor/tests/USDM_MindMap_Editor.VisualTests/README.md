# 下方インデント配置と接続線の実 UI 確認ホスト

本番の `Home` コンポーネント、CSS、JavaScript、Interactive Server、Core の接続線計算を再利用する確認専用ホストです。確認用の文書は Core テストと共有します。本番にシナリオ選択機能は追加しません。

プロジェクトルート `USDM_MindMap_Editor` から起動します。

```bash
dotnet run --project tests/USDM_MindMap_Editor.VisualTests/USDM_MindMap_Editor.VisualTests.csproj --urls http://127.0.0.1:5189
```

`http://127.0.0.1:5189/scenarios/single` をブラウザで開きます。末尾を次の名前に変更して各ケースを確認します。

- `single`: 単一の子
- `multiple`: 複数の子
- `deep-first-subtree`: 最初の子に深い子孫があり、そのサブツリー全体の下へ次の兄弟を配置
- `four-level`: 4 階層
- `multiple-backgrounds`: 複数背景
- `long-text`: 長文と折りたたまれた理由
- `long-expanded`: 理由を展開した状態

各画面で理由の展開・折りたたみ、ズーム、パン、ズームリセットを操作します。子の左辺が親の左辺から親幅の3/4だけ右へ寄り、最初の子が親の下辺から96px以上下へ配置されることを確認します。後続の兄弟と背景は、直前のサブツリーの全ノード・曲線を含む最下端から32px以上下へ配置します。接続線の始点・終点とノードの実矩形、曲線の張り出し、ノード内部の横切り、SVG の描画範囲も確認します。

このホストはソリューションへ追加していません。通常のアプリ起動と単体テストには影響しません。

## ブラウザ回帰確認

Playwright（Chromiumをインストール済み）が必要です。ホスト起動中に別のターミナルで、プロジェクトルートから実行します。通常のNodeモジュール解決でPlaywrightを読み込める場合、環境変数は省略できます。

```bash
PLAYWRIGHT_MODULE=/path/to/node_modules/playwright node tests/js/connectors.browser.cjs
```

7ケースの実DOM境界から親幅3/4のインデント、親子の最小上下間隔96px、ID順の兄弟サブツリー間隔32px、複数背景の左端揃えと縦積みを検証します。サブツリーの最下端には親から入る曲線と内部の全曲線を含めます。ノード同士の非重複、曲線の全ノード非交差、下向き・右向きの制御点、SVG範囲、25%・200%ズーム、パン、リセット、長い理由の開閉3往復も検証します。

`artifacts/indented-layout/` にスクリーンショットと `results.json` を出力します。旧横方向配置の確認結果 `artifacts/connection-lines/` とは別に保存します。これらの生成物は既存の `.gitignore` により除外されます。

## 下辺の子追加領域・入力ダイアログの回帰確認

同じホストを起動した状態で、次を実行します。

```bash
PLAYWRIGHT_MODULE=/path/to/node_modules/playwright node tests/js/childInsertion.browser.cjs
```

背景から上位要求、上位要求から下位要求、下位要求から仕様を実際に追加し、採番・親接続・再配置・実DOM境界への接続を検証します。要求の理由は空欄で保存できること、仕様の関連下位要求を選択できること、所属先を関連先に選べないこと、ツールバーから背景を追加できることも確認します。

必須入力の空白エラー、キャンセル、連打による重複防止、Enter / Space、右クリック・楕円外の本文・理由・接続線からの誤追加防止、追加領域からのドラッグ抑止を検証します。ポインターキャンセルだけはPlaywrightに実デバイスのキャンセル操作がないため、実際の押下／解放の間に合成`pointercancel`イベントを使用します。

ダイアログ内ではTab / Shift+Tabを複数周操作し、フォーカスが外へ抜けないことを確認します。キャンセル・保存・Escapeの各終了方法で、起点の親ノード下辺へフォーカスが戻ること、背景追加の保存後はツールバーへ戻ることも検証します。

25%・100%・200%で楕円の縦が画面上10 CSS px、横がノード幅の1/2となることを確認します。中心、上下左右と斜めの境界は反応し、境界直外と外接長方形の四隅では反応しないことを検証します。パン後と長い理由の展開・折りたたみ後にも位置・寸法・接続線を再確認します。

25%表示で理由のsummaryと重なる楕円上側をクリックし、追加だけが起動することを確認します。MutationObserverで`details.open`の属性変化を監視し、理由が一時的に開閉してから戻される動作も不合格にします。追加のストレス試験ではオーバーレイの`pointer-events`だけを一時的に無効にし、実マウス入力がsummaryへ届く条件でも優先制御が働くことを確認します。同じ場所からのドラッグ・キャンセルで理由やパンへ切り替わらず、楕円外の四隅での理由クリックと、理由へフォーカスしたEnter / Spaceは有効であることを確認します。

`artifacts/elliptical-child-insertion/` に画面画像と `results.json` を保存し、旧4px帯の結果 `artifacts/child-insertion/` と配置試験結果は別に保持します。

関連する自動テスト:

```bash
dotnet test USDM_MindMap_Editor.sln
node --test tests/js/mindmapCanvas.test.mjs
```
