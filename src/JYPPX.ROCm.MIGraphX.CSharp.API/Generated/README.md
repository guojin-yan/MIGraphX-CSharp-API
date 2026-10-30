# Generated sources

Sources in this directory are emitted from the [normalized API model](../../../compatibility/m3-normalized-api.json), with the [reviewed binding subset](../../../compatibility/m2-binding-subset.json) preserved as a semantic override. The generator first verifies the frozen `migraphx.h` SHA-256. Run `eng/generate-interop.ps1 -AcquireHeader` to update all outputs and add `-Verify` for the no-write byte-drift check. Do not maintain handwritten declaration files here.
