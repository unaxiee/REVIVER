import re
from collections import defaultdict

# Read original file
with open("Scenes/SortingHeightAdvanced.cs", "r") as f:
    lines = f.readlines()

# Match: <stateVar> = State.<StateX>;
state_pattern = re.compile(r"(\s*)(\w*State)\s*=\s*State\.(State\w+);")
# Dictionary to hold per-variable valid states
state_enum_map = defaultdict(set)
for line in lines:
    match_state = state_pattern.search(line)
    if match_state:
        _, var_name, state_value = match_state.groups()
        state_enum_map[var_name].add(state_value)
# Convert sets to sorted lists
state_enum_map = {k: sorted(v) for k, v in state_enum_map.items()}


# Regex patterns
output_pattern = re.compile(r"(\s*)(\w+)\.Value\s*=\s*(.+?);")
internal_pattern = re.compile(r"(\s*)(bool|var)\s+(\w+)\s*=\s*([^;]+);")

# State to track if we are inside Execute()
inside_execute = False
brace_count = 0

# Record line numbers and original lines
assignments = []
for i, line in enumerate(lines):
    stripped = line.strip()

    # Enter Execute() method
    if stripped.startswith("public override void Execute"):
        inside_execute = True
        continue

    if inside_execute:
        brace_count += line.count("{") - line.count("}")
        if brace_count == 0:
            break
            
        if "//" not in line:
            if (m := output_pattern.match(line)):
                indent, var, rhs = m.groups()
                assignments.append(("output", i, indent, var.strip(), rhs.strip()))
            elif (m := state_pattern.match(line)):
                indent, var, rhs = m.groups()
                assignments.append(("state", i, indent, var.strip(), rhs.strip()))
            elif (m := internal_pattern.match(line)):
                indent, vtype, var, rhs = m.groups()
                assignments.append(("internal", i, indent, var.strip(), rhs.strip(), vtype.strip()))

print(f"Found {len(assignments)} assignments.")

# Create a version for each assignment
for assignment in assignments:
    atype = assignment[0]
    line_num = assignment[1]
    indent = assignment[2]
    var = assignment[3]
    rhs = assignment[4]
    vtype = assignment[5] if len(assignment) > 5 else ''

    if atype == 'state':
        valid_states = state_enum_map[var]
        alter_states = [s for s in valid_states if s != rhs]

        for alter_state in alter_states:
            new_lines = lines.copy()
            new_line = f'{indent}{var} = State.{alter_state};\n'
            new_lines[line_num] = new_line

            class_suffix = f'{var}_{rhs}_to_{alter_state}_L{line_num+1}'
            new_class_name = f'SortingHeightAdvanced_{class_suffix}'
            for i, line in enumerate(new_lines):
                if 'class SortingHeightAdvanced' in line:
                    new_lines[i] = line.replace('SortingHeightAdvanced', new_class_name)
                elif 'SortingHeightAdvanced(' in line:
                    new_lines[i] = line.replace('SortingHeightAdvanced(', f'{new_class_name}(')

            with open(f'Manipulations/{new_class_name}.cs', 'w') as f:
                f.writelines(new_lines)
        
    else:
        new_lines = lines.copy()
        class_suffix = f'{var}_L{line_num+1}'
        if atype == 'output':
            new_line = f'{indent}{var}.Value = !({rhs});\n'
        elif atype == 'internal':
            new_line = f'{indent}{vtype} {var} = !({rhs});\n'
    
        new_lines[line_num] = new_line

        new_class_name = f'SortingHeightAdvanced_{class_suffix}'
        for i, line in enumerate(new_lines):
            if 'class SortingHeightAdvanced' in line:
                new_lines[i] = line.replace('SortingHeightAdvanced', new_class_name)
            elif 'SortingHeightAdvanced(' in line:
                new_lines[i] = line.replace('SortingHeightAdvanced(', f'{new_class_name}(')

        with open(f'Manipulations/{new_class_name}.cs', 'w') as f:
            f.writelines(new_lines)

print("All versions generated.")
