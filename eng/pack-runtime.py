#!/usr/bin/env python3
"""Pack or audit the pinned MIGraphX engine payload; ROCm remains a host dependency."""

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent
MAXIMUM_BYTES = 250 * 1024 * 1024


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read_archive(path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("Duplicate archive paths")
        if any(PurePosixPath(n).is_absolute() or ".." in PurePosixPath(n).parts
               or "\\" in n or ":" in n for n in names):
            raise ValueError("Unsafe archive path")
        if sum(i.file_size for i in archive.infolist()) > 2 * 1024**3:
            raise ValueError("Native archive exceeds the unpacked size limit")
        return {n: archive.read(n) for n in names}


def native_entries(lock, source):
    if digest(source.read_bytes()) != lock["archiveSha256"]:
        raise ValueError("Native source archive hash mismatch")
    entries = read_archive(source)
    expected = {f["path"] for f in lock["files"]}
    if set(entries) != expected:
        raise ValueError("Native source archive differs from the exact allowlist")
    for item in lock["files"]:
        data = entries[item["path"]]
        if len(data) != item["size"] or digest(data) != item["sha256"]:
            raise ValueError("Native source file hash/size mismatch: " + item["path"])
    return entries


def package_entries(lock, source, version, commit):
    payload = native_entries(lock, source)
    package_id = lock["packageId"]
    entries = {}
    for name, data in payload.items():
        target = (f'runtimes/{lock["rid"]}/native/' + name[7:]
                  if name.startswith("native/") else name)
        entries[target] = data
    manifest = {"schemaVersion": 1, "packageId": package_id, "version": version,
                "repositoryCommit": commit, "platform": lock["platform"],
                "nativeVersion": lock["nativeVersion"], "rocmVersion": lock["rocmVersion"],
                "sourceArchiveSha256": lock["archiveSha256"],
                "source": lock["source"], "systemDependencies": lock["systemDependencies"],
                "loaderAliases": lock.get("loaderAliases", {}),
                "files": [{"path": n, "size": len(b), "sha256": digest(b)}
                          for n, b in sorted(entries.items())]}
    entries["native-manifest.json"] = (json.dumps(manifest, indent=2) + "\n").encode()
    entries["README.md"] = (ROOT / "docs/package/Runtime.README.md").read_bytes()
    package = ET.Element("package", xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd")
    metadata = ET.SubElement(package, "metadata")
    for name, value in {"id": package_id, "version": version, "authors": "Guojin Yan",
                        "requireLicenseAcceptance": "false",
                        "description": f'MIGraphX {lock["nativeVersion"]} engine for {lock["platform"]}. Requires matching ROCm {lock["rocmVersion"]} system libraries and an AMD driver. See README for tested GPUs and native limitations.',
                        "readme": "README.md", "tags": "AMD ROCm MIGraphX native runtime",
                        "releaseNotes": "MIGraphX engine distribution for API 1.0.0. Native engine and ROCm versions are recorded separately in native-manifest.json."}.items():
        ET.SubElement(metadata, name).text = value
    ET.SubElement(metadata, "license", type="file").text = "licenses/MIGraphX-LICENSE.txt"
    # Required by NuGet.org for older clients when a packaged license is used.
    ET.SubElement(metadata, "licenseUrl").text = "https://aka.ms/deprecateLicenseUrl"
    ET.SubElement(metadata, "repository", type="git", url="https://github.com/guojin-yan/MIGraphX-CSharp-API", commit=commit)
    entries[package_id + ".nuspec"] = ET.tostring(package, encoding="utf-8", xml_declaration=True)
    # Restore copies native assets to the application's RID directory. On Unix
    # the helper is an executable; NuGet ZIP extraction does not promise its mode.
    if lock["rid"] == "linux-x64":
        project = ET.Element("Project")
        target = ET.SubElement(project, "Target", Name="MIGraphXRuntimeNativeLayout",
                               AfterTargets="Build;Publish",
                               Condition="$([MSBuild]::IsOSPlatform('Linux'))")
        items = ET.SubElement(target, "ItemGroup")
        ET.SubElement(items, "_MIGraphXHelper", Include="$(TargetDir)**/migraphx-hiprtc-driver;$(PublishDir)**/migraphx-hiprtc-driver")
        ET.SubElement(target, "Exec", Command='chmod +x "%(_MIGraphXHelper.Identity)"',
                      Condition="Exists('%(_MIGraphXHelper.Identity)')")
        # ELF plugin loading uses unversioned names, while DT_NEEDED uses
        # SONAMEs. Copies load the C++ registry twice and crash at exit; links
        # must refer to the same inode. NuGet extraction cannot preserve links.
        for alias, canonical in sorted(lock["loaderAliases"].items()):
            if any(not re.fullmatch(r"[A-Za-z0-9_.-]+", n) for n in (alias, canonical)):
                raise ValueError("Invalid native loader alias")
            ET.SubElement(target, "Exec",
                          Command=f'ln -sfn "{canonical}" "%(_MIGraphXHelper.RootDir)%(_MIGraphXHelper.Directory){alias}"',
                          Condition="Exists('%(_MIGraphXHelper.Identity)')")
        entries[f"buildTransitive/{package_id}.targets"] = ET.tostring(project, encoding="utf-8", xml_declaration=True)
    types = ET.Element("Types", xmlns="http://schemas.openxmlformats.org/package/2006/content-types")
    for ext in sorted({Path(n).suffix[1:] for n in entries if Path(n).suffix}):
        ET.SubElement(types, "Default", Extension=ext, ContentType="application/octet-stream")
    ET.SubElement(types, "Default", Extension="rels", ContentType="application/vnd.openxmlformats-package.relationships+xml")
    entries["[Content_Types].xml"] = ET.tostring(types, encoding="utf-8", xml_declaration=True)
    rels = ET.Element("Relationships", xmlns="http://schemas.openxmlformats.org/package/2006/relationships")
    ET.SubElement(rels, "Relationship", Type="http://schemas.microsoft.com/packaging/2010/07/manifest", Target="/" + package_id + ".nuspec", Id="manifest")
    entries["_rels/.rels"] = ET.tostring(rels, encoding="utf-8", xml_declaration=True)
    return entries


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--platform", required=True, choices=["win-x64", "ubuntu.24.04-x64"])
    parser.add_argument("--source-archive", required=True, type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--verify", type=Path)
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+\.\d+", args.version):
        raise ValueError("Expected a stable three-part package version")
    commit = args.commit or subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    if not re.fullmatch(r"[a-f0-9]{40}", commit):
        raise ValueError("Expected a lowercase Git commit SHA")
    lock = json.loads((ROOT / "eng/contracts/native-runtime-sources.json").read_text())["platforms"][args.platform]
    expected = package_entries(lock, args.source_archive, args.version, commit)
    if args.verify:
        if args.verify.stat().st_size > MAXIMUM_BYTES:
            raise ValueError("Runtime nupkg exceeds 250 MiB")
        actual = read_archive(args.verify)
        if actual != expected:
            raise ValueError("Runtime package differs from the locked assets or metadata")
        print("Runtime package audit passed: " + str(args.verify))
        return
    if args.output is None:
        parser.error("--output is required when packing")
    output = args.output.resolve()
    if output == ROOT or ROOT in output.parents:
        raise ValueError("Runtime output must be outside the source checkout")
    output.mkdir(parents=True, exist_ok=True)
    package = output / f'{lock["packageId"]}.{args.version}.nupkg'
    with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(expected.items()):
            info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (0o100755 if name.endswith("migraphx-hiprtc-driver") else 0o100644) << 16
            archive.writestr(info, data)
    if package.stat().st_size > MAXIMUM_BYTES:
        package.unlink()
        raise ValueError("Runtime nupkg exceeds 250 MiB; split the payload before publication")
    print(package)


if __name__ == "__main__":
    main()
