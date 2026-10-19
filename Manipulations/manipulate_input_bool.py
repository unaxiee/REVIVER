import re
from pathlib import Path
from collections import defaultdict

scene = "AssemblerAnalog"
script_dir = Path(__file__).resolve().parent
output_dir = script_dir / scene
output_dir.mkdir(exist_ok=True)
generated_count = 0

# Read source file
with open(script_dir.parent / "Scenes" / f"{scene}.cs", "r") as f:
    lines = f.readlines()

# 1. Capture boolean input variables
bool_input = re.compile(
    r'\s*MemoryBit\s+(\w+)\s*=\s*MemoryMap\.Instance\.GetBit\([^,]+,\s*MemoryType\.Input\);'
)
# 2. Capture edge trigger variables
edge_trigger = re.compile(r'\s*(FTRIG|RTRIG)\s+(\w+)\s*=\s*new\s+\1\(\);')

bool_inputs = set()
edge_triggers = set()
for line in lines:
    if (m := bool_input.match(line)):
        bool_inputs.add(m.group(1))
    if (m := edge_trigger.match(line)):
        edge_triggers.add(m.group(2))
print(f"Boolean inputs: {sorted(bool_inputs)}")
print(f"Edge trigger variables: {sorted(edge_triggers)}")

# 3. Track usage
value_usage_pattern = re.compile(r'\b(\w+)\.Value\b')
trigger_q_usage_pattern = re.compile(r'\b(\w+)\.Q\b')
bool_usage_lines = defaultdict(list)
trigger_q_usage_lines = defaultdict(list)
for i, line in enumerate(lines):
    if ".CLK" in line:
        continue # skip lines that are CLK input assignments
    for m in value_usage_pattern.finditer(line):
        var = m.group(1)
        if var in bool_inputs:
            bool_usage_lines[var].append(i)
    for m in trigger_q_usage_pattern.finditer(line):
        trig = m.group(1)
        if trig in edge_triggers:
            trigger_q_usage_lines[trig].append(i)
print(bool_usage_lines, trigger_q_usage_lines)

# 4. Create three manipulation variants per matching variable usage line
manip_types = {
    "neg": lambda var: f"!{var}",
    "true": lambda var: "true",
    "false": lambda var: "false"
}
for var, line_nums in bool_usage_lines.items():
    for line_idx in line_nums:
        for manip_type, replace_fn in manip_types.items():
            new_lines = lines.copy()
            # Replace only exact <var>.Value with !<var>.Value
            new_lines[line_idx] = re.sub(
                rf'\b{re.escape(var)}\.Value\b',
                replace_fn(f'{var}.Value'),
                new_lines[line_idx]
            )

            new_class_name = f"{scene}_{var}_{manip_type}_L{line_idx+1}"
            for i, line in enumerate(new_lines):
                if f"class {scene}" in line:
                    new_lines[i] = line.replace(scene, new_class_name)
                elif f"{scene}(" in line:
                    new_lines[i] = line.replace(f"{scene}(", f"{new_class_name}(")

            with open(output_dir / f"{new_class_name}.cs", "w") as f:
                f.writelines(new_lines)
            generated_count += 1


for var, line_nums in trigger_q_usage_lines.items():
    for line_idx in line_nums:
        for manip_type, replace_fn in manip_types.items():
            new_lines = lines.copy()
            # Replace only exact <var>.Q with !<var>.Q
            new_lines[line_idx] = re.sub(
                rf'\b{re.escape(var)}\.Q\b',
                replace_fn(f'{var}.Q'),
                new_lines[line_idx]
            )

            new_class_name = f"{scene}_{var}_{manip_type}_L{line_idx+1}"
            for i, line in enumerate(new_lines):
                if f"class {scene}" in line:
                    new_lines[i] = line.replace(scene, new_class_name)
                elif f"{scene}(" in line:
                    new_lines[i] = line.replace(f"{scene}(", f"{new_class_name}(")

            with open(output_dir / f"{new_class_name}.cs", "w") as f:
                f.writelines(new_lines)
            generated_count += 1

print(f"Generated {generated_count} boolean input manipulation variants in {output_dir}")
