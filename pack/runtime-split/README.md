# Runtime topology (deferred)

M7 selects a layered topology in principle: `JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.linux-x64` version `7.2.1` would depend on exact `JYPPX.ROCm.HIP.CSharp.API.Runtime.linux-x64` version `[7.2.1]` and carry only MIGraphX/provider increments. The two packages may not contain the same target path or mix ROCm families.

Packing remains disabled. hipBLASLt alone exceeds the 262,144,000-byte gate, five provider inventories/licenses remain open, upstream MIGraphX RPATH layout has no package-only proof against HipSharp's runtime asset layout, and the adapter cannot yet compare a cross-assembly loaded-runtime fingerprint. No component split or RPATH rewrite is approved without a new topology review and authorized package-only evidence.
