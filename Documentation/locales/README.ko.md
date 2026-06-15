# AriadneTS

[English](../../README.md) · [简体中文](README.zh-CN.md) ·
[繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · **한국어** ·
[Русский](README.ru.md) · [Español](README.es.md)

AriadneTS는 게임 엔진에서 공통 TypeScript 비즈니스 로직을 실행하기 위한
작은 크로스 엔진 QuickJS 런타임입니다. 현재 Unity `6000.0.77f1`을 지원하며
Unreal 지원은 계획 중입니다.

## 주요 기능

- TypeScript를 표준 ES Module로 컴파일하고 QuickJS에서 실행합니다.
- Unity에 종속되지 않는 네이티브 C ABI를 제공합니다.
- Windows, macOS, Android, iOS 네이티브 Plugin을 포함합니다.
- Addressables에 적합한 서명된 단일 `.bytes` 패키지를 사용합니다.
- 런타임 전환 전에 서명, ABI, 경로, 크기, SHA-256을 검증합니다.
- Promise, 라이프사이클, 상태 전달, 메모리/스택 제한, 실행 제한 시간을 지원합니다.

## 책임 구분

```text
Unity Addressables: 다운로드, 캐시, 버전 선택, 롤백 정책
AriadneTS: 패키지 검증, 모듈 로딩, QuickJS 실행, 원자적 런타임 전환
```

## 빠른 시작

Unity Package Manager의 **Install package from disk**에서 다음 파일을 선택합니다.

```text
UnityPackages/com.ariadnets.runtime/package.json
```

개발 키와 서명 패키지를 생성합니다.

```sh
./Tools/generate_signing_key.sh ~/.ariadnets/dev-private-key.pem
./Tools/package_script_update.sh 0.1.0 1 ~/.ariadnets/dev-private-key.pem
```

`typescript-package.bytes`를 Addressables에 추가하고 로드 후 호출합니다.

```csharp
controller.StartPackage(asset);
controller.SwitchPackage(nextAsset);
```

개인 키는 Git에 커밋하지 말고 Unity에는 출력된 공개 키만 설정하세요.

