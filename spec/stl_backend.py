# stl_api.py  (or append to your stl_backend.py)
from __future__ import annotations
from typing import Any, Optional, List, Dict, Tuple
import functools
import jax, jax.numpy as jnp
import time

# --- import your nodes exactly as defined in formula.py ---
from src.stljax_formula import (STL_Formula,
    Predicate, LessThan, GreaterThan, Equal,
    Always, Eventually, And, Or, Implies, Negation,
    DifferentiableAlways, DifferentiableEventually,
    EventuallyDyn, AlwaysDyn,
    GreaterThanDyn, LessThanDyn, EqualDyn
)

# =========================
# Policy-agnostic builders
# =========================
def pred(*, dim: int, cmp: str, thr: float) -> Any:
    """
    Build an atomic predicate x[dim] ? thr using your Predicate class and its overloaded comparators.
    """
    P = Predicate(f"x[{dim}]", lambda sig: sig[..., dim])   # sig: [B,T,D] expected
    if cmp == "<":  return LessThan(P, thr)
    if cmp == ">":  return GreaterThan(P, thr)
    if cmp == "==": return Equal(P, thr)
    if cmp == ">=": return GreaterThan(P, thr)   # STL robustness uses lhs-rhs; identical to >
    if cmp == "<=": return LessThan(P, thr)      # identical to <
    raise ValueError(f"unsupported cmp {cmp}")

def pred_diff(*, dim1: int, dim2: int, cmp: str, thr: float = 0.0) -> Any:
    """Build an atomic predicate x[dim1] - x[dim2] ? thr (signal-to-signal comparison)."""
    P = Predicate(f"x[{dim1}]-x[{dim2}]", lambda sig, d1=dim1, d2=dim2: sig[..., d1] - sig[..., d2])
    if cmp == "<":  return LessThan(P, thr)
    if cmp == ">":  return GreaterThan(P, thr)
    if cmp == "==": return Equal(P, thr)
    if cmp == ">=": return GreaterThan(P, thr)
    if cmp == "<=": return LessThan(P, thr)
    raise ValueError(f"unsupported cmp {cmp}")

def pred_dyn(*, dim:int, cmp:str, param_key:str):
    """
    Dynamic comparator builder that matches the static pred() API but reads the
    threshold from kwargs[param_key] at call time.
    """
    # make a dimension selector Predicate, consistent with your style
    P = Predicate(f"x[{dim}]", lambda sig: sig[:, dim])  # sig: [B,T,D] expected
    if cmp == "<" or cmp == "<=":  obj = LessThanDyn(P, param_key=param_key)
    elif cmp == ">" or cmp == ">=":  obj = GreaterThanDyn(P, param_key=param_key)
    elif cmp == "==":  obj = EqualDyn(P, param_key=param_key)
    else: raise ValueError(f"unsupported cmp {cmp} for dynamic predicate")
    return obj

def not_(phi: Any) -> Any:
    return Negation(phi)

def always(phi: Any, interval: List[int,int]) -> Any:
    return Always(phi, list(interval))

def differentiable_always(phi: Any, interval: List[int,int]) -> Any:
    return DifferentiableAlways(phi, list(interval))

def eventually(phi: Any, interval: List[int,int]) -> Any:
    return Eventually(phi, list(interval))

def differentiable_eventually(phi: Any, interval: List[int,int]) -> Any:
    return DifferentiableEventually(phi, list(interval))


def eventually_dyn(phi: Any, *, normalized: bool=False, param_key: str="bounds") -> Any:
    """
    Dynamic-window Eventually. Pass bounds at eval:
        runner.robustness(Phi, signals, **{param_key: (ia, ib)})  # or normalized floats if normalized=True
    Each node should have a UNIQUE param_key; if None, it will default to 'bounds'.
    """
    obj = EventuallyDyn(phi, normalized=normalized)
    obj.param_key = param_key
    return obj

def always_dyn(phi: Any, *, normalized: bool=False, param_key: str="bounds") -> Any:
    """Dynamic-window Always (same semantics as above)."""
    obj = AlwaysDyn(phi, normalized=normalized)
    obj.param_key = param_key
    return obj


def and_(lhs: Any, rhs: Any) -> Any:
    return And(lhs, rhs)

def or_(lhs: Any, rhs: Any) -> Any:
    return Or(lhs, rhs)

def implies(lhs: Any, rhs: Any) -> Any:
    return Implies(lhs, rhs)

def rpstl(phi_c: Any, phi_e: Any, outer: List[int,int]) -> Any:
    """Φ = ◇_[outer](phi_c ⇒ phi_e). Pure AST (no policy)."""
    return eventually(implies(phi_c, phi_e), outer)

def rpstl_dyn(phi_c: Any, phi_e: Any, param_key: str = "rpstl_outer_bounds") -> Any:
    """Dynamic-window rPSTL shell: Φ = ◇_[outer](phi_c ⇒ phi_e). Pure AST (no policy)."""
    return eventually_dyn(implies(phi_c, phi_e), normalized=False, param_key=param_key)

# (optional) a tiny size utility; no subclassing needed
def stl_length(node: Any) -> int:
    n = node.__class__.__name__
    if   n in ("LessThan","GreaterThan","Equal","Predicate","Identity"): return 1
    elif n in ("Negation",):    return 1 + stl_length(node.subformula)
    elif n in ("Always","Eventually", "DifferentiableAlways", "DifferentiableEventually"): return 1 + stl_length(node.subformula)
    elif n in ("And","Or","Implies"):  return 1 + stl_length(node.subformula1) + stl_length(node.subformula2)
    else: return 1  # safe fallback

# =========================
# Runner (policy + JIT/cache)
# =========================
class STLRunner:
    def __init__(self, *, approx_method: str="true",
                       temperature: Optional[float]=None,
                       padding: Optional[str]="last"):
        self.approx_method = approx_method
        self.temperature   = temperature
        self.padding       = padding
        self._cache: Dict[Any, Dict[Tuple[str, Optional[float], Optional[str]], Any]] = {}

    @staticmethod
    def _normalize(signals: jnp.ndarray) -> jnp.ndarray:
        x = signals
        if x.ndim == 1:   x = x[None, :, None]
        elif x.ndim == 2: x = x[None, :, :]
        elif x.ndim == 3: pass
        else: raise ValueError(f"signals.ndim={x.ndim} not in (1,2,3)")
        return x  # (B,T,D)

    def _policy_key(self) -> Tuple[str, Optional[float], Optional[str]]:
        return (self.approx_method,
                float(self.temperature) if self.temperature is not None else None,
                self.padding)

    def _get_compiled(self, Phi: Any):
        key = self._policy_key()
        per_phi = self._cache.get(Phi)
        if per_phi is None:
            per_phi = {}
            self._cache[Phi] = per_phi
        if key in per_phi:
            return per_phi[key]

        # JIT a per-batch applicator. node_kwargs is a PyTree (dict of arrays/tuples).
        @functools.partial(jax.jit, static_argnames=("approx_method","padding"))
        def _apply_one(bsig, node_kwargs, *, approx_method, temperature, padding):
            # Pass policy + dynamic kwargs into the formula
            return Phi.robustness(
                bsig,
                approx_method=approx_method,
                temperature=temperature,
                padding=padding,
                **(node_kwargs or {})
            )

        # vmap over batch; share node_kwargs across the batch call
        @jax.jit
        def _apply_batched(BTD, node_kwargs):
            return jax.vmap(lambda b: _apply_one(
                b, node_kwargs,
                approx_method=self.approx_method,
                temperature=self.temperature,
                padding=self.padding
            ))(BTD)

        per_phi[key] = _apply_batched
        return _apply_batched

    def robustness(self, Phi: Any, signals: jnp.ndarray, **node_kwargs) -> jnp.ndarray:
        """
        Evaluate robustness. For dynamic windows, pass per-node bounds by their param_key:
            e1 = eventually_dyn(..., param_key="e1_bounds")
            a1 = always_dyn(...,    param_key="a1_bounds", normalized=True)
            Phi = or_(e1, a1)
            rho = runner.robustness(Phi, signals,
                                    e1_bounds=(10, 30),      # indices
                                    a1_bounds=(0.2, 0.6))    # normalized floats if normalized=True
        """
        x = self._normalize(signals)      # (B,T,D)
        fn = self._get_compiled(Phi)      # compiled: (B,T,D, node_kwargs)->(B,)
        return fn(x, node_kwargs)

def make_signals(B=64, T=128, D=3, seed=0):
    key = jax.random.PRNGKey(seed)
    t = jnp.linspace(0., 1., T)
    base = jnp.stack([
        jnp.sin(2*jnp.pi*1.0*t),
        jnp.cos(2*jnp.pi*0.5*t + 0.2),
        jnp.sin(2*jnp.pi*0.25*t - 0.1),
    ], axis=1)  # (T,3)
    noise = 0.1 * jax.random.normal(key, (B, T, D))
    return base[None, :, :] + noise  # (B,T,D)

def dynamic_test():
    B,T,D = 128,128,3
    signals = make_signals(B,T,D)

    # Dynamic subformulas with distinct keys
    #   a1: Always over x[0] > 0, with normalized bounds (0..1)
    #   e1: Eventually over x[1] < 0.3, with integer index bounds
    a1 = always_dyn(     pred(dim=0, cmp=">", thr=0.0), normalized=True,  param_key="a1_bounds")
    e1 = eventually_dyn( pred(dim=1, cmp="<", thr=0.3), normalized=False, param_key="e1_bounds")

    # Static part for variety (optional): x[2] < 0.2 with fixed window via normal eventually
    # from stl_backend import eventually
    # e_static = eventually(pred(dim=2, cmp="<", thr=0.2), (0, 96))
    # phi_e = or_(e1, e_static)
    # For this test, keep both dynamic to show independence:
    e2 = eventually_dyn( pred(dim=2, cmp="<", thr=0.2), normalized=False, param_key="e2_bounds")
    phi_e = or_(e1, e2)

    # rPSTL shell: Φ = ◇_[8, 120) ( a1 ⇒ (e1 ∨ e2) ), outer window is static
    Phi = rpstl(a1, phi_e, outer=(8, 120))

    # Runner (policy choice)
    runner = STLRunner(approx_method="true", padding="last")

    # ---- Bounds set #1
    kwargs1 = {
        "a1_bounds": (0.10, 0.60),  # normalized (10%-60% of horizon)
        "e1_bounds": (16, 80),      # integer indices
        "e2_bounds": (0,  96),
    }

    # ---- Bounds set #2 (change each independently)
    kwargs2 = {
    "a1_bounds": (0.95, 0.99),  # very late, very short
    "e1_bounds": (0, 0),        # single index at start
    "e2_bounds": (127, 127),    # single index at end (T-1)
    }

    # kwargs2 = { "a1_bounds": (0.30, 0.90), # tighten/shift the 'always' window
    #            "e1_bounds": (64, 120), # push 'eventually' later
    #            "e2_bounds": (8, 40),   # shrink 'eventually' early
    #            }

    # --- First call (compiles) ---
    t0 = time.time()
    rho1 = runner.robustness(Phi, signals, **kwargs1)
    jax.block_until_ready(rho1)
    t1 = time.time()
    print(f"[dyn] compile time (first call): {t1 - t0:.4f}s")
    print("rho (bounds#1) sample:", [float(rho1[i]) for i in range(min(4, rho1.shape[0]))])

    # --- Second call (same policy & Phi; different bounds → reuse compiled graph) ---
    t0 = time.time()
    rho2 = runner.robustness(Phi, signals, **kwargs2)
    jax.block_until_ready(rho2)
    t1 = time.time()
    print(f"[dyn] eval time (second call, different bounds): {t1 - t0:.4f}s")
    print("rho (bounds#2) sample:", [float(rho2[i]) for i in range(min(4, rho2.shape[0]))])

    # --- Third call (reusing bounds#2 again: another fast eval) ---
    t0 = time.time()
    rho3 = runner.robustness(Phi, signals, **kwargs2)
    jax.block_until_ready(rho3)
    t1 = time.time()
    print(f"[dyn] eval time (third call, same bounds): {t1 - t0:.4f}s")

    # Sanity: robustness changes when bounds change
    delta = jnp.mean(jnp.abs(rho2 - rho1))
    print(f"mean |rho(bounds#2)-rho(bounds#1)| = {float(delta):.6f}")


# ------------------------
# Self test / examples
# ------------------------
if __name__ == "__main__":
    key = jax.random.PRNGKey(0)
    B, T, D = 4, 64, 3

    # Synthetic signals: (B,T,D)
    t = jnp.linspace(0, 1.0, T)
    base = jnp.stack([
        jnp.sin(2 * jnp.pi * (1 + i) * t) for i in range(D)
    ], axis=1)  # (T,D)
    noise = jax.random.normal(key, (B, T, D)) * 0.1
    signals = noise + base  # broadcast to (B,T,D)

    backend = STLRunner(approx_method="true")

    # Build φ_c:  □_[0,8) ( x0 >= 0.0 )
    phi_c = always(
        pred(dim=0, cmp=">=", thr=0.0),
        (0, 8),
    )

    # Build φ_e:  ◇_[2,12) ( x1 < 0.5 )  OR  ◇_[0,10) ( x2 < 0.2 )
    e1 = eventually(pred(dim=1, cmp="<", thr=0.5), (2, 12))
    e2 = eventually(pred(dim=2, cmp="<", thr=0.2), (0, 10))
    phi_e = or_(e1, e2)

    # rPSTL shell: Φ = ◇_[3, 30) ( φ_c ⇒ φ_e )
    Phi = rpstl(phi_c, phi_e, outer=(0, 10))
    print("Spec:", Phi, "Length:", stl_length(Phi))

    # Evaluate robustness per batch element
    start_time = time.time()
    rho = backend.robustness(Phi, signals)  # (B,)
    jax.block_until_ready(rho)
    end_time = time.time()
    print(f"rPSTL robustness (hard): {rho.tolist()}, compile time: {end_time - start_time:.4f} sec")

    start_time = time.time()
    rho = backend.robustness(Phi, signals)  # (B,)
    jax.block_until_ready(rho)
    end_time = time.time()
    print(f"rPSTL robustness (hard): {rho.tolist()}, eval time: {end_time - start_time:.4f} sec")
    # Soft evaluation (for gradient-based local polish)
    soft = STLRunner(approx_method="logsumexp", temperature=10.0, padding="last")
    start_time = time.time()
    rho_soft = soft.robustness(Phi, signals)
    jax.block_until_ready(rho_soft)
    end_time = time.time()
    print(f"rPSTL robustness (soft/logsumexp, τ=10): {rho_soft.tolist()}, compile time: {end_time - start_time:.4f} sec")

    start_time = time.time()
    rho_soft = soft.robustness(Phi, signals)
    jax.block_until_ready(rho_soft)
    end_time = time.time()
    print(f"rPSTL robustness (soft/logsumexp, τ=10): {rho_soft.tolist()}, eval time: {end_time - start_time:.4f} sec")

    # Gradient check w.r.t. a scalar threshold (simple example):
    # Define a tiny closure that rebuilds phi with a variable threshold.
    def rob_vs_thr(thr):
        phi_c2 = always(pred(dim=0, cmp=">=", thr=thr), (0, 10))
        Phi2 = rpstl(phi_c2, phi_e, outer=(3, 30))
        # Use a smoothed backend for differentiation
        return soft.robustness(Phi2, signals).mean()

    grad_thr = jax.grad(rob_vs_thr)(0.0)
    print("d mean ρ / d thr at thr=0:", float(grad_thr))

    # test always_dyn and eventually_dyn
    dynamic_test()