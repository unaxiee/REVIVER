import re
from pathlib import Path
from collections import defaultdict

scene = "AssemblerAnalog"
script_dir = Path(__file__).resolve().parent
output_dir = script_dir / scene
generated_count = 0

# Read original file
with open(script_dir.parent / "Scenes" / f"{scene}.cs", "r") as f:
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

# 3: Constants to try (0, 5, 10 plus outliers)
test_constants = list(range(0, 11, 5)) + [-1, 11]

# 4: Replace a variable's .Value uses on one source line per variant
output_dir.mkdir(exist_ok=True)
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

            with open(output_dir / f"{new_class_name}.cs", "w") as f:
                f.writelines(new_lines)
            generated_count += 1

print(f"Generated {generated_count} float input manipulation variants in {output_dir}")
