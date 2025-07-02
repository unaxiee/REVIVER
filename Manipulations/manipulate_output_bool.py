import os
import re

scene = "PickPlaceXYZ"
os.makedirs(scene, exist_ok=True)

# Load source lines
with open(f"../Scenes/{scene}.cs", "r") as f:
    lines = f.readlines()

# 1: Find all output boolean variables
bool_output_pattern = re.compile(
    r'MemoryBit\s+(\w+)\s*=\s*MemoryMap\.Instance\.GetBit\(".*?",\s*MemoryType\.Output\);'
)
bool_outputs = set()
for line in lines:
    match = bool_output_pattern.search(line)
    if match:
        bool_outputs.add(match.group(1))
print(f"Boolean outputs: {sorted(bool_outputs)}")

# 2: Locate Execute() bounds
execute_start = None
brace_depth = 0
for i, line in enumerate(lines):
    if "public override void Execute" in line:
        execute_start = i
        break
else:
    raise RuntimeError("Execute() method not found")
for i in range(execute_start, len(lines)):
    brace_depth += lines[i].count("{") - lines[i].count("}")
    if brace_depth == 0 and i > execute_start:
        execute_end = i
        break

# 3: Find boolean assignments inside Execute()
bool_assign_pattern = re.compile(r"(\s*)(\w+)\.Value\s*=\s*(.+?);")
bool_assign_lines = []  # (line_idx, indent, var_name, original_bool)
for i in range(execute_start, execute_end + 1):
    match = bool_assign_pattern.match(lines[i])
    if match:
        indent, var, rhs = match.groups()
        if var in bool_outputs:
            bool_assign_lines.append((i, indent, var, rhs.strip()))

# 4: Generate negated variants
for line_idx, indent, var, rhs in bool_assign_lines:
    new_lines = lines.copy()
    neg_rhs = f"!({rhs})"
    new_lines[line_idx] = f"{indent}{var}.Value = {neg_rhs};\n"

    new_class_name = f"{scene}_{var}_L{line_idx+1}"
    for i, line in enumerate(new_lines):
        if f"class {scene}" in line:
            new_lines[i] = line.replace(scene, new_class_name)
        elif f"{scene}(" in line:
            new_lines[i] = line.replace(f"{scene}(", f"{new_class_name}(")

    with open(f"{scene}/{new_class_name}.cs", "w") as f:
        f.writelines(new_lines)