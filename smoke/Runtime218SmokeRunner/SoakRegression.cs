using System.Diagnostics;
using System.Text.Json;
using JYPPX.ROCm.HipSharp;
using JYPPX.ROCm.HipSharp.Memory;
using JYPPX.ROCm.HipSharp.Types;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;
using JYPPX.ROCm.MIGraphXSharp;

internal static class SoakRegression
{
    internal static void Run(string native, string hipPath, string output, Action<string, Action> check,
        Action<MIGraphXProgram, bool> compile)
    {
        check("gpu-soak-convolution-literal-relu-numerical", () =>
        {
            using var program = new MIGraphXProgram(native); using var main = program.GetMainModule();
            var inputShape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 1, 16, 16 });
            var weightsShape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 2, 1, 3, 3 });
            var weights = Enumerable.Range(0, 18).Select(i => (i % 5 - 2) * .25f).ToArray();
            using var inputNode = main.AddParameter("input", inputShape);
            MIGraphXInstruction literal;
            using (var argument = MIGraphXArgument.Create(native, weightsShape, weights)) literal = main.AddLiteral(argument);
            using (literal)
            using (var convolution = MIGraphXOperation.Create(native, "convolution"))
            using (var args = new MIGraphXInstructions(native, new[] { inputNode, literal }))
            using (var conv = main.AddInstruction(convolution, args))
            using (var relu = MIGraphXOperation.Create(native, "relu"))
            using (var reluArgs = new MIGraphXInstructions(native, new[] { conv }))
            using (var result = main.AddInstruction(relu, reluArgs))
            using (var returns = new MIGraphXInstructions(native, new[] { result }))
            using (var returned = main.AddReturn(returns)) { }
            compile(program, true);
            for (var iteration = 0; iteration < 8; iteration++)
            {
                var values = Enumerable.Range(0, 256).Select(i => (i % 13 - 6 + iteration) * .125f).ToArray();
                var expected = new float[2 * 14 * 14];
                for (var channel = 0; channel < 2; channel++) for (var y = 0; y < 14; y++) for (var x = 0; x < 14; x++)
                {
                    var value = 0f;
                    for (var ky = 0; ky < 3; ky++) for (var kx = 0; kx < 3; kx++)
                        value += values[(y + ky) * 16 + x + kx] * weights[channel * 9 + ky * 3 + kx];
                    expected[(channel * 14 + y) * 14 + x] = Math.Max(0, value);
                }
                using var input = MIGraphXArgument.Create(native, inputShape, values);
                using var parameters = new MIGraphXParameterMap(native); parameters.Add("input", input);
                using var outputs = program.Run(parameters); RuntimeRegression.Equal(expected, outputs[0].ToArray<float>());
            }
        });
        check("gpu-soak-four-workers-1000-device-runs", () =>
        {
            var timer = Stopwatch.StartNew();
            var completed = new int[4];
            var tasks = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
            {
                const int elements = 32768;
                using var hip = new HipRuntime(hipPath); using var stream = hip.CreateStream(HipStreamFlags.NonBlocking);
                using var program = CreateRelu(native, elements); compile(program, false);
                using var parameters = new DeviceParameters(program, hip);
                var input = parameters.Input;
                using var destination = hip.Allocate(elements * sizeof(float));
                var values = new float[elements]; var bytes = new byte[elements * sizeof(float)]; var actual = new float[elements];
                for (var iteration = 0; iteration < 250; iteration++)
                {
                    for (var i = 0; i < elements; i++) values[i] = (i % 97 - 48 + iteration % 11 + worker * 3) * .125f;
                    Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); input.CopyFrom(bytes);
                    using var pending = program.RunDeviceIntoAsync(parameters.Inputs, new[] { destination }, stream);
                    pending.Synchronize(); destination.CopyTo(bytes); Buffer.BlockCopy(bytes, 0, actual, 0, bytes.Length);
                    for (var i = 0; i < elements; i++)
                        if (actual[i] != Math.Max(0, values[i])) throw new Exception($"Worker {worker}, iteration {iteration}, element {i} changed.");
                    completed[worker]++;
                }
            })).ToArray();
            Task.WaitAll(tasks);
            if (completed.Sum() != 1000) throw new Exception("Not all stress iterations completed.");
            File.WriteAllText(Path.Combine(output, "concurrent-device-runs.json"), JsonSerializer.Serialize(new {
                workers = 4, iterations = completed, elementsPerRun = 32768, everyElementCompared = true, elapsedMilliseconds = timer.ElapsedMilliseconds
            }, new JsonSerializerOptions { WriteIndented = true }));
        });
        check("gpu-soak-50-create-compile-early-dispose-memory", () =>
        {
            const int elements = 262144;
            using var hip = new HipRuntime(hipPath); using var stream = hip.CreateStream(HipStreamFlags.NonBlocking);
            using var source = CreateRelu(native, elements);
            var values = Enumerable.Range(0, elements).Select(i => (i % 41 - 20) * .25f).ToArray();
            var expected = values.Select(value => Math.Max(0, value)).ToArray();
            var bytes = new byte[elements * sizeof(float)]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            var samples = new List<object>();
            ulong baseline = 0;
            for (var iteration = 0; iteration < 50; iteration++)
            {
                using (var program = source.Clone())
                {
                    compile(program, false);
                    using var parameters = new DeviceParameters(program, hip); parameters.Input.CopyFrom(bytes);
                    using var pending = program.RunDeviceAsync(parameters.Inputs, stream);
                    parameters.Dispose(); program.Dispose();
                    pending.Synchronize(); RuntimeRegression.Equal(expected, pending.Outputs[0].ToArray<float>());
                }
                if (iteration is 9 or 29 or 49)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); hip.Synchronize();
                    var used = hip.GetMemoryInfo().UsedBytes;
                    if (iteration == 9) baseline = used;
                    samples.Add(new { completedCycles = iteration + 1, usedGpuBytes = used });
                }
            }
            hip.Synchronize(); var final = hip.GetMemoryInfo().UsedBytes;
            const ulong tolerance = 64UL * 1024 * 1024;
            File.WriteAllText(Path.Combine(output, "resource-cycle-memory.json"), JsonSerializer.Serialize(new {
                cycles = 50, elementsPerRun = elements, baselineAfterWarmupBytes = baseline, finalUsedBytes = final,
                allowedGrowthBytes = tolerance, samples, limitation = "Bounded GPU allocation regression; not a proof of zero leaks or managed/native heap stability."
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (final > baseline + tolerance) throw new Exception("GPU allocation growth exceeded the 64 MiB regression limit.");
        });
    }

    internal static MIGraphXProgram CreateRelu(string native, int elements)
    {
        var program = new MIGraphXProgram(native);
        try
        {
            using var main = program.GetMainModule();
            using var input = main.AddParameter("input", new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, elements }));
            using var args = new MIGraphXInstructions(native, new[] { input }); using var op = MIGraphXOperation.Create(native, "relu");
            using var result = main.AddInstruction(op, args); using var returns = new MIGraphXInstructions(native, new[] { result });
            using var returned = main.AddReturn(returns); return program;
        }
        catch { program.Dispose(); throw; }
    }

    // offloadCopy=false exposes output/scratch parameters as well as model inputs.
    // Supply all compiled parameters, just as the production vision samples do.
    internal sealed class DeviceParameters : IDisposable
    {
        private readonly List<MIGraphXHipDeviceInput> inputs = new();
        internal IReadOnlyList<MIGraphXHipDeviceInput> Inputs => inputs;
        internal HipDeviceMemory Input => inputs.Single(value => value.Name == "input").Memory;
        internal DeviceParameters(MIGraphXProgram program, HipRuntime hip)
        {
            try
            {
                foreach (var pair in program.GetParameterShapes())
                    inputs.Add(new MIGraphXHipDeviceInput(pair.Key, pair.Value, hip.Allocate(checked((ulong)pair.Value.ByteCount))));
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { foreach (var input in inputs) input.Memory.Dispose(); }
    }
}
