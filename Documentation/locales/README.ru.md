# AriadneTS

[English](../../README.md) · [简体中文](README.zh-CN.md) ·
[繁體中文](README.zh-TW.md) · [日本語](README.ja.md) ·
[한국어](README.ko.md) · **Русский** · [Español](README.es.md)

AriadneTS — компактная междвижковая среда выполнения QuickJS для общей
бизнес-логики на TypeScript. Сейчас поддерживается Unity `6000.0.77f1`;
поддержка Unreal запланирована.

## Возможности

- Компиляция TypeScript в стандартные ES-модули и выполнение в QuickJS.
- Независимый от Unity нативный C ABI.
- Нативные плагины для Windows, macOS, Android и iOS.
- Единый подписанный `.bytes`-пакет, удобный для Addressables.
- Проверка подписи, ABI, путей, размеров и SHA-256 до переключения.
- Promise, жизненный цикл, перенос состояния, ограничения памяти/стека и тайм-аут.

## Граница ответственности

```text
Unity Addressables: загрузка, кэш, выбор версии и политика отката
AriadneTS: проверка пакета, загрузка модулей, QuickJS и атомарное переключение
```

## Быстрый старт

Установите через **Install package from disk**:

```text
UnityPackages/com.ariadnets.runtime/package.json
```

Создайте ключ и подписанный пакет:

```sh
./Tools/generate_signing_key.sh ~/.ariadnets/dev-private-key.pem
./Tools/package_script_update.sh 0.1.0 1 ~/.ariadnets/dev-private-key.pem
```

Добавьте `typescript-package.bytes` в Addressables и после загрузки вызовите:

```csharp
controller.StartPackage(asset);
controller.SwitchPackage(nextAsset);
```

Не добавляйте закрытый ключ в Git. В Unity настраивается только открытый ключ.

