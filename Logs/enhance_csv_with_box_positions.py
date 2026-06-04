#!/usr/bin/env python3
"""
Enhance all CSV files with box position columns.

For each CSV file, this script:
1. Identifies when each box is placed (grab 1→0 transition)
2. Records the (X, Y, Z) position at placement time
3. For each row, adds columns showing the current position of each box
4. Writes enhanced CSV with box position columns
"""

import argparse
import csv
import os
from pathlib import Path

COORD_PRECISION = 6
RELEASE_Z_TOLERANCE = 0.1


def apply_gripper_local_offset(x, y, offset_x, offset_y, c_value):
    """Convert a gripper-local offset into world coordinates based on c rotation."""
    if bool(c_value):
        # When the gripper is rotated 90 degrees, swap the local X/Y offset axes.
        return round(x + offset_y, COORD_PRECISION), round(y + offset_x, COORD_PRECISION)
    return round(x + offset_x, COORD_PRECISION), round(y + offset_y, COORD_PRECISION)


def get_released_z(box_id, current_posZ):
    """Keep current_posZ if close to either pallet layer (5 or 10); otherwise fall back to the box_id default."""
    for layer in (5.0, 10.0):
        if abs(current_posZ - layer) <= RELEASE_Z_TOLERANCE:
            return current_posZ
    return 5.0 if box_id == 2 else 10.0


def enhance_csv_with_box_positions(input_filepath, output_filepath, offset_x=0.0, offset_y=0.0, offset_box_id=None):
    """
    Read CSV, infer box positions with optional X,Y offsets, and write enhanced CSV.

    Args:
        offset_x: X offset to apply to gripper position when grabbing (meters)
        offset_y: Y offset to apply to gripper position when grabbing (meters)
        offset_box_id: if not None, only apply the offset to this specific box id; other boxes use zero offset

    Returns: tuple (success: bool, message: str, num_boxes_placed: int)
    """
    try:
        with open(input_filepath, newline='') as input_file:
            reader = csv.DictReader(input_file)
            if reader.fieldnames is None:
                return False, f"Error reading {input_filepath}: CSV is empty", 0

            fieldnames = list(reader.fieldnames)
            rows = list(reader)
    except Exception as e:
        return False, f"Error reading {input_filepath}: {e}", 0

    required_columns = ['detected', 'grab', 'c', 'posX', 'posY', 'posZ']
    missing_columns = [column for column in required_columns if column not in fieldnames]
    if missing_columns:
        return False, f"Error reading {input_filepath}: missing required columns: {', '.join(missing_columns)}", 0
    
    box_columns = [
        'box0_x', 'box0_y', 'box0_z',
        'box1_x', 'box1_y', 'box1_z',
        'box2_x', 'box2_y', 'box2_z'
    ]
    for column in box_columns:
        if column not in fieldnames:
            fieldnames.append(column)

    # Initialize box position columns with blanks (not placed yet)
    for row in rows:
        for column in box_columns:
            row[column] = ''
    
    # Find grab transitions to detect box placements and handling
    try:
        grab_values = [parse_float(row['grab']) for row in rows]
        detect_values = [parse_float(row['detected']) for row in rows]
        c_values = [parse_float(row['c']) for row in rows]
        posX_values = [parse_float(row['posX']) for row in rows]
        posY_values = [parse_float(row['posY']) for row in rows]
        posZ_values = [parse_float(row['posZ']) for row in rows]
    except ValueError as e:
        return False, f"Error reading {input_filepath}: {e}", 0
    
    # Dictionary to store the released position of each box (once placed on pallet)
    box_released_positions = {0: None, 1: None, 2: None}
    num_boxes_placed = 0
    current_box_id = -1
    episode_start_idx = None  # first row of the current active-grab episode; None if inactive

    for i in range(len(grab_values)):
        current_grab = grab_values[i]
        current_posX = posX_values[i]
        current_posY = posY_values[i]
        current_posZ = posZ_values[i]

        is_active = bool(current_grab) and bool(detect_values[i])

        # Inactive→active: start a new episode
        if is_active and episode_start_idx is None:
            current_box_id += 1
            episode_start_idx = i

        # Active→inactive: real release if grab[i-1]==1, grab[i]==0, detected[i]==1
        if not is_active and episode_start_idx is not None:
            is_release_event = (
                i > 0
                and grab_values[i-1] == 1
                and grab_values[i] == 0
                and bool(detect_values[i])
            )
            if is_release_event and current_box_id in box_released_positions:
                apply_offset = offset_box_id is None or offset_box_id == current_box_id
                release_x, release_y = apply_gripper_local_offset(
                    posX_values[i-1],
                    posY_values[i-1],
                    offset_x if apply_offset else 0.0,
                    offset_y if apply_offset else 0.0,
                    c_values[i-1]
                )
                box_released_positions[current_box_id] = {
                    'x': release_x,
                    'y': release_y,
                    'z': get_released_z(current_box_id, posZ_values[i-1])
                }
                num_boxes_placed = max(num_boxes_placed, current_box_id)
            else:
                # False grab: clear filled held-box rows but keep the id consumed
                if 0 <= current_box_id <= 2:
                    for row_index in range(episode_start_idx, i):
                        rows[row_index][f'box{current_box_id}_x'] = ''
                        rows[row_index][f'box{current_box_id}_y'] = ''
                        rows[row_index][f'box{current_box_id}_z'] = ''
            episode_start_idx = None

        # Set the current row's box positions
        for box_id in range(3):
            if box_released_positions[box_id] is not None:
                # Box has been placed on pallet: use released position (fixed on pallet)
                rows[i][f'box{box_id}_x'] = format_float(box_released_positions[box_id]['x'])
                rows[i][f'box{box_id}_y'] = format_float(box_released_positions[box_id]['y'])
                rows[i][f'box{box_id}_z'] = format_float(box_released_positions[box_id]['z'])
            elif is_active and current_box_id == box_id:
                # Gripper is currently holding this box: use gripper position with offsets
                apply_offset = offset_box_id is None or offset_box_id == current_box_id
                hold_x, hold_y = apply_gripper_local_offset(
                    current_posX,
                    current_posY,
                    offset_x if apply_offset else 0.0,
                    offset_y if apply_offset else 0.0,
                    c_values[i]
                )
                rows[i][f'box{box_id}_x'] = format_float(hold_x)
                rows[i][f'box{box_id}_y'] = format_float(hold_y)
                rows[i][f'box{box_id}_z'] = format_float(current_posZ)
    
    # Write enhanced CSV
    try:
        output_dir = os.path.dirname(str(output_filepath))
        if output_dir:
            os.makedirs(output_dir, exist_ok=True)

        with open(output_filepath, 'w', newline='') as output_file:
            writer = csv.DictWriter(output_file, fieldnames=fieldnames)
            writer.writeheader()
            writer.writerows(rows)

        return True, f"Enhanced CSV written to {output_filepath}", num_boxes_placed + 1
    except Exception as e:
        return False, f"Error writing {output_filepath}: {e}", 0


def parse_float(value):
    if value is None or str(value).strip() == '':
        return 0.0

    return float(value)


def format_float(value):
    return f"{float(value):.{COORD_PRECISION}f}".rstrip('0').rstrip('.')

def main():
    parser = argparse.ArgumentParser(description='Enhance grabState CSV files with inferred box positions.')
    parser.add_argument('--line', '-L', type=int, default=None,
                        help='Only process grabState files for the given manipulation line number (e.g. 87)')
    parser.add_argument('--file', '-f', type=str, default=None,
                        help='Process only the specified CSV file (e.g. PickPlaceXYZ_grabState_State0_to_State1_L87.csv)')
    parser.add_argument('--offset-x', type=float, default=0.0,
                        help='X offset to apply to gripper position when grabbing (meters)')
    parser.add_argument('--offset-box-id', type=int, default=None, choices=[0, 1, 2],
                        help='If set, only apply the X/Y offset to this specific box id (0, 1, or 2)')
    parser.add_argument('--offset-y', type=float, default=0.0,
                        help='Y offset to apply to gripper position when grabbing (meters)')
    parser.add_argument('--var-type', type=str, default='grabState',
                        help='Type of variable files to process (e.g. grabState, boxAtPlace)')
    args = parser.parse_args()

    logs_dir = Path('/Users/yongyu/Desktop/Research/Code/STL_mining/logs_org')
    output_dir = Path('/Users/yongyu/Desktop/Research/Code/STL_mining/logs_enhanced')
    
    # Create output directory if it doesn't exist
    output_dir.mkdir(parents=True, exist_ok=True)
    
    # Find all grabState CSV files
    csv_files = sorted([f for f in logs_dir.glob(f'*_{args.var_type}_*.csv') if not f.name.startswith('.')])
    
    if args.file:
        # Process only the specified file
        specified_file = logs_dir / args.file
        if specified_file.exists():
            csv_files = [specified_file]
            print(f"Processing only specified file: {args.file}")
        else:
            print(f"Error: Specified file '{args.file}' not found in {logs_dir}")
            return
    elif args.line is not None:
        # Filter by line number
        pattern = f'_L{args.line}'
        csv_files = [f for f in csv_files if pattern in f.name]
        print(f"Filtering to manipulation line {args.line} (pattern='{pattern}')")
    
    if not csv_files:
        filter_desc = "specified file" if args.file else f"line {args.line}" if args.line else f"{args.var_type} files"
        print(f"No {args.var_type} CSV files found for the requested filter ({filter_desc}). Nothing to process.")
        return

    print(f"Found {len(csv_files)} CSV file(s) to process")
    print(f"Using X offset: {args.offset_x} meters, Y offset: {args.offset_y} meters")
    print()
    print("=" * 80)
    print("ENHANCING CSV FILES WITH BOX POSITIONS")
    print("=" * 80)
    print()
    
    results = []
    for csv_file in csv_files:
        output_file = output_dir / csv_file.name
        success, message, num_boxes = enhance_csv_with_box_positions(csv_file, output_file, args.offset_x, args.offset_y, args.offset_box_id)
        results.append({
            'file': csv_file.name,
            'success': success,
            'message': message,
            'num_boxes': num_boxes
        })
        
        status = "✓" if success else "✗"
        print(f"{status} {csv_file.name}")
        if num_boxes == 3:
            print(f"  → All 3 boxes placed in this execution")
        elif num_boxes > 0:
            print(f"  → Only {num_boxes} box(es) placed in this execution")
        else:
            print(f"  → No boxes placed in this execution")
    
    print()
    print("=" * 80)
    print("SUMMARY")
    print("=" * 80)
    successful = sum(1 for r in results if r['success'])
    print(f"Successfully processed: {successful}/{len(results)} files")
    print(f"Output directory: {output_dir}")
    print()

if __name__ == '__main__':
    main()
