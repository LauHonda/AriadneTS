# AriadneTS

[English](../../README.md) · [简体中文](README.zh-CN.md) · **繁體中文** ·
[日本語](README.ja.md) · [한국어](README.ko.md) ·
[Русский](README.ru.md) · [Español](README.es.md)

AriadneTS 是一個小型、跨引擎的 QuickJS 執行框架，用於在遊戲引擎中執行
共用的 TypeScript 業務邏輯。目前支援 Unity `6000.0.77f1`，Unreal 支援仍在規劃中。

## 主要能力

- TypeScript 編譯為標準 ES Module，並由 QuickJS 執行。
- 原生 C ABI 不依賴 Unity，可供未來其他引擎使用。
- 提供 Windows、macOS、Android 與 iOS 原生 Plugin。
- 使用適合 Addressables 的單一簽名 `.bytes` 套件。
- 切換執行環境前先驗證簽名、ABI、路徑、大小與雜湊。
- 支援 Promise、生命週期、狀態切換、記憶體/堆疊限制與執行逾時。

## 責任邊界

```text
Unity Addressables：下載、快取、版本選擇、回復策略
AriadneTS：驗簽、模組載入、QuickJS 執行、原子切換
```

## 快速開始

在 Unity Package Manager 使用 **Install package from disk** 安裝：

```text
UnityPackages/com.ariadnets.runtime/package.json
```

建立開發金鑰與簽名套件：

```sh
./Tools/generate_signing_key.sh ~/.ariadnets/dev-private-key.pem
./Tools/package_script_update.sh 0.1.0 1 ~/.ariadnets/dev-private-key.pem
```

將 `Build/script-packages/0.1.0/typescript-package.bytes` 加入 Addressables。
場景中加入 `ScriptRuntimeHost` 與 `ScriptPackageRuntimeController`，載入後呼叫：

```csharp
controller.StartPackage(asset);
controller.SwitchPackage(nextAsset);
```

私鑰不可提交到 Git；Unity 僅設定工具輸出的
`RSA1.<modulus>.<exponent>` 公鑰。

完整測試：

```sh
./Tools/test_all.sh
./Tools/build_unity_plugins.sh
```

