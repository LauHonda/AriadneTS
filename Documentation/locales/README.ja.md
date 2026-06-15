# AriadneTS

[English](../../README.md) · [简体中文](README.zh-CN.md) ·
[繁體中文](README.zh-TW.md) · **日本語** · [한국어](README.ko.md) ·
[Русский](README.ru.md) · [Español](README.es.md)

AriadneTS は、ゲームエンジン上で共通の TypeScript ビジネスロジックを実行するための、
小さなクロスエンジン QuickJS ランタイムです。現在は Unity `6000.0.77f1`
をサポートし、Unreal 対応は計画中です。

## 主な機能

- TypeScript を標準 ES Module にコンパイルして QuickJS で実行。
- Unity に依存しないネイティブ C ABI。
- Windows、macOS、Android、iOS 用ネイティブ Plugin。
- Addressables に適した署名付き単一 `.bytes` パッケージ。
- 切り替え前に署名、ABI、パス、サイズ、SHA-256 を検証。
- Promise、ライフサイクル、状態移行、メモリ/スタック制限、実行タイムアウト。

## 責務

```text
Unity Addressables：ダウンロード、キャッシュ、バージョン選択、ロールバック方針
AriadneTS：検証、モジュールロード、QuickJS 実行、ランタイム切り替え
```

## クイックスタート

Unity Package Manager の **Install package from disk** で次を選択します。

```text
UnityPackages/com.ariadnets.runtime/package.json
```

開発鍵と署名済みパッケージを生成します。

```sh
./Tools/generate_signing_key.sh ~/.ariadnets/dev-private-key.pem
./Tools/package_script_update.sh 0.1.0 1 ~/.ariadnets/dev-private-key.pem
```

`Build/script-packages/0.1.0/typescript-package.bytes` を Addressables に追加し、
ロード後に次を呼び出します。

```csharp
controller.StartPackage(asset);
controller.SwitchPackage(nextAsset);
```

秘密鍵は Git にコミットしないでください。Unity には出力された
`RSA1.<modulus>.<exponent>` 公開鍵のみ設定します。

