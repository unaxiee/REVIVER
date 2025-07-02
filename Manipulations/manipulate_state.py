import re
import os
from collections import defaultdict

scene = "PickPlaceXYZ"
os.makedirs(scene, exist_ok=True)

# Read source file
with open(f"../Scenes/{scene}.cs", "r") as f:
    lines = f.readlines()

# 1: Locate Execute() method boundaries
execute_start = None
brace_count = 0
for idx, line in enumerate(lines):
    if "public override void Execute" in line:
        execute_start = idx
        break
if execute_start is None:
    raise ValueError("Execute() method not found.")
# Count braces to find the end of Execute()
for idx in range(execute_start, len(lines)):
    brace_count += lines[idx].count('{') - lines[idx].count('}')
    if brace_count == 0 and idx > execute_start:
        execute_end = idx
        break
else:
    raise ValueError("Could not find matching closing brace for Execute().")

# 2: Find state assignments within Execute()
state_assign_pattern = re.compile(r"(\s*)(\w+)\s*=\s*State\.(State\w+);")
state_enum_map = defaultdict(set)  # var_name -> set of valid states
state_assign_lines = []  # stores (line_idx, indent, var_name, original_state)

for i in range(execute_start, execute_end + 1):
    match = state_assign_pattern.search(lines[i])
    if match:
        indent, state_name, state_value = match.groups()
        state_enum_map[state_name].add(state_value)
        state_assign_lines.append((i, indent, state_name, state_value))

# 3: Add invalid state to each state variable
for var in state_enum_map:
    state_enum_map[var].add("State31")  # Assume State.Invalid is not used anywhere validly
print(state_enum_map)

# 4: Generate manipulation variants
for line_idx, indent, state_name, original_state in state_assign_lines:
    for replacement in sorted(state_enum_map[state_name]):
        if replacement == original_state:
            continue
        new_lines = lines.copy()
        new_lines[line_idx] = f"{indent}{state_name} = State.{replacement};\n"

        class_suffix = f"{state_name}_{original_state}_to_{replacement}_L{line_idx+1}"
        new_class_name = f"{scene}_{class_suffix}"
        print(new_class_name)
        for i, line in enumerate(new_lines):
            if f"class {scene}" in line:
                new_lines[i] = line.replace(scene, new_class_name)
            elif f"{scene}(" in line:
                new_lines[i] = line.replace(f"{scene}(", f"{new_class_name}(")

        with open(f"{scene}/{new_class_name}.cs", "w") as f:
            f.writelines(new_lines)