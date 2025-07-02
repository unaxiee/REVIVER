import re
import os
from collections import defaultdict

scene = "PickPlaceXYZ"
os.makedirs(scene, exist_ok=True)

# Read original file
with open(f"../Scenes/{scene}.cs", "r") as f:
    lines = f.readlines()

# 1: Capture float input variables
float_input = re.compile(
    r'\s*MemoryFloat\s+(\w+)\s*=\s*MemoryMap\.Instance\.GetFloat\([^,]+,\s*MemoryType\.Input\);'
)
float_inputs = set()
for line in lines:
    match = float_input.match(line)
    if match:
        float_inputs.add(match.group(1))
print(f"Float inputs: {sorted(float_inputs)}")

# 2: Find lines where float input .Value is used
value_usage_pattern = re.compile(r'\b(\w+)\.Value\b')
usage_lines = defaultdict(list)
for i, line in enumerate(lines):
    for match in value_usage_pattern.finditer(line):
        var = match.group(1)
        if var in float_inputs:
            usage_lines[var].append(i)
print(usage_lines)

# 3: Constants to try (integers in [0, 10] + outliers)
test_constants = list(range(0, 11, 5)) + [-1, 11]

# 4: For each constant, generate a new .cs file with all .Value uses replaced
def sanitize(x): return str(x).replace("-", "neg_").replace(".", "_")
for var, line_nums in usage_lines.items():
    for line_idx in line_nums:
        for val in test_constants:
            new_lines = lines.copy()
            val_str = f"{float(val):.1f}f"
            new_lines[line_idx] = new_lines[line_idx].replace(f"{var}.Value", val_str)

            # Construct new class name
            class_suffix = f"{var}_to_{sanitize(val_str)}_L{line_idx+1}"
            new_class_name = f"{scene}_{class_suffix}"
            # Rename class and constructor
            for i, line in enumerate(new_lines):
                if f"class {scene}" in line:
                    new_lines[i] = line.replace(scene, new_class_name)
                elif f"{scene}(" in line:
                    new_lines[i] = line.replace(f"{scene}(", f"{new_class_name}(")

            with open(f"{scene}/{new_class_name}.cs", "w") as f:
                f.writelines(new_lines)
