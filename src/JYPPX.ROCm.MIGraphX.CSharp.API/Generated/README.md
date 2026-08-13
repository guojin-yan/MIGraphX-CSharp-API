# Generated sources

M2 sources in this directory are generated from [`compatibility/m2-binding-subset.json`](../../../compatibility/m2-binding-subset.json) after verifying the frozen `migraphx.h` SHA-256. Run `eng/generate-interop.ps1 -AcquireHeader` to update them and add `-Verify` for the drift gate. Do not maintain handwritten declaration files here.
