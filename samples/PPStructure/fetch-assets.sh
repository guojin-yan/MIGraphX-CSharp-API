#!/usr/bin/env bash
set -euo pipefail
root="${1:?Usage: bash fetch-assets.sh /absolute/data/document}"
mkdir -p "$root/models" "$root/demo-inputs"
release='https://github.com/guojin-yan/DeploySharp/releases/download/models-paddleocr'
for file in pp-lcnet-x1-0-doc-ori.onnx pp-doclayout-l.onnx ppocrv5-mobile-det.onnx ppocrv5-mobile-rec.onnx ppocrv5_dict.txt pp-lcnet-x1-0-table-cls.onnx rt-detr-l-wired-cell-det.onnx rt-detr-l-wireless-cell-det.onnx slanext-wired.onnx slanext-wireless.onnx pp-formulanet-plus-s.onnx; do
  curl -fL "$release/$file" -o "$root/models/$file"
done
curl -fL 'https://huggingface.co/PaddlePaddle/PP-FormulaNet_plus-S/resolve/main/inference.yml' -o "$root/models/pp-formulanet-plus-s.inference.yml"
images='https://paddle-model-ecology.bj.bcebos.com/paddlex/imgs/demo_image'
for file in layout.jpg demo_paper.png general_formula_rec_001.png img_rot180_demo.jpg; do curl -fL "$images/$file" -o "$root/demo-inputs/$file"; done
curl -fL "$images/pp_structure_v3_demo.png" -o "$root/demo-inputs/news-page.png"
curl -fL "$images/table_recognition.jpg" -o "$root/demo-inputs/expense-table.jpg"
cp "$(dirname "$0")/documents.json" "$root/documents.json"
