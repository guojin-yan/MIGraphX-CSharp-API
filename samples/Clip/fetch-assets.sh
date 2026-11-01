#!/usr/bin/env bash
set -euo pipefail
root="${1:?Usage: bash fetch-assets.sh /absolute/data/clip}"
mkdir -p "$root/models" "$root/demo-inputs"
release='https://github.com/guojin-yan/DeploySharp/releases/download/models-visual.1'
for file in clip-vit-b-32.image-encoder.onnx clip-vit-b-32.text-encoder.onnx clip-vit-b-32.vocab.json clip-vit-b-32.merges.txt clip-vit-b-32.LICENSE; do
  curl -fL "$release/$file" -o "$root/models/$file"
done
curl -fL 'https://images.unsplash.com/photo-1542291026-7eec264c27ff?auto=format&fit=max&w=1000&q=90' -o "$root/demo-inputs/running-shoe.jpg"
curl -fL 'https://images.unsplash.com/photo-1523275335684-37898b6baf30?auto=format&fit=max&w=1000&q=90' -o "$root/demo-inputs/wristwatch.jpg"
curl -fL 'https://images.unsplash.com/photo-1526170375885-4d8ecf77b99f?auto=format&fit=max&w=1000&q=90' -o "$root/demo-inputs/instant-camera.jpg"
cp "$(dirname "$0")/inputs.json" "$root/inputs.json"
