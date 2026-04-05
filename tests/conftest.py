import os
import sys
import subprocess
import tempfile
import shutil
from pathlib import Path
import pytest

@pytest.fixture
def temp_dir(tmp_path): return tmp_path

@pytest.fixture
def sample_project(temp_dir):
    project_root = temp_dir / "sample_project"
    project_root.mkdir()
    (project_root / "README.md").write_text("# Sample Project")
    (project_root / "main.py").write_text("print('Hello')")
    return {'root': project_root, 'files': ["README.md", "main.py"]}

def run_script(script_path, args, cwd=None):
    cmd = [sys.executable, str(script_path)] + [str(arg) for arg in args]
    result = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True)
    return {'returncode': result.returncode, 'stdout': result.stdout, 'stderr': result.stderr, 'success': result.returncode == 0}

@pytest.fixture
def bootstrap_script(): return Path("tools/seda_bootstrap.py")

@pytest.fixture
def packer_script(): return Path("tools/seda_packer.py")
