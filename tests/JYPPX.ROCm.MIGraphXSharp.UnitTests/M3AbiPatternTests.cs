using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class M3AbiPatternTests
{
    [Fact]
    public void FakeNativeExecutesCallbackLifetimeBoolUtf8BorrowedAndOutHandlePatterns()
    {
        var library = NativeLibrary.Load(GetFakeNativePath());
        try
        {
            var reset = Get<ResetDelegate>(library, "fake_reset");
            var store = Get<StoreCallbackDelegate>(library, "fake_m3_store_callback");
            var invoke = Get<InvokeCallbackDelegate>(library, "fake_m3_invoke_stored");
            var clear = Get<ResetDelegate>(library, "fake_m3_clear_callback");
            reset();

            const string expectedText = "M3-\u4e2d\u6587-\u03a9";
            var utf8 = Encoding.UTF8.GetBytes(expectedText);
            var state = new IntPtr(0x1357);
            var borrowed = new IntPtr(0x2468);
            var returnedHandle = new IntPtr(0x3579);
            var calls = 0;
            CallbackDelegate callback = (IntPtr actualState, IntPtr text, nuint textSize, byte flag, IntPtr actualBorrowed, out IntPtr outHandle) =>
            {
                Assert.Equal(state, actualState);
                Assert.Equal((nuint)utf8.Length, textSize);
                Assert.Equal(expectedText, Marshal.PtrToStringUTF8(text, checked((int)textSize)));
                Assert.Equal((byte)1, flag);
                Assert.Equal(borrowed, actualBorrowed);
                outHandle = returnedHandle;
                calls++;
                return 0;
            };

            Assert.Equal(0, store(callback, state));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var textPin = GCHandle.Alloc(utf8, GCHandleType.Pinned);
            try
            {
                Assert.Equal(0, invoke(textPin.AddrOfPinnedObject(), (nuint)utf8.Length, 1, borrowed, out var actualHandle));
                Assert.Equal(returnedHandle, actualHandle);
                Assert.Equal(1, calls);
            }
            finally
            {
                textPin.Free();
            }

            clear();
            GC.KeepAlive(callback);
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [Fact]
    public void FakeNativeContainsCallbackExceptionsAndCleansFailedOutHandle()
    {
        var library = NativeLibrary.Load(GetFakeNativePath());
        try
        {
            var store = Get<StoreCallbackDelegate>(library, "fake_m3_store_callback");
            var invoke = Get<InvokeCallbackDelegate>(library, "fake_m3_invoke_stored");
            var clear = Get<ResetDelegate>(library, "fake_m3_clear_callback");
            Exception? observed = null;
            CallbackDelegate callback = (IntPtr state, IntPtr text, nuint textSize, byte flag, IntPtr borrowed, out IntPtr outHandle) =>
            {
                outHandle = new IntPtr(0x7777);
                try
                {
                    throw new InvalidOperationException("callback boundary canary");
                }
                catch (Exception error)
                {
                    observed = error;
                    return 4;
                }
            };

            Assert.Equal(0, store(callback, IntPtr.Zero));
            var utf8 = new byte[] { (byte)'x' };
            var textPin = GCHandle.Alloc(utf8, GCHandleType.Pinned);
            try
            {
                Assert.Equal(4, invoke(textPin.AddrOfPinnedObject(), 1, 0, new IntPtr(1), out var actualHandle));
                Assert.Equal(IntPtr.Zero, actualHandle);
                Assert.IsType<InvalidOperationException>(observed);
            }
            finally
            {
                textPin.Free();
                clear();
                GC.KeepAlive(callback);
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [Fact]
    public void FakeNativeExecutesSizeTArrayLengthPattern()
    {
        var library = NativeLibrary.Load(GetFakeNativePath());
        try
        {
            var sum = Get<SumSizeTDelegate>(library, "fake_m3_sum_size_t");
            var values = new nuint[] { 1, 2, 4, 8, 16 };
            var pin = GCHandle.Alloc(values, GCHandleType.Pinned);
            try
            {
                Assert.Equal(0, sum(pin.AddrOfPinnedObject(), (nuint)values.Length, out var result));
                Assert.Equal((nuint)31, result);
                Assert.Equal(1, sum(IntPtr.Zero, 0, out _));
            }
            finally
            {
                pin.Free();
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static string GetFakeNativePath()
    {
        var name = OperatingSystem.IsWindows() ? "migraphx_c.dll" : "libmigraphx_c.so";
        return Path.Combine(FindRepositoryRoot(), "artifacts", "fake-native", "Release", name);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MIGraphXSharp.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the MIGraphXSharp repository root.");
    }

    private static T Get<T>(IntPtr library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ResetDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StoreCallbackDelegate(CallbackDelegate callback, IntPtr state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InvokeCallbackDelegate(IntPtr text, nuint textSize, byte flag, IntPtr borrowed, out IntPtr outHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CallbackDelegate(IntPtr state, IntPtr text, nuint textSize, byte flag, IntPtr borrowed, out IntPtr outHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SumSizeTDelegate(IntPtr values, nuint count, out nuint result);
}
