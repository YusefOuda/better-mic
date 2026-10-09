"""Meson helper: builds the dfshim Rust staticlib and copies it into Meson's build dir.

Usage: cargo_build.py <manifest> <cargo-target-dir> <out-dir>
Outputs: <out-dir>/dfshim.lib and <out-dir>/windows.0.52.0.lib (import library that the
windows-targets crate expects the final link to find).
"""
import glob
import os
import shutil
import subprocess
import sys

manifest, target_dir, out_dir = sys.argv[1:4]

cargo = shutil.which("cargo") or os.path.expanduser(r"~\.cargo\bin\cargo.exe")
env = dict(os.environ)
# Static CRT to match micproc.dll (b_vscrt=static_from_buildtype).
env["RUSTFLAGS"] = "-C target-feature=+crt-static"
subprocess.run(
    [cargo, "build", "--release", "--locked", "--manifest-path", manifest, "--target-dir", target_dir],
    env=env,
    check=True,
)
shutil.copy2(os.path.join(target_dir, "release", "dfshim.lib"), os.path.join(out_dir, "dfshim.lib"))

cargo_home = env.get("CARGO_HOME", os.path.expanduser(r"~\.cargo"))
libs = sorted(glob.glob(os.path.join(cargo_home, "registry", "src", "*", "windows_x86_64_msvc-0.52.*", "lib", "windows.0.52.0.lib")))
if not libs:
    sys.exit("windows.0.52.0.lib not found in the cargo registry")
shutil.copy2(libs[-1], os.path.join(out_dir, "windows.0.52.0.lib"))
