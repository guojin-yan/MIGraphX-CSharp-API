# Generated sources

M1 sources in this directory are generated from [`compatibility/m1-binding-subset.json`](../../../compatibility/m1-binding-subset.json) after verifying the frozen `migraphx.h` SHA-256. Run `eng/generate-interop.ps1 -AcquireHeader` to update them and add `-Verify` for the drift gate. Do not maintain handwritten declaration files here.
