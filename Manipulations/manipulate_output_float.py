import re
import os
from collections import defaultdict

scene = "PickPlaceXYZ"
os.makedirs(scene, exist_ok=True)

# Read source file
with open(f"../Scenes/{scene}.cs", "r") as f:
    lines = f.readlines()

# 1: Capture float output variables
float_output_pattern = re.compile(r'MemoryFloat\s+(\w+)\s*=\s*MemoryMap\.Instance\.GetFloat\(".*?",\s*MemoryType\.Output\);')
float_outputs = set()
for line in lines:
    match = float_output_pattern.search(line)
    if match:
        float_outputs.add(match.group(1))
print(f"Float outputs: {sorted(float_outputs)}")

# 2: Get Execute() method bounds
execute_start = None
brace_depth = 0
for i, line in enumerate(lines):
    if "public override void Execute" in line:
        execute_start = i
        break
else:
    raise RuntimeError("Execute() method not found.")
for i in range(execute_start, len(lines)):
    brace_depth += lines[i].count("{") - lines[i].count("}")
    if brace_depth == 0 and i > execute_start:
        execute_end = i
        break

# 3: Capture assignments inside Execute() for output float variables
float_assign_pattern = re.compile(
    r"""(?x)
    (?P<indent>\s*)                # indentation
    (?P<var>\w+)\.Value\s*=\s*     # variable name and assignment
    (?P<val>-?\d+(\.(?P<decimal>\d+))?)f  # float value with optional decimal part
    \s*;                           # semicolon
    """
)
float_assign_lines = []
for i in range(execute_start, execute_end + 1):
    match = float_assign_pattern.match(lines[i])
    if match:
        decimal = match.group('decimal') or ''
        float_assign_lines.append((i, match.group("indent"), match.group("var"), float(match.group("val")), len(decimal)))

def frange(start, stop, step):
    while start <= stop + 1e-8:
        yield round(start, 10)
        start += step

def get_alter_values(value, precision):
    if precision == 0:
        values = [i for i in range(0, 11) if i != int(value)]
        values += [-1, 11]
        return values
    elif precision == 1:
        step = 0.1

        fine_start = max(0.0, round(value - 1.5, 2))
        fine_end = min(10.0, round(value + 1.5, 2))
        fine_vals = [
            round(x, 1) for x in frange(fine_start, fine_end, step)
            if abs(x - value) > 1e-8  # exclude original
        ]

        coarse_vals = [
            i for i in range(0, 11)
            if (i < fine_start or i > fine_end) and abs(i - value) > 1e-8
        ]

        return sorted(set(fine_vals + coarse_vals + [-1.0, 11.0]))
    else:
        raise ValueError("Unsupported precision (only 0 or 1 allowed)")


# 4: Generate manipulation variants
for line_idx, indent, var, val, precision in float_assign_lines:
    alter_values = get_alter_values(val, precision)

    for alter_val in alter_values:
        new_lines = lines.copy()
        new_val_str = f"{alter_val:.{precision}f}f"
        new_line = f"{indent}{var}.Value = {new_val_str};\n"
        new_lines[line_idx] = new_line

        class_suffix = f"{var}_{val}f_to_{new_val_str}_L{line_idx+1}".replace("-", "neg_").replace(".", "_")
        new_class_name = f'{scene}_{class_suffix}'
        for i, line in enumerate(new_lines):
            if f"class {scene}" in line:
                new_lines[i] = line.replace(scene, new_class_name)
            elif f"{scene}(" in line:
                new_lines[i] = line.replace(f"{scene}(", f"{new_class_name}(")

        with open(f"{scene}/{new_class_name}.cs", "w") as f:
            f.writelines(new_lines)