from __future__ import annotations

import json
import re
import tomllib
from typing import TYPE_CHECKING

from style_scan import Finding

if TYPE_CHECKING:
    from collections.abc import Callable
    from pathlib import Path

ROOT_ONLY = frozenset(
    {
        ".editorconfig",
        ".prettierignore",
        ".shellcheckrc",
        "Directory.Build.props",
        "Directory.Build.targets",
        "PSScriptAnalyzerSettings.psd1",
        "clippy.toml",
        "pyrightconfig.json",
        "ruff.toml",
        "rustfmt.toml",
    }
)
ROOT_ONLY_PATTERN = re.compile(r"^(?:eslint|prettier)\.config\.[cm]?[jt]s$")
NEVER_NAMES = (
    r"\.eslintrc.*",
    r"\.eslintignore",
    r"\.prettierrc.*",
    r"\.ruff\.toml",
    r"\.rustfmt\.toml",
    r"\.clippy\.toml",
    r"\.globalconfig",
    r"GlobalSuppressions\.cs",
    r".*\.ruleset",
)
NEVER = re.compile("^(?:" + "|".join(NEVER_NAMES) + ")$")
PROJECT_FILE = re.compile(r"\.(?:csproj|props|targets)$")
PROJECT_DOWNGRADES = (
    r"<(?:NoWarn|WarningsNotAsErrors|CodeAnalysisRuleSet)\b",
    r"<Analysis(?:Level|Mode)\w*\b[^>]*>(?!\s*(?:latest-all|All)\s*<)",
    r"\bNoWarn\s*=",
    r"<(?:TreatWarningsAsErrors|EnforceCodeStyleInBuild|RunAnalyzers\w*|GenerateDocumentationFile)>\s*false",
    r"<Nullable>\s*(?:disable|warnings|annotations)",
    r"<WarningLevel>\s*[0-3]\s*<",
    r"<Features>[^<]*\brun-nullable-analysis\s*=\s*never",
    r"<(?:EditorConfigFiles|GlobalAnalyzerConfigFiles)\s+(?:Include|Remove|Update)\b",
)
PROJECT_DOWNGRADE = re.compile("|".join(PROJECT_DOWNGRADES), re.IGNORECASE)


def read_toml(path: Path) -> dict[str, object]:
    try:
        return tomllib.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, tomllib.TOMLDecodeError):
        return {}


def read_json(path: Path) -> object:
    try:
        loaded: object = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return None
    return loaded


def has_key(value: object, *keys: str) -> bool:
    return isinstance(value, dict) and any(key in value for key in keys)


def pyproject_problem(path: Path, _root: Path) -> str | None:
    if has_key(read_toml(path).get("tool"), "ruff", "pyright"):
        return "[tool.ruff] or [tool.pyright] overrides the baseline (GEN-5)"
    return None


def package_problem(path: Path, _root: Path) -> str | None:
    if has_key(read_json(path), "eslintConfig", "prettier"):
        return "eslintConfig or prettier key overrides the baseline (GEN-5)"
    return None


def workspace_has_lints(folder: Path, root: Path) -> bool:
    while True:
        workspace = read_toml(folder / "Cargo.toml").get("workspace")
        if isinstance(workspace, dict):
            return "lints" in workspace
        if folder in {root, folder.parent}:
            return False
        folder = folder.parent


def cargo_problem(path: Path, root: Path) -> str | None:
    manifest = read_toml(path)
    lints = manifest.get("lints")
    if lints == {"workspace": True}:
        return None
    if lints is not None and path.parent != root:
        return "[lints] other than workspace = true overrides the baseline (GEN-5)"
    if "package" in manifest and workspace_has_lints(path.parent, root):
        return "[package] lacks [lints] workspace = true, so workspace lints don't apply (GEN-5)"
    return None


CONTENT_CHECKS: dict[str, Callable[[Path, Path], str | None]] = {
    "pyproject.toml": pyproject_problem,
    "package.json": package_problem,
    "Cargo.toml": cargo_problem,
}


def check_project(path: str, root: Path) -> list[Finding]:
    text = (root / path).read_text(encoding="utf-8-sig", errors="replace")
    return [
        Finding(path, text.count("\n", 0, match.start()) + 1, "project setting loosens the baseline (GEN-4)")
        for match in PROJECT_DOWNGRADE.finditer(text)
    ]


def check_config(path: str, root: Path) -> list[Finding]:
    name = path.rsplit("/", 1)[-1]
    nested = "/" in path
    if NEVER.match(name):
        return [Finding(path, 0, "lint or format config the baseline doesn't use (GEN-5)")]
    if nested and (name in ROOT_ONLY or ROOT_ONLY_PATTERN.match(name)):
        return [Finding(path, 0, "nested lint or format config overrides the baseline (GEN-5)")]
    if PROJECT_FILE.search(name):
        return check_project(path, root)
    check = CONTENT_CHECKS.get(name)
    message = check(root / path, root) if check else None
    return [Finding(path, 0, message)] if message else []
