"""
Sample tests for your Fabric deployment.

Run with: pytest tests/
"""

import subprocess
import sys
from pathlib import Path


def test_deployment_validates():
    """udp.yml should pass validation."""
    result = subprocess.run(
        [sys.executable, "-m", "udp_deployment.cli", "validate", "-f", "udp.yml"],
        capture_output=True,
        text=True,
        cwd=Path(__file__).parent.parent,
    )
    assert result.returncode == 0, f"Validation failed: {result.stderr}"


def test_udp_yml_exists():
    """Project should have a udp.yml."""
    assert (Path(__file__).parent.parent / "udp.yml").exists()


def test_notebooks_exist():
    """All notebooks referenced in udp.yml should exist."""
    src_dir = Path(__file__).parent.parent / "notebooks"
    assert src_dir.exists(), "notebooks/ directory not found"
    notebooks = list(src_dir.glob("*.py")) + list(src_dir.glob("*.ipynb"))
    assert len(notebooks) > 0, "No notebooks found in notebooks/"
