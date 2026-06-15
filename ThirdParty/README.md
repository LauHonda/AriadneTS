# Third-party dependencies

## QuickJS

The native runtime currently pins the official QuickJS `2026-06-04` source
release at:

```text
ThirdParty/quickjs/
```

Required source files:

- `quickjs.c`
- `quickjs.h`
- `dtoa.c`
- `libregexp.c`
- `libregexp.h`
- `libunicode.c`
- `libunicode.h`
- `cutils.c`
- `cutils.h`
- `list.h`

QuickJS is maintained as vendored source so engine builds do not depend on a
machine-level package manager and can pin the exact runtime implementation.
