# Native library loading

The loader selects an explicit absolute path or audits application-RID, application-base and system candidates. It preserves platform failures and prevents incompatible native roots from being mixed in one process.

It does not download files or change `PATH`/`LD_LIBRARY_PATH`. See [deployment and troubleshooting](../../../docs/guides/runtime-deployment.md).
