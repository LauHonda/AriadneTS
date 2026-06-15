# AriadneTS

[English](../../README.md) · **简体中文** ·
[繁體中文](README.zh-TW.md) · [日本語](README.ja.md) ·
[한국어](README.ko.md) · [Русский](README.ru.md) · [Español](README.es.md)

AriadneTS 是一个小型、跨引擎的 QuickJS 运行框架，用于在游戏引擎中执行
共享的 TypeScript 业务逻辑。当前支持 Unity `6000.0.77f1`，Unreal 支持尚在规划中。

## 核心能力

- TypeScript 编译为标准 ES Module，并由 QuickJS 执行。
- 原生 C ABI 不依赖 Unity，方便未来接入其他引擎。
- 支持 Windows、macOS、Android 和 iOS 原生插件。
- 使用适合 Addressables 的单文件签名脚本包。
- 在切换运行时之前完成签名、ABI、路径、大小和哈希验证。
- 支持 Promise、生命周期、状态热切换、内存/栈限制和执行超时。

## 职责边界

```text
Unity Addressables：下载、缓存、版本选择、回滚策略
AriadneTS：验签、模块加载、QuickJS 执行、运行时原子切换
```

## 快速开始

在 Unity Package Manager 中选择 **Install package from disk**，安装：

```text
UnityPackages/com.ariadnets.runtime/package.json
```

生成开发密钥与签名脚本包：

```sh
./Tools/generate_signing_key.sh ~/.ariadnets/dev-private-key.pem
./Tools/package_script_update.sh 0.1.0 1 ~/.ariadnets/dev-private-key.pem
```

输出文件：

```text
Build/script-packages/0.1.0/typescript-package.bytes
```

将该文件加入 Addressables，并在持久 GameObject 上添加
`ScriptRuntimeHost` 与 `ScriptPackageRuntimeController`。加载后调用：

```csharp
TextAsset asset = await Addressables
    .LoadAssetAsync<TextAsset>("ariadnets-package")
    .Task;

controller.StartPackage(asset);
controller.SwitchPackage(nextAsset);
```

私钥不能提交到 Git。Unity 中只配置打包工具输出的
`RSA1.<modulus>.<exponent>` 公钥。

运行完整测试：

```sh
./Tools/test_all.sh
./Tools/build_unity_plugins.sh
```

详细内容请阅读[部署说明](../unity-deployment.md)与
[运行时架构](../runtime-architecture.md)。

