#!/usr/bin/env python3
"""Prepare PP-Structure ONNX models for MIGraphX 2.15; keep original files intact.

Requirements: Python 3.10+, onnx 1.20.1, numpy 2.2.6. Run once before C# inference.
SLANeXt is split into its original encoder and recurrent step. C# carries the
hidden state and previous token and stops at EOS, without the export's tensor
array/ScatterND Loop bookkeeping. Model execution remains MIGraphX GPU.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper


def free_names(graph):
    defined = {v.name for v in graph.input} | {v.name for v in graph.initializer}
    defined |= {s for n in graph.node for s in n.output}
    used = {s for n in graph.node for s in n.input if s} | {v.name for v in graph.output}
    for node in graph.node:
        for attribute in node.attribute:
            if attribute.type == onnx.AttributeProto.GRAPH:
                used.update(free_names(attribute.g))
    return used - defined


def prune(graph):
    required = {v.name for v in graph.output}
    keep = []
    for node in reversed(graph.node):
        if required.intersection(node.output):
            keep.append(node)
            required.update(s for s in node.input if s)
            for attribute in node.attribute:
                if attribute.type == onnx.AttributeProto.GRAPH:
                    required.update(free_names(attribute.g))
    del graph.node[:]
    graph.node.extend(reversed(keep))
    weights = [v for v in graph.initializer if v.name in required]
    del graph.initializer[:]
    graph.initializer.extend(weights)


def repair_scalar_reshape(graph):
    constants = {v.name: numpy_helper.to_array(v) for v in graph.initializer}
    for node in graph.node:
        if node.op_type == "Constant":
            for attribute in node.attribute:
                if attribute.name == "value":
                    constants[node.output[0]] = numpy_helper.to_array(attribute.t)
        if node.op_type == "Identity" and node.input[0] in constants:
            constants[node.output[0]] = constants[node.input[0]]
        if node.op_type == "Reshape" and node.input[1] in constants and constants[node.input[1]].size == 0:
            # ONNX Reshape(shape=[]) is a scalar reshape. Squeeze is equivalent
            # for this one-element source and avoids MIGraphX's empty dims bug.
            node.op_type = "Squeeze"
            del node.input[1:]
            del node.attribute[:]


def save(model, path):
    repair_scalar_reshape(model.graph)
    onnx.checker.check_model(model)
    onnx.save(model, path)
    return {"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}


def split_table(model, directory, name):
    graph = model.graph
    loop = next(n for n in graph.node if n.op_type == "Loop")
    body = next(a.g for a in loop.attribute if a.name == "body")
    expected = ["p2o.sub_block.pd_op.add.7.0", "p2o.sub_block.pd_op.argmax.0.0", "p2o.sub_block.pd_op.sigmoid.2.0", "p2o.sub_block.pd_op.add.9.0"]
    if [body.output[i].name for i in [2, 5, 8, 9]] != expected:
        raise ValueError("Unexpected SLANeXt export: output contract changed")
    step = copy.deepcopy(body)
    del step.output[:]
    step.output.extend(copy.deepcopy([body.output[i] for i in [2, 5, 8, 9]]))
    prune(step)
    used = {s for n in step.node for s in n.input}
    state = [v for v in step.input if v.name in used]
    if [v.name for v in state] != ["p2o.pd_op.full_with_tensor.0.0", "p2o.pd_op.full_with_tensor.4.0"]:
        raise ValueError("Unexpected SLANeXt recurrent state")
    outer_weights = {v.name for v in graph.initializer}
    closure = sorted(free_names(step) - outer_weights)
    if closure != ["p2o.pd_op.transpose.54.0"]:
        raise ValueError("Unexpected SLANeXt encoder outputs")
    step.initializer.extend(copy.deepcopy([v for v in graph.initializer if v.name in free_names(step)]))
    del step.input[:]
    step.input.extend([
        helper.make_tensor_value_info("hidden", TensorProto.FLOAT, [1, 512]),
        helper.make_tensor_value_info("token", TensorProto.INT32, [1]),
        helper.make_tensor_value_info("memory0", TensorProto.FLOAT, [1, 256, 512]),
    ])
    mapping = {state[0].name: "hidden", state[1].name: "token", closure[0]: "memory0"}
    for node in step.node:
        for i, value in enumerate(node.input):
            node.input[i] = mapping.get(value, value)
    for value, shape in zip(step.output, [[1, 512], [1], [1, 8], [1, 50]]):
        del value.type.tensor_type.shape.dim[:]
        for dimension in shape:
            value.type.tensor_type.shape.dim.add(dim_value=dimension)
    encoder = copy.deepcopy(graph)
    index = next(i for i, n in enumerate(encoder.node) if n.op_type == "Loop")
    del encoder.node[index:]
    del encoder.output[:]
    encoder.output.append(helper.make_tensor_value_info(closure[0], TensorProto.FLOAT, [1, 256, 512]))
    encoder.input[0].type.tensor_type.shape.dim[0].dim_value = 1
    prune(encoder)
    files = []
    for part, suffix in [(encoder, "encoder"), (step, "step")]:
        if any(n.op_type in ("Loop", "If") for n in part.node) or free_names(part):
            raise ValueError("Unresolved control flow or model inputs")
        derived = helper.make_model(part, opset_imports=model.opset_import, ir_version=model.ir_version)
        files.append(save(derived, directory / f"{name}.{suffix}.onnx"))
    return files


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Folder containing downloaded original ONNX models")
    args = parser.parse_args()
    directory = args.directory.resolve()
    records = []
    for name in ["pp-doclayout-l", "rt-detr-l-wired-cell-det", "rt-detr-l-wireless-cell-det", "slanext-wired", "slanext-wireless"]:
        source = directory / (name + ".onnx")
        model = onnx.load(source)
        outputs = split_table(model, directory, name) if name.startswith("slanext") else [save(model, directory / (name + ".migraphx.onnx"))]
        records.append({"source": source.name, "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(), "outputs": outputs})
        print(name, "prepared", flush=True)
    (directory / "prepared-models.json").write_text(json.dumps(records, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
