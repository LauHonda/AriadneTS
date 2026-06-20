# AriadneTS SDK

This folder is managed by AriadneTS and is intended to be treated as read-only
application infrastructure. Business scripts should import the public API from
this folder instead of editing SDK internals directly.

The SDK currently exposes a minimal logging API. The implementation still uses
the existing runtime `host.log` bridge; engine-specific warning/error routing
will be added in the bridge layer later.
