#!/usr/bin/env python3
"""
Final multiclass evaluation by max robustness over a set of STL specs.

Configured by YAML, the script:
  1. loads all CSV trajectories from the selected class directories,
  2. optionally splits them into train/test sets stratified by true class,
  3. pads them to a common horizon,
  4. evaluates every trajectory against every spec,
  5. predicts the class with the highest robustness,
  6. reports overall accuracy and per-class accuracy.
"""

from __future__ import annotations

import csv
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Sequence, TYPE_CHECKING

import jax
import jax.numpy as jnp
import numpy as np

from fit_fixed_spec_params import (
    build_static_formula,
    load_spec,
    log,
    pad_signal_traces,
    resolve_intervals,
)
from stl_backend import STLRunner

if TYPE_CHECKING:
    from omegaconf import DictConfig

try:
    from hydra.utils import to_absolute_path
except ModuleNotFoundError:
    def to_absolute_path(path: str) -> str:
        return os.path.abspath(path)


@dataclass
class LoadedTrace:
    path: str
    class_name: str
    signals: np.ndarray  # [T, D]
    length: int

def read_raw_trace(path: str, signal_list: Sequence[str], downsample: int) -> LoadedTrace:
    with open(path, newline="") as f:
        rows = list(csv.DictReader(f))

    if not rows:
        raise ValueError(f"{path}: empty CSV")

    required = {"timestamp_ms"} | set(signal_list)
    missing = required - set(rows[0].keys())
    if missing:
        raise ValueError(f"{path}: missing columns {sorted(missing)}")

    if downsample > 1:
        rows = rows[::downsample]

    rows.sort(key=lambda r: float(r["timestamp_ms"]))
    signals = np.array(
        [[float(r[s]) if r.get(s, "") != "" else -1.0 for s in signal_list] for r in rows],
        dtype=np.float32,
    )
    return LoadedTrace(
        path=path,
        class_name=Path(path).parent.name,
        signals=signals,
        length=signals.shape[0],
    )


def load_dataset(dataset_root: str, classes: Sequence[str], signal_list: Sequence[str], downsample: int) -> List[LoadedTrace]:
    traces: List[LoadedTrace] = []
    for cls in classes:
        class_dir = Path(dataset_root) / cls
        if not class_dir.is_dir():
            raise ValueError(f"Class directory not found: {class_dir}")
        csvs = sorted(class_dir.rglob("*.csv"))
        if not csvs:
            raise ValueError(f"No CSVs found in {class_dir}")
        for csv_path in csvs:
            trace = read_raw_trace(str(csv_path), signal_list, downsample)
            trace.class_name = cls
            traces.append(trace)
    return traces


def evaluate_specs(
    X: jnp.ndarray,
    spec_nodes: Sequence[Dict[str, Any]],
    T: int,
    runner: STLRunner,
) -> np.ndarray:
    rho_cols: List[np.ndarray] = []
    for spec_node in spec_nodes:
        phi = build_static_formula(resolve_intervals(spec_node, T))
        rho = runner.robustness(phi, X)
        rho_cols.append(np.asarray(rho, dtype=np.float32))
    return np.stack(rho_cols, axis=1)  # [N, C]


def predict_classes(rho: np.ndarray, *, eval_epsilon: float, allow_unknown: bool) -> np.ndarray:
    y_pred = np.argmax(rho, axis=1).astype(np.int32)
    if not allow_unknown:
        return y_pred
    best_rho = rho[np.arange(rho.shape[0]), y_pred]
    unknown_idx = rho.shape[1]
    y_pred = y_pred.copy()
    y_pred[best_rho <= eval_epsilon] = unknown_idx
    return y_pred


def format_rate(correct: int, total: int) -> str:
    pct = 100.0 * correct / total if total > 0 else float("nan")
    return f"{pct:.2f}% ({correct}/{total})"


def report_split(
    title: str,
    y_true: np.ndarray,
    y_pred: np.ndarray,
    classes: Sequence[str],
    *,
    show_confusion: bool,
    allow_unknown: bool,
) -> None:
    total = len(y_true)
    overall_correct = int(np.sum(y_pred == y_true))
    log("")
    log(f"{title}:")
    log(f"  Accuracy: {format_rate(overall_correct, total)}")
    if allow_unknown:
        unknown_idx = len(classes)
        unknown_count = int(np.sum(y_pred == unknown_idx))
        log(f"  Unknown rate: {format_rate(unknown_count, total)}")

    log("")
    log(f"{title} per class:")
    for idx, cls in enumerate(classes):
        mask = y_true == idx
        total_i = int(np.sum(mask))
        correct_i = int(np.sum(y_pred[mask] == idx))
        log(f"  {cls}: {format_rate(correct_i, total_i)}")

    if show_confusion:
        pred_labels = list(classes) + (["unknown"] if allow_unknown else [])
        conf = np.zeros((len(classes), len(pred_labels)), dtype=np.int32)
        for yt, yp in zip(y_true, y_pred):
            conf[yt, yp] += 1
        log("")
        log(f"{title} confusion matrix (rows=true, cols=pred):")
        header = "true\\pred".ljust(24) + "".join(cls[:18].ljust(20) for cls in pred_labels)
        log(header)
        for i, cls in enumerate(classes):
            row = cls[:22].ljust(24) + "".join(str(conf[i, j]).ljust(20) for j in range(len(pred_labels)))
            log(row)


def run_with_config(cfg: "DictConfig") -> None:
    train_root_cfg = cfg.get("train_dataset_root", None)
    dataset_root_cfg = cfg.get("dataset_root", None)
    if train_root_cfg is not None:
        train_dataset_root = to_absolute_path(str(train_root_cfg))
    elif dataset_root_cfg is not None:
        train_dataset_root = to_absolute_path(str(dataset_root_cfg))
    else:
        raise ValueError("Config must provide `train_dataset_root` or legacy `dataset_root`.")

    test_root_cfg = cfg.get("test_dataset_root", None)
    test_dataset_root = to_absolute_path(str(test_root_cfg)) if test_root_cfg else train_dataset_root
    classes = [str(cls) for cls in cfg.classes]
    class_to_spec = {str(cls): to_absolute_path(str(path)) for cls, path in cfg.class_specs.items()}
    downsample = int(cfg.get("downsample", 1))
    pad_mode = str(cfg.get("pad_mode", "last"))
    show_confusion = bool(cfg.get("show_confusion", False))
    allow_unknown = bool(cfg.get("allow_unknown", False))
    eval_epsilon = float(cfg.get("eval_epsilon", 0.0))

    missing_specs = [cls for cls in classes if cls not in class_to_spec]
    if missing_specs:
        raise ValueError(f"Missing spec paths for classes: {missing_specs}")

    first_spec = load_spec(class_to_spec[classes[0]])
    signal_list = list(first_spec["stl"]["signal_list"])
    spec_nodes: List[Dict[str, Any]] = []
    for cls in classes:
        spec = load_spec(class_to_spec[cls])
        this_signals = list(spec["stl"]["signal_list"])
        if this_signals != signal_list:
            raise ValueError(f"Signal list mismatch for {cls}: {this_signals} != {signal_list}")
        spec_nodes.append(spec["stl"]["formula"])

    log(f"Config name: {cfg.get('name', 'eval_multiclass')}")
    log(f"Loading training dataset from {train_dataset_root}")
    train_traces = load_dataset(train_dataset_root, classes, signal_list, downsample)
    log(f"Loaded {len(train_traces)} training trajectories across {len(classes)} classes")
    log(f"Loading testing dataset from {test_dataset_root}")
    test_traces = load_dataset(test_dataset_root, classes, signal_list, downsample)
    log(f"Loaded {len(test_traces)} testing trajectories across {len(classes)} classes")
    log(f"Prediction mode: {'satisfy_else_unknown' if allow_unknown else 'argmax'}")
    log(f"Evaluation epsilon: {eval_epsilon}")

    class_to_idx = {cls: i for i, cls in enumerate(classes)}
    log(
        "Split traces: "
        f"train={len(train_traces)} "
        f"test={len(test_traces)} "
        "(explicit training/testing roots)"
    )

    runner = STLRunner(padding=pad_mode)

    def eval_subset(title: str, subset: Sequence[LoadedTrace]) -> None:
        if not subset:
            return
        y_true = np.array([class_to_idx[t.class_name] for t in subset], dtype=np.int32)
        X, _names, lengths, T = pad_signal_traces(subset, pad_mode=pad_mode)
        jax.block_until_ready(X)
        log("")
        log(f"{title} padded batch shape={tuple(X.shape)} with common horizon T={T}")
        log(f"{title} lengths: min={int(lengths.min())} max={int(lengths.max())}")
        rho = evaluate_specs(X, spec_nodes, T, runner)
        y_pred = predict_classes(rho, eval_epsilon=eval_epsilon, allow_unknown=allow_unknown)
        report_split(title, y_true, y_pred, classes, show_confusion=show_confusion, allow_unknown=allow_unknown)

    log(f"Evaluating {len(spec_nodes)} specs on training split")
    eval_subset("Train", train_traces)
    if test_traces:
        log(f"Evaluating {len(spec_nodes)} specs on testing split")
        eval_subset("Test", test_traces)


if __name__ == "__main__":
    import hydra

    @hydra.main(config_path=os.path.dirname(os.path.abspath(__file__)), config_name="eval_multiclass", version_base=None)
    def main(cfg: "DictConfig") -> None:
        run_with_config(cfg)

    main()
