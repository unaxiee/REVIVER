import re
import os


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


scene = "PickPlaceXYZ"
os.makedirs(scene, exist_ok=True)

# Read original file
with open(f"../Scenes/{scene}.cs", "r") as f:
    lines = f.readlines()

float_pattern = re.compile(
    r"""(?x)
    (?P<indent>\s*)                # indentation
    (?P<var>\w+)\.Value\s*=\s*     # variable name and assignment
    (?P<val>-?\d+(\.(?P<decimal>\d+))?)f  # float value with optional decimal part
    \s*;                           # semicolon
    """
)

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
            if (m := float_pattern.match(line)):
                indent = m.group('indent')
                var = m.group('var')
                value = float(m.group('val'))
                decimal = m.group('decimal') or ''
                precision = len(decimal)
                assignments.append((i, indent, var.strip(), value, precision))

print(f"Found {len(assignments)} assignments.")

# Create a version for each assignment
for assignment in assignments:
    line_num = assignment[0]
    indent = assignment[1]
    var = assignment[2]
    value = assignment[3]
    precision = assignment[4]

    alter_values = get_alter_values(value, precision)

    for alter_val in alter_values:
        new_lines = lines.copy()
        new_val_str = f'{alter_val:.{precision}f}f'
        new_line = f'{indent}{var}.Value = {new_val_str};\n'
        new_lines[line_num] = new_line

        class_suffix = f'{var}_{value}f_to_{new_val_str}_L{line_num+1}'.replace('-', 'neg').replace('.', '_')
        new_class_name = f'{scene}_{class_suffix}'
        for i, line in enumerate(new_lines):
            if f'class {scene}' in line:
                new_lines[i] = line.replace(scene, new_class_name)
            elif f'{scene}(' in line:
                new_lines[i] = line.replace(f'{scene}(', f'{new_class_name}(')

        with open(f'{scene}/{new_class_name}.cs', 'w') as f:
            f.writelines(new_lines)
        

print("All versions generated.")
