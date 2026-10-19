# tAKI

テーマ「あき」から生まれた、滝を上る3Dアクションゲームです。
A・K・Iのアイテムを集め、AKIブーストで頂上のsAKIを目指します。

- Unity **2022.3.62f3**／URP 14
- 対象：Windows 64bit
- 操作：WASDで開始・移動。クリア後は一度キーを離して再び押すと再挑戦

## プロジェクトを開く

1. このリポジトリをクローンします。
2. [音源の復元手順](tAKI/AUDIO_SETUP.md) に従ってBGM・効果音を配置します。配布元の音源ファイルは、このリポジトリに含めていません。
3. Unity Hubから **`tAKI/` フォルダー**をUnityプロジェクトとして開きます。
4. `Assets/_Project/Scenes/Main.unity` を開き、Playを押します。

すでにUnityで開いた後に音源を配置した場合は、`tAKI > Restore audio references` を実行してください。

## ビルド

Playを停止してシーンを保存し、Unityの `tAKI > Build Windows` を実行します。
出力先は `tAKI/Builds/Windows/tAKI.exe` です。クレジットは配布物に同梱してください。

## ドキュメント

- [開発・調整・ビルドの説明](tAKI/README.md)
- [企画書](tAKI/GAME_DESIGN.md)
- [クレジット・第三者素材の利用条件](tAKI/CREDITS.md)
- [音源の復元](tAKI/AUDIO_SETUP.md)
- [開発ガイド](AGENTS.md)

`Library`・`Temp`・ビルド成果物・ダウンロードした素材原本はGit管理の対象外です。
ゲームが参照するモデル・フォント・各種設定は、Unityプロジェクト内の `Assets/` に含まれています。
