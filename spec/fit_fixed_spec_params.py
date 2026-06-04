#!/usr/bin/env python3
"""
Fit STL parameters for a fixed manual spec template.

This script keeps the manual STL structure fixed, ignores all numeric values
from the manual spec, and re-optimizes thresholds / temporal bounds from
scratch using the existing SA-based parameter optimizer.

The fitting path is intentionally consistent end to end:
  - each prepared CSV is treated as one sample,
  - all samples are padded to a common horizon T,
  - optimization and evaluation both run on that same padded representation.
"""

from __future__ import annotations

import csv
import json
import os
import re
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple, TYPE_CHECKING

import jax
jax.config.update("jax_default_matmul_precision", "highest")

# jax.config.update("jax_platforms", "cpu")
import jax.numpy as jnp
import numpy as np

if TYPE_CHECKING:
    from omegaconf import DictConfig

try:
    from hydra.utils import to_absolute_path
except ModuleNotFoundError:
    def to_absolute_path(path: str) -> str:
        return os.path.abspath(path)

from src.dag_nodes import Atom, Bool, Effect, Formula, Timed, bind_effect_params, build_effect
from src.dag_search import SAAdapterCfg, make_sa_optimizer_adapter
from src.eval_bindings import make_effect_bundle
from src.param_sa import ParamRegistry
from stl_backend import STLRunner, pred, pred_diff, always, eventually, and_, or_, not_


@dataclass
class LoadedTrace:
    path: str
    signals: np.ndarray  # [T, D]
    raw_label: int
    length: int


@dataclass
class TraceSplit:
    train: List[LoadedTrace]
    test: List[LoadedTrace]


def log(msg: str) -> None:
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


def load_spec(spec_path: str) -> Dict[str, Any]:
    with open(spec_path) as f:
        return json.load(f)


def build_static_formula(node: Dict[str, Any]) -> Any:
    ntype = node["node_type"]

    if ntype == "atom":
        if "dim2" in node:
            return pred_diff(dim1=node["dim"], dim2=node["dim2"], cmp=node["cmp"], thr=node.get("threshold", 0.0))
        return pred(dim=node["dim"], cmp=node["cmp"], thr=node["threshold"])

    if ntype == "timed":
        sub = build_static_formula(node["sub"])
        interval = node["interval"]
        if "value_indices" in interval:
            bounds = interval["value_indices"]
        else:
            raise ValueError("interval missing value_indices")
        if node["op"] == "always":
            return always(sub, bounds)
        if node["op"] == "eventually":
            return eventually(sub, bounds)
        raise ValueError(f"Unknown timed op: {node['op']}")

    if ntype == "bool":
        left = build_static_formula(node["left"])
        right = build_static_formula(node["right"])
        if node["op"] == "and":
            return and_(left, right)
        if node["op"] == "or":
            return or_(left, right)
        raise ValueError(f"Unknown bool op: {node['op']}")

    if ntype == "not":
        return not_(build_static_formula(node["sub"]))

    raise ValueError(f"Unknown node_type: {ntype}")


def resolve_intervals(node: Dict[str, Any], T: int) -> Dict[str, Any]:
    t = node["node_type"]
    if t == "atom":
        return node
    if t == "timed":
        iv = node["interval"]
        if iv["kind"] == "length":
            resolved = [T - iv["length"], T - 1]
        elif iv.get("value_indices") is None:
            resolved = [0, T - 1]
        else:
            resolved = iv["value_indices"]
        return {
            **node,
            "interval": {"kind": "bounds", "normalized": False, "value_indices": resolved},
            "sub": resolve_intervals(node["sub"], T),
        }
    if t == "bool":
        return {
            **node,
            "left": resolve_intervals(node["left"], T),
            "right": resolve_intervals(node["right"], T),
        }
    if t == "not":
        return {**node, "sub": resolve_intervals(node["sub"], T)}
    raise ValueError(node)


def collect_csv_paths(directories: Optional[List[str]], explicit_list: Optional[List[str]]) -> List[str]:
    paths: List[str] = []
    if explicit_list:
        paths.extend(explicit_list)
    if directories:
        for d in directories:
            paths.extend(
                os.path.join(d, f) for f in os.listdir(d) if f.endswith(".csv")
            )
    return sorted(set(paths))


def cfg_to_runtime(cfg: DictConfig) -> Tuple[Dict[str, Any], List[str], List[str], SAAdapterCfg]:
    spec_path = to_absolute_path(cfg.spec_path)
    output_path = to_absolute_path(cfg.output_path)
    train_dirs = [to_absolute_path(d) for d in cfg.get("train_dirs", cfg.get("dirs", []))]
    train_csv_files = [to_absolute_path(p) for p in cfg.get("train_csv_files", cfg.get("csv_files", []))]
    test_dirs = [to_absolute_path(d) for d in cfg.get("test_dirs", [])]
    test_csv_files = [to_absolute_path(p) for p in cfg.get("test_csv_files", [])]

    train_csv_paths = collect_csv_paths(train_dirs, train_csv_files)
    if not train_csv_paths:
        raise ValueError("Provide at least one training CSV via `train_dirs`/`train_csv_files` or legacy `dirs`/`csv_files`.")

    test_csv_paths = collect_csv_paths(test_dirs, test_csv_files)
    if not test_csv_paths:
        test_csv_paths = list(train_csv_paths)

    spec = load_spec(spec_path)
    label_mode = cfg.label_mode if cfg.get("label_mode") is not None else spec.get("window_label_mode", "any_attack")

    sa_cfg = SAAdapterCfg(
        iters=int(cfg.sa_cfg.iters),
        step=float(cfg.sa_cfg.step),
        temp0=float(cfg.sa_cfg.temp0),
        decay=float(cfg.sa_cfg.decay),
        seed=int(cfg.sa_cfg.seed),
        multistarts=int(cfg.sa_cfg.multistarts),
        init_strategy=str(cfg.sa_cfg.init_strategy),
        bounds_init=(float(cfg.sa_cfg.bounds_init_a), float(cfg.sa_cfg.bounds_init_b)),
        bounds_step=float(cfg.sa_cfg.bounds_step),
        scalar_step=float(cfg.sa_cfg.scalar_step),
        scalar_init=float(cfg.sa_cfg.scalar_init),
        verbose=bool(cfg.sa_cfg.verbose),
        progress_every=int(cfg.sa_cfg.progress_every),
        selection_mode=str(cfg.sa_cfg.get("selection_mode", "argmin_loss")),
    )

    eval_epsilon_cfg = cfg.get("eval_epsilon", None)
    eval_epsilon = float(cfg.loss.margin) if eval_epsilon_cfg is None else float(eval_epsilon_cfg)

    runtime = {
        "name": str(cfg.get("name", "")),
        "spec_path": spec_path,
        "output_path": output_path,
        "downsample": int(cfg.downsample),
        "label_mode": str(label_mode),
        "pad_mode": str(cfg.pad_mode),
        "force_normalized_bounds": bool(cfg.force_normalized_bounds),
        "positive_raw_label": int(cfg.positive_raw_label),
        "loss_variant": str(cfg.loss.loss_variant),
        "margin": float(cfg.loss.margin),
        "balance_classes": bool(cfg.loss.balance_classes),
        "pos_loss_weight": float(cfg.loss.get("pos_loss_weight", 1.0)),
        "neg_loss_weight": float(cfg.loss.get("neg_loss_weight", 1.0)),
        "length_weight": float(cfg.loss.length_weight),
        "eval_epsilon": eval_epsilon,
    }
    return runtime, train_csv_paths, test_csv_paths, sa_cfg


def aggregate_raw_label(labels: np.ndarray, mode: str) -> int:
    if mode == "any_attack":
        return int(np.any(labels > 0))
    if mode == "last":
        return int(labels[-1] > 0)
    if mode == "majority":
        return int(np.mean(labels > 0) > 0.5)
    raise ValueError(f"Unknown label mode: {mode}")


def read_prepared_trace(path: str, signal_list: Sequence[str], downsample: int, label_mode: str) -> LoadedTrace:
    with open(path, newline="") as f:
        rows = list(csv.DictReader(f))

    if not rows:
        raise ValueError(f"{path}: empty CSV")

    required = {"timestamp", "label"} | set(signal_list)
    missing = required - set(rows[0].keys())
    if missing:
        raise ValueError(f"{path}: missing columns {sorted(missing)}")

    if downsample > 1:
        rows = rows[::downsample]

    rows.sort(key=lambda r: float(r["timestamp"]))
    signals = np.array([[float(r[s]) for s in signal_list] for r in rows], dtype=np.float32)
    labels = np.array([int(r["label"]) for r in rows], dtype=np.int32)
    raw_label = aggregate_raw_label(labels, label_mode)
    return LoadedTrace(path=path, signals=signals, raw_label=raw_label, length=signals.shape[0])


def load_traces(paths: Sequence[str], signal_list: Sequence[str], downsample: int, label_mode: str) -> List[LoadedTrace]:
    return [read_prepared_trace(path, signal_list, downsample, label_mode) for path in paths]


def stratified_split_items(
    items: Sequence[Any],
    *,
    group_keys: Sequence[Any],
    test_fraction: float,
    seed: int,
) -> Tuple[List[Any], List[Any]]:
    if not 0.0 <= test_fraction < 1.0:
        raise ValueError(f"test_fraction must be in [0, 1), got {test_fraction}")

    items = list(items)
    if not items:
        raise ValueError("No items to split.")
    if len(items) != len(group_keys):
        raise ValueError("items and group_keys must have the same length.")

    if test_fraction == 0.0:
        return items, []

    rng = np.random.default_rng(seed)
    grouped: Dict[Any, List[Any]] = {}
    for item, key in zip(items, group_keys):
        grouped.setdefault(key, []).append(item)

    def split_group(group: List[Any]) -> Tuple[List[Any], List[Any]]:
        n = len(group)
        n_test = int(round(n * test_fraction))
        if n > 1:
            n_test = max(1, min(n - 1, n_test))
        else:
            n_test = 0

        order = rng.permutation(n)
        test_idx = set(order[:n_test].tolist())
        train_group = [group[i] for i in range(n) if i not in test_idx]
        test_group = [group[i] for i in range(n) if i in test_idx]
        return train_group, test_group

    train: List[Any] = []
    test: List[Any] = []
    for _, group in sorted(grouped.items(), key=lambda kv: str(kv[0])):
        train_group, test_group = split_group(group)
        train.extend(train_group)
        test.extend(test_group)
    rng.shuffle(train)
    rng.shuffle(test)
    return train, test


def split_traces(
    traces: Sequence[LoadedTrace],
    *,
    positive_raw_label: int,
    test_fraction: float,
    seed: int,
) -> TraceSplit:
    traces = list(traces)
    if not traces:
        raise ValueError("No traces to split.")

    group_keys = [int(t.raw_label == positive_raw_label) for t in traces]
    train, test = stratified_split_items(
        traces,
        group_keys=group_keys,
        test_fraction=test_fraction,
        seed=seed,
    )
    return TraceSplit(train=train, test=test)




def count_split_labels(traces: Sequence[LoadedTrace], positive_raw_label: int) -> Tuple[int, int]:
    n_pos = sum(1 for t in traces if t.raw_label == positive_raw_label)
    n_neg = len(traces) - n_pos
    return n_pos, n_neg


def pad_signal_traces(traces: Sequence[Any], *, pad_mode: str) -> Tuple[jnp.ndarray, List[str], np.ndarray, int]:
    if not traces:
        raise ValueError("No traces to pad.")

    T = max(t.length for t in traces)
    D = traces[0].signals.shape[1]
    X = np.zeros((len(traces), T, D), dtype=np.float32)
    lengths = np.zeros((len(traces),), dtype=np.int32)
    names: List[str] = []

    for i, trace in enumerate(traces):
        sig = trace.signals
        cur_T = sig.shape[0]
        X[i, :cur_T, :] = sig
        if cur_T < T:
            if pad_mode != "last":
                raise ValueError(f"Unsupported pad_mode: {pad_mode}")
            X[i, cur_T:, :] = sig[-1]
        lengths[i] = cur_T
        names.append(os.path.basename(trace.path))

    return jnp.array(X), names, lengths, T


def pad_traces(traces: Sequence[LoadedTrace], *, pad_mode: str) -> Tuple[jnp.ndarray, jnp.ndarray, List[str], int, np.ndarray]:
    X, names, lengths, T = pad_signal_traces(traces, pad_mode=pad_mode)
    raw_y = jnp.array([t.raw_label for t in traces], dtype=jnp.int32)
    return X, raw_y, names, T, lengths


def raw_to_fit_labels(raw_y: jnp.ndarray, positive_raw_label: int) -> jnp.ndarray:
    return (raw_y == int(positive_raw_label)).astype(jnp.int32)


def build_data_bounded_registry(
    effect: Effect,
    *,
    key_prefix: str,
    signal_lo: np.ndarray,
    signal_hi: np.ndarray,
) -> ParamRegistry:
    """
    Rebuild the parameter registry with scalar thresholds bounded by observed
    signal ranges. This keeps the existing optimizer flow intact while making
    SA search in a much more meaningful region.
    """
    res = build_effect(effect, key_prefix=key_prefix)
    reg = ParamRegistry()

    for desc in res.param_descs:
        if desc.kind == "scalar":
            m = re.search(r"thr_d(\d+)", desc.key)
            if m is None:
                raise ValueError(f"Could not infer signal dimension from scalar key: {desc.key}")
            dim = int(m.group(1))
            lo = float(signal_lo[dim])
            hi = float(signal_hi[dim])
            span = max(hi - lo, 1e-3)
            margin = 0.05 * span
            reg.add_scalar(desc.key, lo=lo - margin, hi=hi + margin)
        elif desc.kind == "bounds":
            reg.add_bounds(desc.key, normalized=desc.normalized)
        elif desc.kind == "left_fixed_bound":
            reg.add_left_fixed_bound(desc.key, fixed=float(desc.fixed or 0.0), normalized=desc.normalized)
        elif desc.kind == "right_fixed_bound":
            reg.add_right_fixed_bound(desc.key, fixed=float(desc.fixed or 0.0), normalized=desc.normalized)
        else:
            raise ValueError(f"Unknown parameter descriptor kind: {desc.kind}")

    return reg


def import_formula_structure(node: Dict[str, Any], *, force_normalized_bounds: bool) -> Formula:
    ntype = node["node_type"]

    if ntype == "atom":
        if "dim2" in node:
            raise NotImplementedError("Signal-to-signal atoms are not supported by the fixed-structure importer yet.")
        return Atom(dim=int(node["dim"]), cmp=str(node["cmp"]))

    if ntype == "timed":
        return Timed(
            op=str(node["op"]),
            sub=import_formula_structure(node["sub"], force_normalized_bounds=force_normalized_bounds),
            normalized=bool(force_normalized_bounds),
        )

    if ntype == "bool":
        return Bool(
            op=str(node["op"]),
            left=import_formula_structure(node["left"], force_normalized_bounds=force_normalized_bounds),
            right=import_formula_structure(node["right"], force_normalized_bounds=force_normalized_bounds),
        )

    if ntype == "not":
        raise NotImplementedError("Negation is not supported by the internal Effect structure yet.")

    raise ValueError(f"Unknown node_type: {ntype}")


def evaluate_dynamic_formula(
    runner: STLRunner,
    Phi: Any,
    X: jnp.ndarray,
    y_fit: jnp.ndarray,
    params: Dict[str, Any],
    eval_epsilon: float,
) -> Dict[str, Any]:
    rho = runner.robustness(Phi, X, **params)
    pred = (rho > eval_epsilon).astype(jnp.int32)
    acc = float(jnp.mean((pred == y_fit).astype(jnp.float32)))

    n_pos = int(jnp.sum(y_fit == 1))
    n_neg = int(jnp.sum(y_fit == 0))
    tpr = float(jnp.mean((pred[y_fit == 1] == 1).astype(jnp.float32))) if n_pos > 0 else float("nan")
    tnr = float(jnp.mean((pred[y_fit == 0] == 0).astype(jnp.float32))) if n_neg > 0 else float("nan")
    fpr = float(jnp.mean((pred[y_fit == 0] == 1).astype(jnp.float32))) if n_neg > 0 else float("nan")
    fnr = float(jnp.mean((pred[y_fit == 1] == 0).astype(jnp.float32))) if n_pos > 0 else float("nan")

    return {
        "acc": acc,
        "TPR": tpr,
        "TNR": tnr,
        "FPR": fpr,
        "FNR": fnr,
        "n_pos": n_pos,
        "n_neg": n_neg,
        "rho": np.asarray(rho),
        "pred": np.asarray(pred),
    }


def empty_eval_stats() -> Dict[str, Any]:
    return {
        "acc": float("nan"),
        "TPR": float("nan"),
        "TNR": float("nan"),
        "FPR": float("nan"),
        "FNR": float("nan"),
        "n_pos": 0,
        "n_neg": 0,
        "rho": np.asarray([], dtype=np.float32),
        "pred": np.asarray([], dtype=np.int32),
    }


def evaluate_manual_baseline(
    runner: STLRunner,
    formula_node: Dict[str, Any],
    X: jnp.ndarray,
    y_fit: jnp.ndarray,
    T: int,
    eval_epsilon: float,
) -> Dict[str, Any]:
    bound_formula = build_static_formula(resolve_intervals(formula_node, T))
    rho = runner.robustness(bound_formula, X)
    pred = (rho > eval_epsilon).astype(jnp.int32)
    acc = float(jnp.mean((pred == y_fit).astype(jnp.float32)))

    n_pos = int(jnp.sum(y_fit == 1))
    n_neg = int(jnp.sum(y_fit == 0))
    tpr = float(jnp.mean((pred[y_fit == 1] == 1).astype(jnp.float32))) if n_pos > 0 else float("nan")
    tnr = float(jnp.mean((pred[y_fit == 0] == 0).astype(jnp.float32))) if n_neg > 0 else float("nan")
    fpr = float(jnp.mean((pred[y_fit == 0] == 1).astype(jnp.float32))) if n_neg > 0 else float("nan")
    fnr = float(jnp.mean((pred[y_fit == 1] == 0).astype(jnp.float32))) if n_pos > 0 else float("nan")

    return {
        "acc": acc,
        "TPR": tpr,
        "TNR": tnr,
        "FPR": fpr,
        "FNR": fnr,
        "n_pos": n_pos,
        "n_neg": n_neg,
        "rho": np.asarray(rho),
        "pred": np.asarray(pred),
    }


def make_exportable_spec(
    *,
    template_spec: Dict[str, Any],
    effect: Effect,
    params: Dict[str, Any],
    signal_list: Sequence[str],
    window_size: int,
    train_fit_stats: Dict[str, Any],
    test_fit_stats: Dict[str, Any],
    train_baseline_stats: Dict[str, Any],
    test_baseline_stats: Dict[str, Any],
    pad_mode: str,
    positive_raw_label: int,
    eval_epsilon: float,
    split_fraction: float,
    train_count: int,
    test_count: int,
) -> Dict[str, Any]:
    bound = bind_effect_params(
        effect=effect,
        params=params,
        key_prefix="e",
        signal_names=list(signal_list),
        include_param_keys=False,
        strict=True,
        window_size=window_size,
    )

    return {
        "version": 1,
        "task_type": "effect_detection",
        "structure_type": "Effect",
        "mode": template_spec.get("mode", "attack"),
        "window_label_mode": template_spec.get("window_label_mode", "any_attack"),
        "pca": {"enabled": False},
        "temporal_diff": {"enabled": False},
        "stl": {
            "signal_list": list(signal_list),
            "window_size": int(window_size),
            "formula": bound["formula"],
            "formula_str": bound["formula_str"],
        },
        "offline_stats": {
            "fit": {
                "acc": train_fit_stats["acc"],
                "TPR": train_fit_stats["TPR"],
                "TNR": train_fit_stats["TNR"],
                "n_pos": train_fit_stats["n_pos"],
                "n_neg": train_fit_stats["n_neg"],
            },
            "fit_train": {
                "acc": train_fit_stats["acc"],
                "TPR": train_fit_stats["TPR"],
                "TNR": train_fit_stats["TNR"],
                "n_pos": train_fit_stats["n_pos"],
                "n_neg": train_fit_stats["n_neg"],
            },
            "fit_test": {
                "acc": test_fit_stats["acc"],
                "TPR": test_fit_stats["TPR"],
                "TNR": test_fit_stats["TNR"],
                "n_pos": test_fit_stats["n_pos"],
                "n_neg": test_fit_stats["n_neg"],
            },
            "baseline_manual_spec_on_padded_data": {
                "acc": train_baseline_stats["acc"],
                "TPR": train_baseline_stats["TPR"],
                "TNR": train_baseline_stats["TNR"],
                "n_pos": train_baseline_stats["n_pos"],
                "n_neg": train_baseline_stats["n_neg"],
            },
            "baseline_manual_spec_on_padded_train_data": {
                "acc": train_baseline_stats["acc"],
                "TPR": train_baseline_stats["TPR"],
                "TNR": train_baseline_stats["TNR"],
                "n_pos": train_baseline_stats["n_pos"],
                "n_neg": train_baseline_stats["n_neg"],
            },
            "baseline_manual_spec_on_padded_test_data": {
                "acc": test_baseline_stats["acc"],
                "TPR": test_baseline_stats["TPR"],
                "TNR": test_baseline_stats["TNR"],
                "n_pos": test_baseline_stats["n_pos"],
                "n_neg": test_baseline_stats["n_neg"],
            },
        },
        "fit_metadata": {
            "padding_mode": pad_mode,
            "positive_raw_label": int(positive_raw_label),
            "eval_epsilon": float(eval_epsilon),
            "test_fraction": float(split_fraction),
            "train_count": int(train_count),
            "test_count": int(test_count),
            "manual_parameters_used_as_init": False,
        },
    }


def print_sample_predictions(title: str, names: Sequence[str], raw_y: jnp.ndarray, eval_stats: Dict[str, Any], positive_raw_label: int) -> None:
    print(title, flush=True)
    pred = eval_stats["pred"]
    sat_raw_label = positive_raw_label
    for name, raw, sat in zip(names, np.asarray(raw_y), pred):
        expected = "satisfy" if int(raw) == sat_raw_label else "reject"
        actual = "satisfy" if int(sat) == 1 else "reject"
        status = "OK" if expected == actual else "ERR"
        print(f"  [{status}] raw_label={int(raw)} expected={expected:<7} actual={actual:<7} {name}", flush=True)


def run_with_config(cfg: "DictConfig") -> None:
    t_main = time.perf_counter()
    runtime, train_csv_paths, test_csv_paths, sa_cfg = cfg_to_runtime(cfg)
    log("Loading manual template")
    template = load_spec(runtime["spec_path"])
    signal_list = template["stl"]["signal_list"]
    formula_node = template["stl"]["formula"]

    t0 = time.perf_counter()
    log("Loading prepared training traces")
    train_traces = load_traces(train_csv_paths, signal_list, runtime["downsample"], runtime["label_mode"])
    log(f"Loaded {len(train_traces)} training traces in {time.perf_counter() - t0:.2f}s")
    t0 = time.perf_counter()
    log("Loading prepared testing traces")
    test_traces = load_traces(test_csv_paths, signal_list, runtime["downsample"], runtime["label_mode"])
    log(f"Loaded {len(test_traces)} testing traces in {time.perf_counter() - t0:.2f}s")
    split = TraceSplit(train=list(train_traces), test=list(test_traces))
    train_pos, train_neg = count_split_labels(split.train, runtime["positive_raw_label"])
    test_pos, test_neg = count_split_labels(split.test, runtime["positive_raw_label"])
    log(
        "Split traces: "
        f"train={len(split.train)} "
        f"test={len(split.test)} "
        "(explicit training/testing roots)"
    )
    log(f"Train labels: pos={train_pos} neg={train_neg}")
    log(f"Test labels: pos={test_pos} neg={test_neg}")

    t0 = time.perf_counter()
    log("Padding training traces to a common horizon")
    X_train, raw_y_train, train_names, T_train, train_lengths = pad_traces(split.train, pad_mode=runtime["pad_mode"])
    jax.block_until_ready(X_train)
    log(f"Train batch shape={tuple(X_train.shape)} in {time.perf_counter() - t0:.2f}s")
    y_fit_train = raw_to_fit_labels(raw_y_train, runtime["positive_raw_label"])
    signal_lo = np.asarray(jnp.min(X_train, axis=(0, 1)))
    signal_hi = np.asarray(jnp.max(X_train, axis=(0, 1)))

    if split.test:
        t0 = time.perf_counter()
        log("Padding testing traces to a common horizon")
        X_test, raw_y_test, test_names, T_test, test_lengths = pad_traces(split.test, pad_mode=runtime["pad_mode"])
        jax.block_until_ready(X_test)
        log(f"Test batch shape={tuple(X_test.shape)} in {time.perf_counter() - t0:.2f}s")
        y_fit_test = raw_to_fit_labels(raw_y_test, runtime["positive_raw_label"])
    else:
        X_test = None
        raw_y_test = None
        test_names = []
        T_test = 0
        test_lengths = np.asarray([], dtype=np.int32)
        y_fit_test = None

    log("Importing fixed STL structure")
    effect = Effect(import_formula_structure(formula_node, force_normalized_bounds=runtime["force_normalized_bounds"]))

    runner = STLRunner(padding=runtime["pad_mode"])
    loss_cfg = {
        "loss_variant": runtime["loss_variant"],
        "margin": runtime["margin"],
        "balance_classes": runtime["balance_classes"],
        "pos_loss_weight": runtime["pos_loss_weight"],
        "neg_loss_weight": runtime["neg_loss_weight"],
        "temperature": None,
        "length_weight": runtime["length_weight"],
    }
    bundle = make_effect_bundle(
        effect,
        runner=runner,
        loss_cfg=loss_cfg,
        signals=X_train,
        labels=y_fit_train,
        key_prefix="e",
    )
    log("Building data-bounded parameter registry")
    bundle.registry = build_data_bounded_registry(
        effect,
        key_prefix="e",
        signal_lo=signal_lo,
        signal_hi=signal_hi,
    )

    optimizer = make_sa_optimizer_adapter(sa_cfg)

    log(f"JAX backend={jax.default_backend()} devices={jax.devices()}")
    if runtime["name"]:
        log(f"Config name: {runtime['name']}")
    log(f"Spec template: {runtime['spec_path']}")
    log(f"Training CSV files: {len(train_csv_paths)}")
    log(f"Testing CSV files: {len(test_csv_paths)}")
    log(f"Signals: {signal_list}")
    log(f"Train horizon T: {T_train}")
    log(f"Train lengths: min={int(train_lengths.min())} max={int(train_lengths.max())}")
    if split.test:
        log(f"Test horizon T: {T_test}")
        log(f"Test lengths: min={int(test_lengths.min())} max={int(test_lengths.max())}")
    log(f"Positive label: raw label {runtime['positive_raw_label']} should satisfy the spec")
    log(f"Evaluation epsilon: {runtime['eval_epsilon']}")
    log(
        "Loss weights: "
        f"pos={runtime['pos_loss_weight']:.4f} "
        f"neg={runtime['neg_loss_weight']:.4f} "
        f"balance_classes={runtime['balance_classes']}"
    )
    log(f"Structure size: {effect.size()}")
    log(f"Param count: {bundle.registry.size}")
    log(f"SA config: {sa_cfg}")


    t0 = time.perf_counter()
    log("Starting parameter optimization")
    best_params, best_cost, metrics = optimizer(bundle)
    log(f"Optimization took {time.perf_counter() - t0:.2f}s")
    t0 = time.perf_counter()
    log("Evaluating fitted spec on padded training data")
    train_fit_stats = evaluate_dynamic_formula(runner, bundle.Phi, X_train, y_fit_train, best_params, runtime["eval_epsilon"])
    log(f"Train fitted evaluation took {time.perf_counter() - t0:.2f}s")

    if split.test and X_test is not None and y_fit_test is not None:
        t0 = time.perf_counter()
        log("Evaluating fitted spec on padded testing data")
        test_fit_stats = evaluate_dynamic_formula(runner, bundle.Phi, X_test, y_fit_test, best_params, runtime["eval_epsilon"])
        log(f"Test fitted evaluation took {time.perf_counter() - t0:.2f}s")
    else:
        test_fit_stats = empty_eval_stats()

    log(f"Optimization done: best_cost={best_cost:.6f}")
    log(
        "Fitted spec on padded training data: "
        f"acc={train_fit_stats['acc']:.4f} "
        f"TPR={train_fit_stats['TPR']:.4f} "
        f"TNR={train_fit_stats['TNR']:.4f} "
        f"FPR={train_fit_stats['FPR']:.4f} "
        f"FNR={train_fit_stats['FNR']:.4f}"
    )
    if split.test:
        log(
            "Fitted spec on padded testing data: "
            f"acc={test_fit_stats['acc']:.4f} "
            f"TPR={test_fit_stats['TPR']:.4f} "
            f"TNR={test_fit_stats['TNR']:.4f} "
            f"FPR={test_fit_stats['FPR']:.4f} "
            f"FNR={test_fit_stats['FNR']:.4f}"
        )
    log(f"Optimizer metrics: {metrics}")
    if "best_accuracy_chain" in metrics:
        bac = metrics["best_accuracy_chain"]
        log(
            "Optimizer chain summary: "
            f"selected(loss) acc={metrics.get('selected_chain_acc', float('nan')):.6f} "
            f"loss={metrics.get('selected_chain_loss', float('nan')):.6f}; "
            f"best(acc) acc={bac.get('acc', float('nan')):.6f} "
            f"loss={bac.get('loss', float('nan')):.6f}"
        )
    # print_sample_predictions("Train fitted predictions:", train_names, raw_y_train, train_fit_stats, runtime["positive_raw_label"])
    # if split.test and raw_y_test is not None:
    #     print_sample_predictions("Test fitted predictions:", test_names, raw_y_test, test_fit_stats, runtime["positive_raw_label"])

    t0 = time.perf_counter()
    log("Evaluating manual baseline on padded training data")
    train_baseline_stats = evaluate_manual_baseline(
        runner, formula_node, X_train, y_fit_train, T_train, runtime["eval_epsilon"]
    )
    log(
        "Manual baseline on padded training data: "
        f"acc={train_baseline_stats['acc']:.4f} "
        f"TPR={train_baseline_stats['TPR']:.4f} "
        f"TNR={train_baseline_stats['TNR']:.4f} "
        f"FPR={train_baseline_stats['FPR']:.4f} "
        f"FNR={train_baseline_stats['FNR']:.4f}"
    )
    log(f"Baseline evaluation took {time.perf_counter() - t0:.2f}s")
    if split.test and X_test is not None and y_fit_test is not None:
        t0 = time.perf_counter()
        log("Evaluating manual baseline on padded testing data")
        test_baseline_stats = evaluate_manual_baseline(
            runner, formula_node, X_test, y_fit_test, T_test, runtime["eval_epsilon"]
        )
        log(
            "Manual baseline on padded testing data: "
            f"acc={test_baseline_stats['acc']:.4f} "
            f"TPR={test_baseline_stats['TPR']:.4f} "
            f"TNR={test_baseline_stats['TNR']:.4f} "
            f"FPR={test_baseline_stats['FPR']:.4f} "
            f"FNR={test_baseline_stats['FNR']:.4f}"
        )
        log(f"Test baseline evaluation took {time.perf_counter() - t0:.2f}s")
    else:
        test_baseline_stats = empty_eval_stats()

    log("Exporting fitted spec")
    export_spec = make_exportable_spec(
        template_spec=template,
        effect=effect,
        params=best_params,
        signal_list=signal_list,
        window_size=T_train,
        train_fit_stats=train_fit_stats,
        test_fit_stats=test_fit_stats,
        train_baseline_stats=train_baseline_stats,
        test_baseline_stats=test_baseline_stats,
        pad_mode=runtime["pad_mode"],
        positive_raw_label=runtime["positive_raw_label"],
        eval_epsilon=runtime["eval_epsilon"],
        split_fraction=0.0,
        train_count=len(split.train),
        test_count=len(split.test),
    )

    output_path = Path(runtime["output_path"])
    output_path.parent.mkdir(parents=True, exist_ok=True)
    t0 = time.perf_counter()
    with open(output_path, "w") as f:
        json.dump(export_spec, f, indent=2)
    log(f"Saved fitted spec to {output_path} in {time.perf_counter() - t0:.2f}s")
    log(f"Formula: {export_spec['stl']['formula_str']}")
    log(f"Total runtime: {time.perf_counter() - t_main:.2f}s")


if __name__ == "__main__":
    import hydra
    from omegaconf import DictConfig

    @hydra.main(config_path=os.path.dirname(os.path.abspath(__file__)), config_name="fit_complete", version_base=None)
    def main(cfg: DictConfig) -> None:
        run_with_config(cfg)

    main()
