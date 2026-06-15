# AriadneTS

[English](../../README.md) · [简体中文](README.zh-CN.md) ·
[繁體中文](README.zh-TW.md) · [日本語](README.ja.md) ·
[한국어](README.ko.md) · [Русский](README.ru.md) · **Español**

AriadneTS es un runtime QuickJS pequeño y multiplataforma para ejecutar lógica
de negocio TypeScript compartida entre motores. Actualmente admite Unity
`6000.0.77f1`; el soporte de Unreal está planificado.

## Características

- TypeScript se compila a módulos ES estándar ejecutados por QuickJS.
- ABI C nativa independiente de Unity.
- Plugins nativos para Windows, macOS, Android e iOS.
- Paquete `.bytes` único, firmado y adecuado para Addressables.
- Verificación de firma, ABI, rutas, tamaños y SHA-256 antes del cambio.
- Promesas, ciclo de vida, transferencia de estado, límites y tiempo máximo.

## Responsabilidades

```text
Unity Addressables: descarga, caché, selección de versión y política de rollback
AriadneTS: validación, carga de módulos, ejecución QuickJS y cambio atómico
```

## Inicio rápido

Instala mediante **Install package from disk**:

```text
UnityPackages/com.ariadnets.runtime/package.json
```

Genera una clave de desarrollo y un paquete firmado:

```sh
./Tools/generate_signing_key.sh ~/.ariadnets/dev-private-key.pem
./Tools/package_script_update.sh 0.1.0 1 ~/.ariadnets/dev-private-key.pem
```

Añade `typescript-package.bytes` a Addressables y, después de cargarlo, llama:

```csharp
controller.StartPackage(asset);
controller.SwitchPackage(nextAsset);
```

No confirmes la clave privada en Git. Configura en Unity únicamente la clave
pública generada.

