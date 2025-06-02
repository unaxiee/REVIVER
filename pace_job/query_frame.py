from transformers import Qwen2_5_VLForConditionalGeneration, AutoTokenizer, AutoProcessor
from qwen_vl_utils import process_vision_info
import os
import numpy as np
print('library imported')


model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
    "Qwen/Qwen2.5-VL-32B-Instruct",
    cache_dir='/storage/home/hcoda1/5/yxie405/p-szonouz6-0/hf_cache',
    torch_dtype="auto",
    device_map="auto"
)
print('model loaded')

processor = AutoProcessor.from_pretrained(
    "Qwen/Qwen2.5-VL-32B-Instruct",
    cache_dir='/storage/home/hcoda1/5/yxie405/p-szonouz6-0/hf_cache'
)
print('processor ready')

base_single_frame_prompt = (
    "You're overlooking a T-shaped system,"
    "with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor)"
    "meeting at a central turntable."
    "Describe where the {complete_item} is in the image without mentioning the movement of the {item}."
)
base_multiple_frames_prompt = (
    "You're overlooking a T-shaped system,"
    "with three conveyors (long horizontal conveyor, short top vertical conveyor, short bottom vertical conveyor)"
    "meeting at a central turntable."
    "Describe where the {complete_item} is in each image. Don't assume the {item} stay at the same place in all images."
)
item_dict = {
    "blue_square": ("blue item with a wooden pallet under it", "blue item"),
    "green_square": ("green item with a wooden pallet under it", "green item"),
    "pallet": ("wooden pallet", "pallet"),
    "white_box": ("white box with a wooden pallet under it", "white box"),
    "yellow_box": ("yellow box with a wooden pallet under it", "yellow box")
}
prompt_text_single_frame_prompt_dict = {
    k: base_single_frame_prompt.format(complete_item=v[0], item=v[1]) for k, v in item_dict.items()
}
prompt_text_multiple_frames_prompt_dict = {
    k: base_multiple_frames_prompt.format(complete_item=v[0], item=v[1]) for k, v in item_dict.items()
}
print('prompt text ready')

log_file = 'output_query_frame.log'

user_define_frames = {
    1: [8],
    2: [0, 8],
    3: [0, 5, 8]
}

for frame_folder in os.listdir('sampled_frames'):
    print(frame_folder)
    with open(log_file, 'a', encoding='utf-8') as f:
        f.write(f"=== Item: {frame_folder} ===\n\n")

    videos = set()
    for frame in os.listdir('sampled_frames/' + frame_folder):
        videos.add(frame.split('_frame_')[0])

    for video in videos:
        print(video)
        with open(log_file, 'a', encoding='utf-8') as f:
            f.write(f"=== Manipulation: {video} ===\n\n")

        # Max 8 images for one A100 80G GPU
        for num_frame in range(1, 9):
            if num_frame <= 3:
                query_frames = user_define_frames[num_frame]
            else:
                query_frames = np.linspace(0, 9, num_frame, dtype=int).tolist()

            if num_frame == 1:
                prompt_text = prompt_text_single_frame_prompt_dict[frame_folder]
            else:
                prompt_text = prompt_text_multiple_frames_prompt_dict[frame_folder]

            # Messages containing a local video path and a text query
            messages = [
                {
                    "role": "user",
                    "content": [
                        *[
                            {"type": "image", "image": f"sampled_frames/{frame_folder}/{video}_frame_{i}.jpg"}
                            for i in query_frames
                        ],
                        {"type": "text", "text": prompt_text},
                    ],
                }
            ]

            # Preparation for inference
            text = processor.apply_chat_template(
                messages, tokenize=False, add_generation_prompt=True
            )
            image_inputs, video_inputs = process_vision_info(messages)
            inputs = processor(
                text=[text],
                images=image_inputs,
                videos=video_inputs,
                padding=True,
                return_tensors="pt",
            )
            inputs = inputs.to("cuda")

            # Inference: Generation of the output
            generated_ids = model.generate(**inputs, max_new_tokens=512)
            generated_ids_trimmed = [
                out_ids[len(in_ids) :] for in_ids, out_ids in zip(inputs.input_ids, generated_ids)
            ]
            output_text = processor.batch_decode(
                generated_ids_trimmed, skip_special_tokens=True, clean_up_tokenization_spaces=False
            )
            
            with open(log_file, 'a', encoding='utf-8') as f:
                f.write(f"=== Number of frames: {num_frame} ===\n")
                for line in output_text:
                    f.write(line + "\n")
                f.write('\n')