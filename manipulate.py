import re

# Read original file
with open("Scenes/SortingHeightAdvanced.cs", "r") as f:
    lines = f.readlines()

# Find all output assignments: X.Value = Y;
pattern = re.compile(r"(\s*)(\w+)\.Value\s*=\s*(.+?);")

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
            
        match = pattern.search(line)
        if match and "//" not in line:
            indent = match.group(1)
            lhs = match.group(2)
            rhs = match.group(3).strip()
            assignments.append((i, indent, lhs, rhs))

print(f"Found {len(assignments)} assignments.")

# Create a version for each assignment
for idx, (target_line_idx, indent, lhs, rhs) in enumerate(assignments):
    new_lines = lines.copy()
        
    # Replace the RHS with !RHS
    new_line = f"{indent}{lhs}.Value = !({rhs});\n"
    new_lines[target_line_idx] = new_line

    # Replace class name
    new_lines = [re.sub(r"class\s+SortingHeightAdvanced", f"class SortingHeightAdvanced_L{target_line_idx+1}", l) for l in new_lines]
    # Replace constructor name
    new_lines = [re.sub(r'\bSortingHeightAdvanced\s*\(', f'SortingHeightAdvanced_L{target_line_idx+1}(', l) for l in new_lines]


    # Save new version
    with open(f"Manipulations/SortingHeightAdvanced_L{target_line_idx+1}.cs", "w") as f:
        f.writelines(new_lines)

print("All versions generated.")
