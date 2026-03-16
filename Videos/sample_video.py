import cv2
import os
import glob
import numpy as np

# Folder with original videos
input_folder = 'FaultInjection/PickPlaceXYZ'

# Folder where all sampled + cropped frames will be saved
output_sample_folder = 'images/sample'
output_last_frame_folder = 'images/last_frame'

# How many frames to sample per video
num_samples = 10

os.makedirs(output_sample_folder, exist_ok=True)
os.makedirs(output_last_frame_folder, exist_ok=True)

# Crop settings
# Remove this many pixels from each side
CROP_TOP = 100
CROP_BOTTOM = 100
CROP_LEFT = 400
CROP_RIGHT = 100

# Threshold for "too similar" to last frame (0–255 grayscale)
# Lower => stricter (must be more different)
SIMILARITY_THRESHOLD = 3.0  # you can try 2.0, 3.0 if this is too strict

video_files = glob.glob(os.path.join(input_folder, '*.mp4'))

for video_path in video_files:
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print(f"Cannot open video: {video_path}")
        continue

    # Use reported frame count only as an upper bound
    reported_total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))

    if reported_total <= 0:
        print(f"Video {video_path} has 0 reported frames, skipping.")
        cap.release()
        continue

    # ---------- Find and save the true last decodable frame ----------
    last_frame = None
    last_index = None

    for idx in range(reported_total - 1, -1, -1):
        cap.set(cv2.CAP_PROP_POS_FRAMES, idx)
        ret, frame = cap.read()
        if ret:
            last_frame = frame
            last_index = idx
            break

    if last_frame is None:
        print(f"Could not decode any frame in {video_path}, skipping.")
        cap.release()
        continue

    video_name = os.path.splitext(os.path.basename(video_path))[0]

    # Crop last frame (or leave uncropped if crop is invalid)
    h_last, w_last = last_frame.shape[:2]
    y1 = max(0, CROP_TOP)
    y2 = min(h_last, h_last - CROP_BOTTOM)
    x1 = max(0, CROP_LEFT)
    x2 = min(w_last, w_last - CROP_RIGHT)

    if y2 > y1 and x2 > x1:
        last_cropped = last_frame[y1:y2, x1:x2]
    else:
        print(f"Invalid crop region for last frame in {video_path}, saving uncropped.")
        last_cropped = last_frame

    last_frame_filename = f"{video_name}_lastframe_orig{last_index}.jpg"
    last_frame_path = os.path.join(output_last_frame_folder, last_frame_filename)
    cv2.imwrite(last_frame_path, last_cropped)

    # Prepare a grayscale version of the last cropped frame for similarity checks
    last_gray = cv2.cvtColor(last_cropped, cv2.COLOR_BGR2GRAY)

    # ---------- Now sample frames from [0, last_index) only ----------
    # Number of frames available for sampling (excluding the last frame)
    usable_count = last_index  # indices 0 .. last_index-1

    if usable_count <= 0:
        print(f"{video_path}: only last frame exists (index {last_index}), no sampled frames.")
        cap.release()
        continue

    if usable_count <= num_samples:
        # Fewer frames than requested: use all of them
        frame_indices = list(range(usable_count))  # 0 .. last_index-1
    else:
        # Enough frames: evenly sample num_samples indices in [0, last_index-1]
        max_index_for_sampling = last_index - 1
        frame_indices = [
            int(i * max_index_for_sampling / (num_samples - 1))
            for i in range(num_samples)
        ]

    sampled_saved = []

    for idx, frame_no in enumerate(frame_indices):
        cap.set(cv2.CAP_PROP_POS_FRAMES, frame_no)
        ret, frame = cap.read()
        if not ret:
            print(f"Failed to read sampled frame {frame_no} from {video_path}")
            continue

        h, w = frame.shape[:2]
        y1 = max(0, CROP_TOP)
        y2 = min(h, h - CROP_BOTTOM)
        x1 = max(0, CROP_LEFT)
        x2 = min(w, w - CROP_RIGHT)

        if y2 <= y1 or x2 <= x1:
            print(f"Invalid crop region for sampled frame {frame_no} in {video_path}")
            continue

        cropped = frame[y1:y2, x1:x2]

        # Convert to grayscale for similarity check
        gray = cv2.cvtColor(cropped, cv2.COLOR_BGR2GRAY)

        # Resize if dimensions differ (just in case)
        if gray.shape != last_gray.shape:
            gray_resized = cv2.resize(gray, (last_gray.shape[1], last_gray.shape[0]))
        else:
            gray_resized = gray

        # Mean absolute difference
        diff = cv2.absdiff(gray_resized, last_gray)
        mean_diff = float(diff.mean())

        if mean_diff < SIMILARITY_THRESHOLD:
            # Too similar to last frame; skip to avoid train–test leakage
            print(
                f"{video_path}: frame {frame_no} too similar to last frame "
                f"(mean diff {mean_diff:.4f}), skipped."
            )
            continue

        output_filename = f"{video_name}_sampled_{idx+1}_orig{frame_no}.jpg"
        output_path = os.path.join(output_sample_folder, output_filename)
        cv2.imwrite(output_path, cropped)
        sampled_saved.append(frame_no)

    cap.release()

    print(
        f"Processed {video_path}, last frame index: {last_index}, "
        f"sampled (orig indices): {sampled_saved}"
    )

print("Done: saved last frames and sampled frames from all videos.")