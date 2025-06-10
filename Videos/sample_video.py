import cv2
import os
import glob

input_folder = 'SortingHeightAdvanced/BlueSquare/cropped_videos'  # Folder with videos
output_folder = 'SortingHeightAdvanced/BlueSquare/sampled_frames'
num_samples = 5

os.makedirs(output_folder, exist_ok=True)

video_files = glob.glob(os.path.join(input_folder, '*.mp4'))

for video_path in video_files:
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print(f"Cannot open video: {video_path}")
        continue

    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    if total_frames < num_samples:
        print(f"Video {video_path} has fewer than {num_samples} frames, skipping.")
        cap.release()
        continue

    frame_indices = [int(i * (total_frames - 1) / (num_samples - 1)) for i in range(num_samples)]

    video_name = os.path.splitext(os.path.basename(video_path))[0]
    video_output_folder = os.path.join(output_folder, video_name)

    saved_any_frame = False
    saved_frames = []

    for idx, frame_no in enumerate(frame_indices):
        cap.set(cv2.CAP_PROP_POS_FRAMES, frame_no)
        ret, frame = cap.read()
        if not ret:
            print(f"Failed to read frame {frame_no} from {video_path}")
            continue
        if not saved_any_frame:
            os.makedirs(video_output_folder, exist_ok=True)
            saved_any_frame = True
        output_path = os.path.join(video_output_folder, f"frame_{idx+1}.jpg")
        cv2.imwrite(output_path, frame)
        saved_frames.append(idx)

    cap.release()

    if saved_any_frame:
        print(f"Processed {video_path}, saved frames: {saved_frames}")
    else:
        print(f"No frames saved from {video_path}, skipping folder creation.")

print("Done sampling frames from all videos.")
