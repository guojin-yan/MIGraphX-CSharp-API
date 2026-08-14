# M1 Direct P/Invoke design

## Frozen subset

The single semantic source is `compatibility/m1-binding-subset.json`. Before generation, the script downloads or accepts the frozen `migraphx.h` and requires SHA-256 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2` and 36,635 bytes.

The subset contains exactly:

- `migraphx_target_create`, `migraphx_target_assign_to`, `migraphx_target_destroy`;
- `migraphx_program_create`, `migraphx_program_assign_to`, `migraphx_program_destroy`;
- `migraphx_status` values 0, 1, 3, and 4;
- target and program single-pointer opaque handles.

The frozen header has no public last-error function. Managed exceptions therefore preserve the integer status, known mapping, exact entry-point context, and loader/export platform errors without inventing native error text.

## Generated declarations

The generator produces one common enum/model file plus two declaration files. .NET 7 and later compile `LibraryImport` with cdecl. Earlier TFMs compile `DllImport` with cdecl. There are no separate handwritten semantic lists. Verify mode fails when output is missing or changed.

## Loader and ownership

Explicit paths must be absolute and existing files. System probing records application RID-native, application-base, and system-loader candidates in that order. Linux includes `libmigraphx_c.so.3` and `migraphx_c`; Windows/macOS are diagnostic candidates only and are not official support claims.

Modern targets use `NativeLibrary` and a resolver. .NET Framework uses Win32 loading/export APIs on Windows; non-Windows legacy loading is an explicit documented limitation. The loader never changes `PATH`, `LD_LIBRARY_PATH`, TLS configuration, or downloads.

Create results immediately enter a SafeHandle. If native creation reports failure after returning a non-null handle, the handle is destroyed. A success status with a null handle becomes a managed failure without a destroy call. `Dispose` is idempotent. Assign calls operate on two pre-existing handles; no public clone API is inferred from the function name.

Target names are encoded with strict UTF-8 into a call-duration unmanaged buffer. Null, empty, embedded NUL, and invalid UTF-16 are rejected before native execution.

## Evidence

The official ELF static export gate, generated declarations, and header subset all match six names. The local fake validates enum size, pointer size, UTF-8 bytes, status injection, null/failing construction, destroy counts, assign copy behavior, and concurrent create/destroy balance across representative old and modern TFMs.

Fake execution remains `fake-native-executed`, not official MIGraphX evidence. An independent official Linux session at `f1a11cfd1701a041cee29188f7600c85b34ae260` loaded the fixed library, verified exports, passed the strict UTF-8 runtime-rejection probe, and executed valid `gpu` target/program create, assign, and destroy. ONNX compile/run belongs to the separately bounded M2 result; async and device buffers remain later work.

中文摘要：M1 由单一 manifest 生成两条声明路径，严格区分固定头/官方 ELF 静态证据、本地 fake 执行与精确 SHA 的官方 runtime 执行。SafeHandle 覆盖失败清理与幂等释放，但不提前公开 Program/Target 高层 clone API。
